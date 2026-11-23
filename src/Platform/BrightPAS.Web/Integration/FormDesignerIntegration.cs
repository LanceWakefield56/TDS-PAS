using BrightPAS.Licensing;
using BrightPAS.Modules.Forms;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Triage;
using BrightPAS.Web.Licensing;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.IO;

namespace BrightPAS.Web.Integration;

/// <summary>Mapped form answers flow into the record (spec 7.23.1): allergies become patient alerts; measurements become triage findings (and re-run triage).</summary>
public sealed class FormDataSink(LicenceCache licence, PatientService patients, TriageService triage) : IFormDataSink
{
    public async Task<IReadOnlyList<string>> ApplyAsync(FormEntry entry, IReadOnlyDictionary<string, string> mapped, string user, CancellationToken ct)
    {
        var done = new List<string>();
        if (mapped.TryGetValue("allergy", out var allergy) && !string.IsNullOrWhiteSpace(allergy) && !allergy.Equals("no", StringComparison.OrdinalIgnoreCase) && !allergy.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            await patients.AddAlertAsync(entry.PatientId!.Value, AlertType.Allergy, AlertSeverity.Warning, $"Allergy (from form): {allergy}", user, ct);
            done.Add("allergy alert added");
        }
        var facts = mapped.Where(kv => kv.Key != "allergy" && TriageService.StaffFacts.ContainsKey(kv.Key)).Select(kv => new StaffInput(kv.Key, kv.Value, "Recorded on a form")).ToList();
        if (facts.Count > 0 && licence.Status.AccessTo("triage") == ModuleAccess.Full)
        {
            await triage.RunAsync(entry.PatientId!.Value, entry.BookingId, TriageTrigger.NewAnswers, "Form entry", user, facts, null, ct);
            done.Add($"{facts.Count} finding(s) sent to triage");
        }
        return done;
    }
}

/// <summary>Fillable PDF import (spec 7.23.2): reads the AcroForm fields (name, type, options) into a draft form.</summary>
public static class PdfFormImport
{
    public static (List<FieldEdit> Fields, List<string> Errors) Read(byte[] pdf)
    {
        var fields = new List<FieldEdit>();
        try
        {
            using var ms = new MemoryStream(pdf);
            using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
            var form = doc.AcroForm;
            if (form is null || form.Fields.Count == 0) return (fields, ["This PDF has no fillable fields. Scanned and flat forms need to be set up in the designer (or as a CSV question list)."]);
            void Walk(PdfAcroField.PdfAcroFieldCollection list)
            {
                for (var i = 0; i < list.Count; i++)
                {
                    var f = list[i];
                    if (f.HasKids) { Walk(f.Fields); continue; }
                    var label = string.IsNullOrWhiteSpace(f.Name) ? $"Field {fields.Count + 1}" : f.Name.Replace('_', ' ').Trim();
                    switch (f)
                    {
                        case PdfCheckBoxField: fields.Add(new FieldEdit(null, null, label, FieldType.YesNo, false, null, null, null, null, null, null, null, null)); break;
                        case PdfRadioButtonField r: fields.Add(new FieldEdit(null, null, label, FieldType.Choice, false, null, null, null, null, null, null, null, Options(r))); break;
                        case PdfComboBoxField c: fields.Add(new FieldEdit(null, null, label, FieldType.Choice, false, null, null, null, null, null, null, null, Options(c))); break;
                        case PdfListBoxField l: fields.Add(new FieldEdit(null, null, label, FieldType.MultiChoice, false, null, null, null, null, null, null, null, Options(l))); break;
                        case PdfSignatureField: fields.Add(new FieldEdit(null, null, label, FieldType.Signature, false, null, null, null, null, null, null, null, null)); break;
                        case PdfTextField t: fields.Add(new FieldEdit(null, null, label, t.MultiLine ? FieldType.LongText : label.Contains("date", StringComparison.OrdinalIgnoreCase) ? FieldType.Date : FieldType.Text, false, null, null, null, null, null, null, null, null)); break;
                        default: fields.Add(new FieldEdit(null, null, label, FieldType.Text, false, null, null, null, null, null, null, null, null)); break;
                    }
                    if (fields.Count >= FormDesignerService.MaxFields) return;
                }
            }
            Walk(form.Fields);
        }
        catch (Exception ex) when (ex is PdfReaderException or InvalidOperationException or NotSupportedException or IOException)
        {
            return (fields, ["The PDF could not be read" + (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase) ? " – it is password protected." : ".")]);
        }
        // Choice fields with fewer than two options become text so the draft opens; staff fix them in the designer.
        return ([.. fields.Select(f => f.Type is FieldType.Choice or FieldType.MultiChoice && (f.Options?.Count ?? 0) < 2 ? f with { Type = FieldType.Text, Options = null } : f)], []);
    }

    private static List<OptionEdit> Options(PdfChoiceField c)
    {
        var list = new List<OptionEdit>();
        var opt = c.Elements.GetArray("/Opt");
        if (opt is null) return list;
        for (var i = 0; i < opt.Elements.Count; i++)
        {
            var item = opt.Elements[i];
            var text = item is PdfSharp.Pdf.PdfArray a && a.Elements.Count > 1 ? a.Elements.GetString(1) : (item as PdfSharp.Pdf.PdfString)?.Value;
            if (!string.IsNullOrWhiteSpace(text)) list.Add(new OptionEdit(text, null));
        }
        return list;
    }

    private static List<OptionEdit> Options(PdfRadioButtonField r)
    {
        var list = new List<OptionEdit>();
        var kids = r.Elements.GetArray("/Kids");
        if (kids is null) return list;
        foreach (var k in kids.Elements)
            if ((k as PdfSharp.Pdf.Advanced.PdfReference)?.Value is PdfSharp.Pdf.PdfDictionary d && d.Elements.GetDictionary("/AP")?.Elements.GetDictionary("/N") is { } n)
                foreach (var name in n.Elements.Keys.Where(x => x != "/Off")) list.Add(new OptionEdit(name.TrimStart('/'), null));
        return list;
    }
}
