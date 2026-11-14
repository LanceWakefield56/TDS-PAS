using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Data;
using BrightPAS.Messaging.Hl7;
using BrightPAS.Modules.Billing;
using BrightPAS.Modules.Forms;
using BrightPAS.Modules.Labs;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Theatre;

namespace BrightPAS.Modules.Tests;

public class Hl7Tests
{
    [Fact]
    public void Parses_fields_components_repetitions_and_escapes()
    {
        var m = Hl7Message.Parse("MSH|^~\\&|LAB|AWANUI|PAS|TDS|202611091000||ORU^R01|C1|P|2.4\rPID|1||ZSC21TN^^^NHI^NHI~TDS0000001^^^TDS^MR||NGATAI^Aroha^Mere||19800517|F\rNTE|1||Line one\\.br\\Fe \\T\\ Co \\F\\ x\r");
        Assert.Equal("ORU^R01", m.MessageType);
        Assert.Equal("C1", m.ControlId);
        Assert.Equal("AWANUI", m.SendingFacility);
        var pid = m.First("PID")!;
        Assert.Equal("ZSC21TN", pid.Get(3, 1));
        Assert.Equal("MR", pid.Get(3, 5, 0, 1));
        Assert.Equal("Aroha", pid.Get(5, 2));
        Assert.Equal("Line one\nFe & Co | x", m.First("NTE")!.Get(3));
    }

    [Fact]
    public void Builds_ack_and_mllp_frames()
    {
        var m = Hl7Message.Parse("MSH|^~\\&|LAB|AWANUI|PAS|TDS|202611091000||ORU^R01|C1|P|2.4\r");
        var ack = Hl7Message.Parse(m.Ack("AE", "Bad | data", DateTimeOffset.Now));
        Assert.Equal("ACK", ack.Msh.Get(9, 1));
        Assert.Equal("AE", ack.First("MSA")!.Get(1));
        Assert.Equal("C1", ack.First("MSA")!.Get(2));
        Assert.Equal("Bad   data", ack.First("MSA")!.Get(3));
        var framed = Mllp.Frame("MSH|x");
        Assert.Equal(0x0B, framed[0]);
        Assert.Equal(0x1C, framed[^2]);
    }

    [Fact]
    public async Task Mllp_reads_framed_message()
    {
        using var ms = new MemoryStream([.. "junk"u8.ToArray(), .. Mllp.Frame("MSH|^~\\&|A\rPID|1")]);
        Assert.Equal("MSH|^~\\&|A\rPID|1", await Mllp.ReadAsync(ms, 1000, default));
        Assert.Null(await Mllp.ReadAsync(ms, 1000, default));
    }

    [Fact]
    public void Rejects_non_hl7() => Assert.Throws<Hl7Exception>(() => Hl7Message.Parse("hello"));
}

public class LabTests
{
    private static string Oru(string nhi = "ZSC21TN", string family = "NGATAI", string dob = "19800517", string control = "C1", string status = "F", string flag = "L") =>
        $"MSH|^~\\&|LAB|AWANUI|PAS|TDS|202611091000||ORU^R01|{control}|P|2.4\r" +
        $"PID|1||{nhi}^^^NHI^NHI||{family}^Aroha||{dob}|F\r" +
        $"OBR|1|O1|ACC1|CBC^Full blood count|||202611080800|||||||||123^SMITH^JOHN^^^DR||||||202611091000|||{status}\r" +
        $"OBX|1|NM|718-7^Haemoglobin||98|g/L|115-155|{flag}|||F\rNTE|1||Repeat in 2 weeks\r" +
        "OBX|2|NM|777-3^Platelets||265|x10*9/L|150-400|N|||F\r";

    [Fact]
    public async Task Files_result_against_patient_with_observations_abnormal_flag_and_ack()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<LabService>();
        var r = await svc.IngestAsync(Oru(), "MLLP", "mllp:test");
        Assert.Equal(LabMessageOutcome.Accepted, r.Outcome);
        Assert.Contains("MSA|AA|C1", r.Ack);
        var rep = await s.ServiceProvider.GetRequiredService<LabsDbContext>().Reports.Include(x => x.Observations).SingleAsync();
        Assert.Equal(p.Id, rep.PatientId);
        Assert.True(rep.IsAbnormal);
        Assert.Equal("Full blood count", rep.TestName);
        Assert.Equal("DR JOHN SMITH", rep.RequestedBy);
        Assert.Equal(2, rep.Observations.Count);
        Assert.Equal("Repeat in 2 weeks", rep.Observations.Single(o => o.Code == "718-7").Comment);
        Assert.Single(await svc.InboxAsync("unacknowledged", null, null));
    }

    [Fact]
    public async Task Date_of_birth_or_name_mismatch_is_held_unmatched()
    {
        await using var h = new ModulesHost();
        await h.PatientAsync();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<LabService>();
        await svc.IngestAsync(Oru(dob: "19810517"), "MLLP", "t");
        await svc.IngestAsync(Oru(family: "JONES", control: "C2"), "MLLP", "t");
        var unmatched = await svc.InboxAsync("unmatched", null, null);
        Assert.Equal(2, unmatched.Count);
        Assert.Contains(unmatched, u => u.MatchNote!.Contains("date of birth"));
        Assert.Contains(unmatched, u => u.MatchNote!.Contains("family name"));
        Assert.Empty(await svc.InboxAsync("unacknowledged", null, null));
        Assert.Equal("Match this result to a patient before acknowledging it.", await svc.AcknowledgeAsync(unmatched[0].Id, "nurse", null));
    }

    [Fact]
    public async Task Manual_match_needs_reason_then_acknowledge_once()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<LabService>();
        var r = await svc.IngestAsync(Oru(nhi: ""), "Upload", "t");
        var id = r.ReportIds[0];
        Assert.NotNull(await svc.MatchToPatientAsync(id, p.Id, "nurse", " "));
        Assert.Null(await svc.MatchToPatientAsync(id, p.Id, "nurse", "Phoned lab, confirmed DOB and address"));
        Assert.Null(await svc.AcknowledgeAsync(id, "dr.smith", "Seen, iron studies ordered"));
        Assert.Contains("Already acknowledged", await svc.AcknowledgeAsync(id, "dr.smith", null));
    }

    [Fact]
    public async Task Duplicate_message_is_acknowledged_but_not_filed_twice()
    {
        await using var h = new ModulesHost();
        await h.PatientAsync();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<LabService>();
        await svc.IngestAsync(Oru(), "MLLP", "t");
        var again = await svc.IngestAsync(Oru(), "MLLP", "t");
        Assert.Equal(LabMessageOutcome.Duplicate, again.Outcome);
        Assert.Contains("MSA|AA", again.Ack);
        Assert.Equal(1, await s.ServiceProvider.GetRequiredService<LabsDbContext>().Reports.CountAsync());
    }

    [Fact]
    public async Task Corrected_result_supersedes_earlier_one()
    {
        await using var h = new ModulesHost();
        await h.PatientAsync();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<LabService>();
        await svc.IngestAsync(Oru(status: "P"), "MLLP", "t");
        await svc.IngestAsync(Oru(status: "C", control: "C9", flag: "N"), "MLLP", "t");
        var current = await svc.InboxAsync("all", null, null);
        Assert.Single(current);
        Assert.Equal("Corrected", current[0].ResultStatus);
    }

    [Fact]
    public async Task Non_oru_and_garbage_are_rejected()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<LabService>();
        var adt = await svc.IngestAsync("MSH|^~\\&|X|Y|Z|W|202611091000||ADT^A01|C1|P|2.4\rPID|1\r", "MLLP", "t");
        Assert.Equal(LabMessageOutcome.Rejected, adt.Outcome);
        Assert.Contains("MSA|AR", adt.Ack);
        Assert.Equal(LabMessageOutcome.Rejected, (await svc.IngestAsync("not hl7", "Upload", "t")).Outcome);
    }

    [Fact]
    public async Task Sample_message_round_trips()
    {
        await using var h = new ModulesHost();
        await h.PatientAsync();
        await using var s = h.Scope();
        var r = await s.ServiceProvider.GetRequiredService<LabService>().IngestAsync(
            LabService.SampleMessage("ZSC21TN", "Ngātai", "Aroha", new DateOnly(1980, 5, 17), h.Clock.GetLocalNow()), "Upload", "t");
        Assert.Equal(2, r.ReportIds.Count);
        Assert.All(await s.ServiceProvider.GetRequiredService<LabService>().InboxAsync("all", null, null), x => Assert.Equal("Matched", x.MatchStatus));
    }
}

public class TheatreTests
{
    private static async Task<(ModulesHost H, Guid Theatre1)> Setup()
    {
        var h = new ModulesHost();
        await using var s = h.Scope();
        var t1 = await s.ServiceProvider.GetRequiredService<PasDbContext>().Locations.SingleAsync(l => l.Code == "TH1");
        return (h, t1.Id);
    }

    [Fact]
    public async Task Creates_list_and_rejects_clash_and_inactive_theatre()
    {
        var (h, th1) = await Setup();
        await using var _ = h;
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<TheatreService>();
        var day = new DateOnly(2026, 11, 16);
        var (ok, e1) = await svc.CreateSessionAsync(new(th1, day, new(8, 0), new(12, 30), SessionType.Surgical, Doc.Id("Dr Ben Hall"), Doc.Id("Dr Kate Ford"), null), "rec");
        Assert.NotNull(ok);
        var (_, e2) = await svc.CreateSessionAsync(new(th1, day, new(12, 0), new(16, 0), SessionType.Endoscopy, Doc.Id("Dr X"), null, null), "rec");
        Assert.Contains("already has a list", e2);
        var th2 = await s.ServiceProvider.GetRequiredService<PasDbContext>().Locations.SingleAsync(l => l.Code == "TH2");
        var (_, e3) = await svc.CreateSessionAsync(new(th2.Id, day, new(8, 0), new(12, 0), SessionType.Surgical, Doc.Id("Dr X"), null, null), "rec");
        Assert.Contains("active", e3);
    }

    [Fact]
    public async Task Booking_plans_times_warns_on_overrun_and_double_booking()
    {
        var (h, th1) = await Setup();
        await using var _ = h;
        var p = await h.PatientAsync();
        var p2 = await h.PatientAsync("Smith", "John", "ZVB97XQ", new DateOnly(1957, 11, 2));
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<TheatreService>();
        var day = new DateOnly(2026, 11, 16);
        var (list, _) = await svc.CreateSessionAsync(new(th1, day, new(8, 0), new(10, 0), SessionType.Surgical, Doc.Id("Dr Ben Hall"), null, null), "rec");
        var b1 = await svc.BookAsync(new(list!.Id, p.Id, "Carpal tunnel release", Laterality.Right, 45, AnaestheticType.Regional, null), "rec");
        Assert.NotNull(b1.Booking);
        var dup = await svc.BookAsync(new(list.Id, p.Id, "Again", Laterality.Left, 30, AnaestheticType.Local, null), "rec");
        Assert.Null(dup.Booking);
        Assert.Contains(dup.Warnings, w => w.Contains("already booked"));
        var over = await svc.BookAsync(new(list.Id, p2.Id, "Hernia repair", Laterality.Left, 90, AnaestheticType.General, null), "rec");
        Assert.Contains(over.Warnings, w => w.Contains("run over"));
        var accepted = await svc.BookAsync(new(list.Id, p2.Id, "Hernia repair", Laterality.Left, 90, AnaestheticType.General, null, AcceptWarnings: true), "rec");
        Assert.NotNull(accepted.Booking);
        var view = (await svc.SessionsAsync(day, day)).Single();
        Assert.Equal(new TimeOnly(8, 0), view.Bookings[0].PlannedStart);
        Assert.Equal(new TimeOnly(8, 55), view.Bookings[1].PlannedStart);
        Assert.True(view.Overrun);
    }

    [Fact]
    public async Task Status_flow_is_enforced_and_cancel_needs_reason()
    {
        var (h, th1) = await Setup();
        await using var _ = h;
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<TheatreService>();
        var (list, _) = await svc.CreateSessionAsync(new(th1, new DateOnly(2026, 11, 16), new(8, 0), new(12, 0), SessionType.Surgical, Doc.Id("Dr A"), null, null), "rec");
        var b = (await svc.BookAsync(new(list!.Id, p.Id, "Colonoscopy", Laterality.NotApplicable, 30, AnaestheticType.Sedation, null), "rec")).Booking!;
        Assert.NotNull(await svc.SetStatusAsync(b.Id, BookingStatus.Discharged, null, "rn"));
        Assert.NotNull(await svc.SetStatusAsync(b.Id, BookingStatus.Cancelled, null, "rn"));
        foreach (var st in new[] { BookingStatus.Arrived, BookingStatus.InTheatre, BookingStatus.Recovery, BookingStatus.Discharged })
            Assert.Null(await svc.SetStatusAsync(b.Id, st, null, "rn"));
        Assert.Contains("Move or cancel", await svc.CancelSessionAsync(list.Id, "rec") ?? "");
    }
}

public class FormTests
{
    private static Dictionary<string, string> Answers(Action<Dictionary<string, string>>? change = null)
    {
        var a = new Dictionary<string, string>
        {
            ["height_cm"] = "165",
            ["weight_kg"] = "70",
            ["support_person"] = "Mere 021 555 0100",
            ["lives_alone"] = "No",
            ["heart"] = "No",
            ["pacemaker"] = "No",
            ["high_bp"] = "No",
            ["lung"] = "No",
            ["sleep_apnoea"] = "No",
            ["diabetes"] = "No",
            ["kidney_liver"] = "No",
            ["stroke"] = "No",
            ["bleeding"] = "No",
            ["infection"] = "No",
            ["pregnant"] = "No",
            ["blood_thinners"] = "No",
            ["herbal"] = "No",
            ["allergies"] = "No",
            ["latex"] = "No",
            ["anaesthetic_problems"] = "No",
            ["family_mh"] = "No",
            ["teeth"] = "No",
            ["neck"] = "No",
            ["smoking"] = "Never",
            ["alcohol"] = "1–10",
            ["mobility"] = "Independently",
            ["interpreter"] = "No",
            ["confirm"] = "Yes"
        };
        change?.Invoke(a);
        return a;
    }

    [Fact]
    public void Assessment_suggests_category()
    {
        Assert.Equal(TriageCategory.Green, Questionnaire.Assess(Answers()).Suggested);
        Assert.Equal(TriageCategory.Amber, Questionnaire.Assess(Answers(a => a["blood_thinners"] = "Yes")).Suggested);
        Assert.Equal(TriageCategory.Red, Questionnaire.Assess(Answers(a => a["weight_kg"] = "120")).Suggested); // BMI 44
        Assert.Equal(TriageCategory.Red, Questionnaire.Assess(Answers(a => a["pacemaker"] = "Yes")).Suggested);
        Assert.Empty(Questionnaire.Validate(Answers()));
        Assert.Contains(Questionnaire.Validate(Answers(a => a["allergies"] = "Yes")), e => e.Contains("details"));
        Assert.Contains(Questionnaire.Validate(Answers(a => a.Remove("confirm"))), e => e.Contains("correct"));
    }

    [Fact]
    public async Task Link_needs_date_of_birth_locks_after_five_and_submits()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<FormService>();
        var (req, token, _) = await svc.CreateAsync(p.Id, null, "rn");
        Assert.NotNull(token);
        var db = s.ServiceProvider.GetRequiredService<FormsDbContext>();
        Assert.DoesNotContain(await db.Requests.Select(r => r.TokenHash).ToListAsync(), t => t == token);
        Assert.Null(await svc.PublicInfoAsync("wrong-token"));
        Assert.Equal("open", (await svc.PublicInfoAsync(token!))!.Status);
        var good = await svc.VerifyAsync(token!, p.DateOfBirth);
        Assert.True(good.Ok);
        Assert.Equal("Aroha", good.GivenName);
        for (var i = 0; i < 5; i++) Assert.False((await svc.VerifyAsync(token!, new DateOnly(1999, 1, 1))).Ok);
        Assert.Contains("Too many", (await svc.VerifyAsync(token!, p.DateOfBirth)).Error);
        h.Clock.Advance(TimeSpan.FromMinutes(31));
        var (bad, errors) = await svc.SubmitAsync(token!, p.DateOfBirth, Answers(a => a.Remove("height_cm")), final: true);
        Assert.False(bad);
        Assert.Contains(errors, e => e.Contains("height"));
        Assert.True((await svc.SubmitAsync(token!, p.DateOfBirth, Answers(a => { a["allergies"] = "Yes"; a["allergies_details"] = "Penicillin – rash"; }), final: true)).Ok);
        Assert.Equal("submitted", (await svc.PublicInfoAsync(token!))!.Status);
        var row = Assert.Single(await svc.ListAsync("to-triage", null));
        Assert.Equal("Amber", row.Suggested);
        Assert.Null(await svc.TriageAsync(row.Id, TriageCategory.Amber, "Phoned patient", "Rash only", addAllergyAlert: true, "rn"));
        var alerts = await s.ServiceProvider.GetRequiredService<PatientsDbContext>().Alerts.Where(a => a.PatientId == p.Id).ToListAsync();
        Assert.Contains(alerts, a => a.Description.Contains("Penicillin"));
        Assert.False((await svc.VerifyAsync(token!, p.DateOfBirth)).Ok); // closed after submission
    }

    [Fact]
    public async Task Expired_link_is_closed()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<FormService>();
        var (_, token, _) = await svc.CreateAsync(p.Id, null, "rn");
        h.Clock.Advance(TimeSpan.FromDays(15));
        Assert.Equal("closed", (await svc.PublicInfoAsync(token!))!.Status);
    }
}

public class BillingTests
{
    [Fact]
    public async Task Estimate_split_gst_invoice_payments_and_numbering()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<BillingService>();
        await svc.SeedExampleFeesAsync();
        var (est, errors) = await svc.CreateAsync(DocumentKind.Estimate, new NewDocument(p.Id, Payer.Insurer, "Southern Cross", "POL123", "Dr Ben Hall", "Dr Kate Ford", null,
            [new("COLON", null, 1, null), new("FAC-DAY", null, 1, null), new(null, "Biopsy pot", 2, 10m)]), "rec");
        Assert.Empty(errors);
        Assert.Equal("EST-000001", est!.Number);
        var t = BillingService.Calculate(est);
        Assert.Equal(2120m, t.Subtotal);          // 1450 + 650 + 20
        Assert.Equal(318m, t.Gst);                // 15%
        Assert.Equal(2438m, t.Total);
        Assert.Equal(580m, t.SurgeonShare);       // 40% of the procedure fee only
        Assert.Equal(290m, t.AnaesthetistShare);  // 20%
        Assert.Equal(1250m, t.FacilityShare);

        Assert.Null(await svc.SetStatusAsync(est.Id, DocumentStatus.Issued, null, "rec"));
        Assert.Null(await svc.SetStatusAsync(est.Id, DocumentStatus.Accepted, null, "rec"));
        var (inv, _) = await svc.InvoiceFromEstimateAsync(est.Id, "fin");
        Assert.Null(inv!.Number);
        Assert.NotNull(await svc.RecordPaymentAsync(inv.Id, new(new DateOnly(2026, 11, 9), 100, PaymentMethod.Eftpos, null), "fin"));
        Assert.Null(await svc.SetStatusAsync(inv.Id, DocumentStatus.Issued, null, "fin"));
        var issued = await svc.GetAsync(inv.Id, "fin", null);
        Assert.Equal("INV-000001", issued!.Number);
        Assert.Contains("more than the balance", await svc.RecordPaymentAsync(inv.Id, new(new DateOnly(2026, 11, 9), 5000, PaymentMethod.Eftpos, null), "fin"));
        Assert.Null(await svc.RecordPaymentAsync(inv.Id, new(new DateOnly(2026, 11, 9), 438m, PaymentMethod.Eftpos, "excess"), "fin"));
        Assert.Equal(DocumentStatus.PartPaid, (await svc.GetAsync(inv.Id, "fin", null))!.Status);
        Assert.Contains("cannot be voided", await svc.SetStatusAsync(inv.Id, DocumentStatus.Void, "x", "fin"));
        Assert.Null(await svc.RecordPaymentAsync(inv.Id, new(new DateOnly(2026, 11, 10), 2000m, PaymentMethod.Insurer, "SX claim"), "fin"));
        Assert.Equal(DocumentStatus.Paid, (await svc.GetAsync(inv.Id, "fin", null))!.Status);
    }

    [Fact]
    public async Task Settings_and_per_item_split_override()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<BillingService>();
        Assert.NotNull(await svc.UpdateSettingsAsync(70, 40, 15, 30, 20, null, null, "fin"));
        Assert.Null(await svc.UpdateSettingsAsync(45, 15, 15, 30, 20, "123-456-789", null, "fin"));
        Assert.Null(await svc.SaveFeeAsync(new FeeItem { Code = "scope", Description = "Endoscopy special", Price = 1000m, Split = true, SurgeonPercent = 50, AnaesthetistPercent = 0 }, "fin"));
        Assert.Null(await svc.SaveFeeAsync(new FeeItem { Code = "proc", Description = "Default split procedure", Price = 1000m, Split = true }, "fin"));
        var (d, _) = await svc.CreateAsync(DocumentKind.Invoice, new NewDocument(p.Id, Payer.SelfFunded, null, null, null, null, null, [new("SCOPE", null, 1, null), new("PROC", null, 1, null)]), "fin");
        var t = BillingService.Calculate(d!);
        Assert.Equal(500m + 450m, t.SurgeonShare);
        Assert.Equal(0m + 150m, t.AnaesthetistShare);
        var (_, errs) = await svc.CreateAsync(DocumentKind.Estimate, new NewDocument(p.Id, Payer.Insurer, null, null, null, null, null, [new("NOPE", null, 1, null)]), "fin");
        Assert.Contains(errs, e => e.Contains("insurer"));
        Assert.Contains(errs, e => e.Contains("fee item"));
    }
}
