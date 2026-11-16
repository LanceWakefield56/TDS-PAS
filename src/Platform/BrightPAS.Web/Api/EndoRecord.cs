using Microsoft.EntityFrameworkCore;
using BrightPAS.Licensing;
using BrightPAS.Modules.Endoscopy;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Theatre;
using BrightPAS.Web.Licensing;
using BrightPAS.Web.Pdf;
using Ast = BrightPAS.Modules.Assets;
using static BrightPAS.Modules.Endoscopy.Terminology;

namespace BrightPAS.Web.Api;

/// <summary>Builds the endoscopy record for the screen and the data for the PDF reports, joining theatre, patients, assets (scopes) and eMR (sedation).</summary>
public static class EndoRecord
{
    private static bool Licensed(IServiceProvider sp, string module) => sp.GetRequiredService<LicenceCache>().Status.AccessTo(module) != ModuleAccess.None;

    public static object MediaView(Media m) => new
    {
        m.Id,
        kind = m.Kind.ToString(),
        m.ContentType,
        source = m.Source.ToString(),
        status = m.Status.ToString(),
        m.CapturedAt,
        m.CapturedBy,
        m.DurationSeconds,
        m.Bytes,
        m.Site,
        m.Caption,
        m.InReport,
        m.InPatientReport,
        m.Sort,
        m.FindingId,
        m.PolypId,
        url = $"/api/endoscopy/media/{m.Id}",
        m.AnnotatedFromId,
        original = m.OriginalFileName is null ? null : $"/api/endoscopy/media/{m.Id}/original",
        originalType = m.OriginalContentType,
        m.ArchivedAt,
    };

    public static async Task<object> BuildAsync(IServiceProvider sp, TheatreBooking booking, TheatreSession session, Procedure? p)
    {
        var core = sp.GetRequiredService<BrightPAS.Core.Data.PasDbContext>();
        var patients = sp.GetRequiredService<PatientsDbContext>();
        var pt = await patients.Patients.AsNoTracking().Include(x => x.Alerts).SingleAsync(x => x.Id == booking.PatientId);
        var location = await core.Locations.AsNoTracking().Where(l => l.Id == session.LocationId).Select(l => l.Name).SingleOrDefaultAsync();
        var surgeon = await core.Clinicians.AsNoTracking().Where(c => c.Id == session.SurgeonId).Select(c => c.Name).SingleOrDefaultAsync();
        var header = new
        {
            bookingId = booking.Id,
            patientId = pt.Id,
            name = $"{pt.FamilyName.ToUpperInvariant()}, {pt.GivenNames}",
            pt.Nhi,
            pt.Mrn,
            pt.DateOfBirth,
            sex = pt.Sex.ToString(),
            alerts = pt.Alerts.Where(a => a.IsActive).Select(a => new { type = a.Type.ToString(), severity = a.Severity.ToString(), a.Description }),
            booking.Procedure,
            date = session.Date,
            location,
            surgeon,
            anaesthetic = booking.Anaesthetic.ToString(),
        };
        if (p is null) return new { header, procedure = (object?)null };
        var db = sp.GetRequiredService<EndoDbContext>();
        var svc = sp.GetRequiredService<EndoService>();
        var findings = await db.Findings.AsNoTracking().Where(x => x.ProcedureId == p.Id).OrderBy(x => x.Number).ToListAsync();
        var polyps = await db.Polyps.AsNoTracking().Where(x => x.ProcedureId == p.Id).OrderBy(x => x.Number).ToListAsync();
        var tattoos = await db.Tattoos.AsNoTracking().Where(x => x.ProcedureId == p.Id).ToListAsync();
        var therapy = await db.Interventions.AsNoTracking().Where(x => x.ProcedureId == p.Id).ToListAsync();
        var media = await svc.MediaAsync(p.Id);

        object? scopes = null, towers = null;
        if (Licensed(sp, "assets"))
        {
            var all = await sp.GetRequiredService<Ast.CssdService>().ScopesAsync();
            scopes = all.Select(s => new
            {
                s.Id,
                s.AssetNumber,
                s.Name,
                s.Model,
                s.SerialNumber,
                s.State,
                s.StateText,
                upper = !(s.Model ?? "").StartsWith("CF", StringComparison.OrdinalIgnoreCase) && !(s.Model ?? "").StartsWith("PCF", StringComparison.OrdinalIgnoreCase),
                lower = !(s.Model ?? "").StartsWith("GIF", StringComparison.OrdinalIgnoreCase)
            });
            towers = await sp.GetRequiredService<Ast.AssetsDbContext>().Equipment.AsNoTracking().Where(e => e.Category == Ast.EquipmentCategory.EndoscopyTower && e.Status != Ast.EquipmentStatus.Retired)
                .OrderBy(e => e.AssetNumber).Select(e => new { e.Id, e.AssetNumber, e.Name, e.Model, e.NetworkAddress }).ToListAsync();
        }
        object? gpLetter = null;
        if (p.GpLetterId is { } lid && Licensed(sp, "letters"))
            gpLetter = await sp.GetRequiredService<BrightPAS.Modules.Letters.LettersDbContext>().Letters.AsNoTracking().Where(l => l.Id == lid)
                .Select(l => new { l.Id, l.Number, status = l.Status.ToString(), l.RecipientName, l.SentAt, l.AckText }).SingleOrDefaultAsync();
        object? sedation = null; Guid? episodeId = null;
        if (Licensed(sp, "emr"))
        {
            var emr = sp.GetRequiredService<BrightPAS.Modules.Emr.EmrDbContext>();
            episodeId = await emr.Episodes.AsNoTracking().Where(e => e.BookingId == booking.Id).Select(e => (Guid?)e.Id).SingleOrDefaultAsync();
            if (episodeId is { } eid)
                sedation = await emr.Medications.AsNoTracking().Where(m => m.EpisodeId == eid).OrderBy(m => m.GivenAt).Select(m => new { m.Medicine, m.Dose, m.Route, m.GivenAt, m.GivenBy }).ToListAsync();
        }
        return new
        {
            header,
            procedure = p,
            indications = p.Indications.Select(i => i.Code),
            findings,
            polyps,
            tattoos,
            therapy,
            media = media.Select(MediaView),
            missing = await svc.MissingAsync(p),
            suggestion = EndoService.SuggestSurveillance(p, polyps),
            withdrawalMinutes = EndoService.WithdrawalMinutes(p),
            upperMinutes = EndoService.UpperMinutes(p),
            bbps = EndoService.BbpsTotal(p),
            adequatePrep = EndoService.AdequatePrep(p),
            scopes,
            towers,
            sedation,
            episodeId,
            gpLetter,
        };
    }

    /// <summary>Records "used on this patient" against each chosen endoscope (assets module) so scope-to-patient traceability is automatic.</summary>
    public static async Task<IReadOnlyList<string>> RecordScopeUseAsync(IServiceProvider sp, Guid procedureId, string user)
    {
        if (!Licensed(sp, "assets")) return [];
        var p = await sp.GetRequiredService<EndoDbContext>().Procedures.AsNoTracking().SingleAsync(x => x.Id == procedureId);
        var assets = sp.GetRequiredService<Ast.AssetsDbContext>();
        var cssd = sp.GetRequiredService<Ast.CssdService>();
        var warnings = new List<string>();
        foreach (var scopeId in new[] { p.UpperScopeId, p.LowerScopeId }.OfType<Guid>())
        {
            if (await assets.ScopeEvents.AnyAsync(e => e.ScopeId == scopeId && e.BookingId == p.BookingId && e.Kind == Ast.ScopeEventKind.Use)) continue;
            var (_, error, warning) = await cssd.RecordScopeEventAsync(new Ast.ScopeEventEdit(scopeId, Ast.ScopeEventKind.Use, null, null, p.BookingId, p.PatientId, $"Endoscopy {p.AccessionNumber}"), user);
            if (error is not null) warnings.Add("Scope use NOT recorded – " + error + " Check the scope with CSSD before use.");
            if (warning is not null) warnings.Add(warning);
        }
        return warnings;
    }

    public static async Task<EndoReportData?> ReportDataAsync(IServiceProvider sp, Guid procedureId)
    {
        var svc = sp.GetRequiredService<EndoService>();
        var p = await svc.FindAsync(procedureId);
        if (p is null) return null;
        var db = sp.GetRequiredService<EndoDbContext>();
        var store = sp.GetRequiredService<MediaStore>();
        var theatre = sp.GetRequiredService<TheatreDbContext>();
        var booking = await theatre.Bookings.AsNoTracking().SingleAsync(b => b.Id == p.BookingId);
        var session = await theatre.Sessions.AsNoTracking().SingleAsync(s => s.Id == booking.SessionId);
        var pt = await sp.GetRequiredService<PatientsDbContext>().Patients.AsNoTracking().SingleAsync(x => x.Id == p.PatientId);
        var site = await sp.GetRequiredService<BrightPAS.Core.Organisation.OrganisationService>().MainSiteAsync();
        var findings = await db.Findings.AsNoTracking().Where(x => x.ProcedureId == p.Id).OrderBy(x => x.Number).ToListAsync();
        var polyps = await db.Polyps.AsNoTracking().Where(x => x.ProcedureId == p.Id).OrderBy(x => x.Number).ToListAsync();
        var tattoos = await db.Tattoos.AsNoTracking().Where(x => x.ProcedureId == p.Id).ToListAsync();
        var therapy = await db.Interventions.AsNoTracking().Where(x => x.ProcedureId == p.Id).ToListAsync();
        var media = (await svc.MediaAsync(p.Id)).Where(m => m.Kind == MediaKind.Image && (m.InReport || m.InPatientReport)).ToList();
        var pictures = new List<EndoPicture>();
        foreach (var m in media)
            if (await store.ReadAsync(m) is { } bytes)
                pictures.Add(new EndoPicture(bytes, string.Join(" – ", new[] { m.Site is null ? null : SiteLabel(m.Site), m.Caption }.Where(x => !string.IsNullOrWhiteSpace(x))),
                    m.InReport, m.InPatientReport, m.PolypId is { } pid ? polyps.FirstOrDefault(x => x.Id == pid)?.Number : null));

        var scopes = new List<string>();
        var meds = new List<string>();
        if (Licensed(sp, "assets"))
        {
            var ids = new[] { p.UpperScopeId, p.LowerScopeId, p.TowerId }.OfType<Guid>().ToList();
            scopes = await sp.GetRequiredService<Ast.AssetsDbContext>().Equipment.AsNoTracking().Where(e => ids.Contains(e.Id))
                .Select(e => $"{e.Name} {e.Model} ({e.AssetNumber}{(e.SerialNumber != null ? ", SN " + e.SerialNumber : "")})").ToListAsync();
        }
        if (Licensed(sp, "emr"))
        {
            var emr = sp.GetRequiredService<BrightPAS.Modules.Emr.EmrDbContext>();
            var eid = await emr.Episodes.AsNoTracking().Where(e => e.BookingId == p.BookingId).Select(e => (Guid?)e.Id).SingleOrDefaultAsync();
            if (eid is not null) meds = (await emr.Medications.AsNoTracking().Where(m => m.EpisodeId == eid).OrderBy(m => m.GivenAt).ToListAsync()).Select(m => $"{m.Medicine} {m.Dose} {m.Route}").ToList();
        }
        var advice = (await svc.AdviceAsync()).ToDictionary(a => a.Key, a => (a.Title, a.Body));
        return new EndoReportData(p, booking.Id, pt.Id, p.AccessionNumber, KindLabel(p.Kind), session.Date,
            site?.Org.Name ?? "Tasman Day Surgery",
            site is { } x ? string.Join(", ", new[] { x.Site.AddressLine1, x.Site.Suburb, x.Site.City }.Where(v => !string.IsNullOrWhiteSpace(v))) : null, site?.Site.Phone,
            $"{pt.FamilyName.ToUpperInvariant()}, {pt.GivenNames}", pt.PreferredName ?? pt.GivenNames.Split(' ')[0], pt.Nhi, pt.Mrn, pt.DateOfBirth, pt.Sex.ToString(),
            p.Indications.Select(i => IndicationLabel(i.Code)).Concat(string.IsNullOrWhiteSpace(p.OtherIndication) ? [] : [p.OtherIndication!]).ToList(),
            findings, polyps, tattoos, therapy, pictures, scopes, meds, advice, EndoService.SuggestSurveillance(p, polyps), p.Status == ProcedureStatus.Signed);
    }
}
