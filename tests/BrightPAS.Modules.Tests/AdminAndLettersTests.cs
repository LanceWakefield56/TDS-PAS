using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Data;
using BrightPAS.Core.Organisation;
using BrightPAS.Modules.Letters;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Referrals;
using BrightPAS.Modules.Theatre;

namespace BrightPAS.Modules.Tests;

public class OrganisationAdminTests
{
    [Fact]
    public async Task Clinic_details_are_validated_and_saved()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<OrganisationService>();
        var bad = await svc.UpdateDetailsAsync(new("Tasman Day Surgery", "X1", "Main", "F123", null, null, null, null, "70", null, "not-an-email"), "admin");
        Assert.Equal(4, bad.Count);
        Assert.Empty(await svc.UpdateDetailsAsync(new("Tasman Day Surgery", "G00001-G", "Main site", "f2n060-e", "12 Example Street", null, "Richmond", "Nelson", "7020", "03 555 0100", "info@example.nz"), "admin"));
        var (_, site) = (await svc.MainSiteAsync())!.Value;
        Assert.Equal("F2N060-E", site.HpiFacilityId);
        Assert.Equal("Richmond", site.Suburb);
    }

    [Fact]
    public async Task Theatre_2_can_be_switched_on_and_codes_stay_unique()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var core = s.ServiceProvider.GetRequiredService<PasDbContext>();
        var svc = s.ServiceProvider.GetRequiredService<OrganisationService>();
        var th2 = await core.Locations.AsNoTracking().SingleAsync(l => l.Code == "TH2");
        var (ok, _) = await svc.SaveLocationAsync(new(th2.Id, th2.FacilityId, "TH2", "Theatre 2", LocationType.Theatre, true, 20), "admin");
        Assert.True(ok!.IsActive);
        var (_, dup) = await svc.SaveLocationAsync(new(null, th2.FacilityId, "th1", "Another", LocationType.ProcedureRoom, true, 25), "admin");
        Assert.Contains("already used", dup);
        var (proc, _) = await svc.SaveLocationAsync(new(null, th2.FacilityId, "PR1", "Procedure room", LocationType.ProcedureRoom, true, 25), "admin");
        Assert.NotNull(proc);
        Assert.Equal(3, (await s.ServiceProvider.GetRequiredService<TheatreService>().BookableLocationsAsync()).Count);
    }

    [Fact]
    public async Task Clinician_register_checks_cpn_and_duplicates()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<OrganisationService>();
        var (_, bad) = await svc.SaveClinicianAsync(new(null, "Dr", ClinicianType.Surgeon, null, "12ABC345", null, "x", null, null, true), "admin");
        Assert.Equal(3, bad.Count);
        var (reid, _) = await svc.SaveClinicianAsync(new(null, "Dr Tim Reid", ClinicianType.Surgeon, "General surgery", "12abcd", null, null, "Reid Surgical", null, true), "admin");
        Assert.Equal("12ABCD", reid!.HpiCpn);
        var (_, dup) = await svc.SaveClinicianAsync(new(null, "Dr Tim Reid", ClinicianType.Surgeon, null, "34EFGH", null, null, null, null, true), "admin");
        Assert.Contains(dup, e => e.Contains("already in the register"));
        await svc.SaveClinicianAsync(new(reid.Id, "Dr Tim Reid", ClinicianType.Surgeon, null, "12ABCD", null, null, null, null, false), "admin");
        Assert.DoesNotContain(await svc.CliniciansAsync(activeOnly: true), c => c.Name == "Dr Tim Reid");
    }

    [Fact]
    public async Task Procedure_catalogue_examples_are_added_once()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<OrganisationService>();
        var n = await svc.AddExampleProceduresAsync("admin");
        Assert.True(n >= 10);
        Assert.Equal(0, await svc.AddExampleProceduresAsync("admin"));
        var (_, errors) = await svc.SaveProcedureAsync(new(null, "LAPCHOLE", "Dup", "Surgical", 3, "Gas", false, null, true), "admin");
        Assert.Equal(3, errors.Count);
    }
}

public class ListTemplateTests
{
    [Fact]
    public async Task Templates_generate_weekly_and_fortnightly_lists_and_skip_clashes()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<TheatreService>();
        var th1 = await s.ServiceProvider.GetRequiredService<PasDbContext>().Locations.SingleAsync(l => l.Code == "TH1");
        var mon = new DateOnly(2026, 11, 9);
        var (weekly, _) = await svc.SaveTemplateAsync(new(null, th1.Id, DayOfWeek.Tuesday, new(8, 0), new(12, 30), SessionType.Surgical, Doc.Id("Dr Ben Hall"), Doc.Id("Dr Kate Ford"), 1, mon, null, true), "rec");
        Assert.NotNull(weekly);
        var (_, clash) = await svc.SaveTemplateAsync(new(null, th1.Id, DayOfWeek.Tuesday, new(12, 0), new(16, 0), SessionType.Endoscopy, Doc.Id("Dr Ana Lee"), null, 1, mon, null, true), "rec");
        Assert.Contains("already uses", clash);
        await svc.SaveTemplateAsync(new(null, th1.Id, DayOfWeek.Thursday, new(13, 0), new(17, 0), SessionType.Endoscopy, Doc.Id("Dr Ana Lee"), null, 2, mon, null, true), "rec");

        var r = await svc.GenerateFromTemplatesAsync(mon, mon.AddDays(27), "rec");
        Assert.Equal(4 + 2, r.Created);
        var again = await svc.GenerateFromTemplatesAsync(mon, mon.AddDays(27), "rec");
        Assert.Equal(0, again.Created);
        Assert.Equal(6, again.Skipped.Count);
        Assert.Contains("26 weeks", (await svc.GenerateFromTemplatesAsync(mon, mon.AddDays(400), "rec")).Skipped[0]);
        var lists = await svc.SessionsAsync(mon, mon.AddDays(27));
        Assert.All(lists, l => Assert.Equal("From template", l.Notes));
    }
}

public class LettersTests
{
    private static async Task<(Patient P, Guid BookingId)> BookedPatientAsync(ModulesHost h, AsyncServiceScope s)
    {
        var p = await h.PatientAsync();
        var core = s.ServiceProvider.GetRequiredService<PasDbContext>();
        var org = s.ServiceProvider.GetRequiredService<OrganisationService>();
        await org.UpdateDetailsAsync(new("Tasman Day Surgery", null, "Main site", null, "12 Example Street", null, null, "Nelson", "7010", "03 555 0100", null), "admin");
        var theatre = s.ServiceProvider.GetRequiredService<TheatreService>();
        var th1 = await core.Locations.SingleAsync(l => l.Code == "TH1");
        var (list, _) = await theatre.CreateSessionAsync(new(th1.Id, new DateOnly(2026, 11, 17), new(8, 0), new(12, 30), SessionType.Surgical, Doc.Id("Dr Ben Hall"), null, null), "rec");
        var other = await h.PatientAsync("Smith", "John", "ZVB97XQ", new DateOnly(1957, 11, 2));
        await theatre.BookAsync(new(list!.Id, other.Id, "Colonoscopy", Laterality.NotApplicable, 50, AnaestheticType.Sedation, null), "rec");
        var b = await theatre.BookAsync(new(list.Id, p.Id, "Inguinal hernia repair", Laterality.Right, 60, AnaestheticType.General, null), "rec");
        return (p, b.Booking!.Id);
    }

    [Fact]
    public async Task Appointment_letter_fills_in_booking_times_and_locks_when_final()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var (p, bookingId) = await BookedPatientAsync(h, s);
        var svc = s.ServiceProvider.GetRequiredService<LetterService>();
        Assert.Equal(5, (await svc.TemplatesAsync(true)).Count);
        var (_, needBooking) = await svc.CreateDraftAsync(new(p.Id, "APPT", null, null), "rec", "Sam Reception");
        Assert.Contains("booking", needBooking[0]);

        var (letter, _) = await svc.CreateDraftAsync(new(p.Id, "APPT", bookingId, null), "rec", "Sam Reception");
        Assert.Equal("L000001", letter!.Number);
        Assert.Contains("Inguinal hernia repair (right) with Dr Ben Hall", letter.Body);
        Assert.Contains("Tuesday 17 November 2026", letter.Body);
        Assert.Contains("about 9:00 am", letter.Body);    // second on the list: 08:00 + 50 + 10 turnover
        Assert.Contains("at 8:00 am", letter.Body);       // arrive 60 minutes before
        Assert.Contains("12 Example Street, Nelson, 7010", letter.Body);
        Assert.Contains("Sam Reception", letter.Body);
        Assert.Equal(RecipientType.Patient, letter.Recipient);

        Assert.Null(await svc.UpdateDraftAsync(letter.Id, new("Aroha Mere Ngātai", "1 Test Road\nNelson 7010", letter.Subject, letter.Body + "\nP.S. {Unknown}", "Sam Reception"), "rec"));
        Assert.Contains("{Unknown}", await svc.FinaliseAsync(letter.Id, "rec"));
        Assert.Null(await svc.UpdateDraftAsync(letter.Id, new("Aroha Mere Ngātai", "1 Test Road\nNelson 7010", letter.Subject, letter.Body.Replace("\nP.S. {Unknown}", ""), "Sam Reception"), "rec"));
        var current = (await svc.ListAsync("drafts", null)).Single();
        Assert.Null(await svc.FinaliseAsync(letter.Id, "rec"));
        Assert.Contains("draft", await svc.UpdateDraftAsync(letter.Id, new("x", null, "x", "x", null), "rec"));
        Assert.Null(await svc.RecordPrintAsync(letter.Id, "rec"));
        Assert.Equal("Sent", (await svc.ListAsync("recent", p.Id)).Single().Status);
        Assert.Contains("cannot be voided", await svc.VoidAsync(letter.Id, "oops", "rec"));
        _ = current;
    }

    [Fact]
    public async Task Templates_reject_unknown_placeholders()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<LetterService>();
        var errors = await svc.SaveTemplateAsync(new("APPT", "Appointment", LetterKind.Appointment, RecipientType.Patient, "Hi {PatientName}", "Your {Diagnosis} on {BookingDate}", true), "admin");
        Assert.Contains("{Diagnosis}", errors.Single());
        Assert.Empty(await svc.SaveTemplateAsync(new("APPT", "Appointment", LetterKind.Appointment, RecipientType.Patient, "Hi {PatientName}", "See you {BookingDate}", true), "admin"));
        Assert.Equal(2, (await svc.TemplatesAsync(false)).Single(t => t.Code == "APPT").Version);
    }

    [Fact]
    public async Task Gp_letter_needs_a_practice_then_goes_by_healthlink_with_escaped_hl7()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var (p, bookingId) = await BookedPatientAsync(h, s);
        var svc = s.ServiceProvider.GetRequiredService<LetterService>();
        var hl = s.ServiceProvider.GetRequiredService<HealthLinkService>();

        var (noGp, _) = await svc.CreateDraftAsync(new(p.Id, "GPBOOK", bookingId, null), "rec", "Sam");
        Assert.StartsWith("(GP practice not recorded", noGp!.RecipientName);
        Assert.Contains("GP practice", await svc.FinaliseAsync(noGp.Id, "rec"));
        Assert.Null(await svc.VoidAsync(noGp.Id, "No GP recorded", "rec"));

        Assert.Empty(await svc.SavePracticeAsync(new(null, "Stoke Medical Centre", "F12345-A", "stokemed", "1 Main Road", null, "Nelson", "7011", null, null, DeliveryChannel.HealthLink, true), "admin"));
        await using (var s2 = h.Scope())
        {
            var pts = s2.ServiceProvider.GetRequiredService<PatientsDbContext>();
            (await pts.Patients.SingleAsync(x => x.Id == p.Id)).GpPracticeHpiFacilityId = "F12345-A";
            await pts.SaveChangesAsync();
        }
        var (gp, _) = await svc.CreateDraftAsync(new(p.Id, "GPBOOK", bookingId, null), "rec", "Sam");
        Assert.Equal("Stoke Medical Centre", gp!.RecipientName);
        Assert.Contains("NHI ZSC21TN", gp.Subject);
        Assert.Null(await svc.UpdateDraftAsync(gp.Id, new(gp.RecipientName, gp.RecipientAddress, gp.Subject, gp.Body + "\nNote: pipes | carets ^ & tildes ~", "Sam"), "rec"));
        Assert.Contains("Finalise", (await hl.SendAsync(gp.Id, "rec")).Message);
        Assert.Null(await svc.FinaliseAsync(gp.Id, "rec"));
        var (ok, msg) = await hl.SendAsync(gp.Id, "rec");   // default mode: test outbox
        Assert.True(ok, msg);
        Assert.Contains("test mode", msg);
        var sent = (await svc.ListAsync("recent", p.Id)).First(l => l.Id == gp.Id);
        Assert.Equal("Sent", sent.Status);
        Assert.Equal("HealthLink", sent.Channel);

        var letter = await s.ServiceProvider.GetRequiredService<LettersDbContext>().Letters.AsNoTracking().SingleAsync(l => l.Id == gp.Id);
        var practice = (await svc.PracticesAsync()).Single();
        var patient = await s.ServiceProvider.GetRequiredService<PatientsDbContext>().Patients.AsNoTracking().SingleAsync(x => x.Id == p.Id);
        var hl7 = HealthLinkService.BuildRefI12(letter, patient, practice, "Tasman Day Surgery", null, "tdsurg", "BP1", DateTimeOffset.Now);
        var segs = hl7.Split('\r', StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("MSH|^~\\&|BrightPAS|tdsurg|HealthLink|stokemed|", segs[0]);
        Assert.Contains("REF^I12^REF_I12", segs[0]);
        Assert.Contains("ZSC21TN^^^NHI^NHI", segs.Single(x => x.StartsWith("PID|")));
        var obx = segs.Single(x => x.StartsWith("OBX|"));
        Assert.Contains(@"pipes \F\ carets \S\ \T\ tildes \R\", obx);
        Assert.Equal(12, obx.Split('|').Length - 3); // OBX-5 is one field: the escaping kept the segment structure (15 fields)
    }

    [Fact]
    public async Task Folder_mode_writes_file_and_acknowledgement_completes_the_letter()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var (p, bookingId) = await BookedPatientAsync(h, s);
        var svc = s.ServiceProvider.GetRequiredService<LetterService>();
        var hl = s.ServiceProvider.GetRequiredService<HealthLinkService>();
        var root = Directory.CreateTempSubdirectory("hl");
        var outbox = Directory.CreateDirectory(Path.Combine(root.FullName, "out")).FullName;
        var inbox = Directory.CreateDirectory(Path.Combine(root.FullName, "in")).FullName;
        Assert.Contains(await hl.UpdateSettingsAsync(new(HealthLinkMode.FolderDrop, "x", "relative", null), "admin"), e => e.Contains("full path"));
        Assert.Empty(await hl.UpdateSettingsAsync(new(HealthLinkMode.FolderDrop, "tdsurg", outbox, inbox), "admin"));
        await svc.SavePracticeAsync(new(null, "Stoke Medical Centre", "F12345-A", "stokemed", null, null, "Nelson", null, null, null, DeliveryChannel.HealthLink, true), "admin");
        await using (var s2 = h.Scope())
        {
            var pts = s2.ServiceProvider.GetRequiredService<PatientsDbContext>();
            (await pts.Patients.SingleAsync(x => x.Id == p.Id)).GpPracticeHpiFacilityId = "F12345-A";
            await pts.SaveChangesAsync();
        }
        var (gp, _) = await svc.CreateDraftAsync(new(p.Id, "GPBOOK", bookingId, null), "rec", "Sam");
        await svc.FinaliseAsync(gp!.Id, "rec");
        var (ok, msg) = await hl.SendAsync(gp.Id, "rec");
        Assert.True(ok, msg);
        var file = Directory.GetFiles(outbox, "*.hl7").Single();
        var control = Path.GetFileNameWithoutExtension(file);
        Assert.Contains(control, await File.ReadAllTextAsync(file));

        await File.WriteAllTextAsync(Path.Combine(inbox, "ack1.hl7"), $"MSH|^~\\&|HealthLink|stokemed|BrightPAS|tdsurg|20261117||ACK^I12|A1|P|2.4\rMSA|AA|{control}|Delivered\r");
        Assert.Equal(1, await hl.ProcessAcknowledgementsAsync());
        Assert.Empty(Directory.GetFiles(inbox, "*.hl7"));
        Assert.Single(Directory.GetFiles(Path.Combine(inbox, "processed")));
        var row = (await svc.ListAsync("recent", p.Id)).Single(l => l.Id == gp.Id);
        Assert.Equal("Acknowledged", row.Status);
        Assert.Equal("AA", row.AckCode);

        // 0.15.1: a report letter with a PDF attached goes as OBX ED.
        var pdf = "%PDF-1.4 test report"u8.ToArray();
        var (rep, repErr) = await svc.CreateGpReportAsync(p.Id, bookingId, "Colonoscopy report", "Dear Doctor\nReport attached.", pdf, "endoscopy-E1.pdf", "dr", "Dr Lindqvist");
        Assert.Empty(repErr);
        Assert.Equal(LetterStatus.Final, rep!.Status);
        Assert.True((await hl.SendAsync(rep.Id, "dr")).Ok);
        var msg2 = string.Join("\n", Directory.GetFiles(outbox, "*.hl7").Select(File.ReadAllText));
        Assert.Contains("OBX|2|ED|PDF^endoscopy-E1.pdf^L||^AP^PDF^Base64^" + Convert.ToBase64String(pdf), msg2);
        root.Delete(true);
    }

    [Fact]
    public async Task Referral_decline_letter_goes_to_referrer_with_reason()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var refs = s.ServiceProvider.GetRequiredService<ReferralService>();
        var (r, _) = await refs.CreateReferralAsync(new(p.Id, new DateOnly(2026, 11, 2), ReferralSource.GeneralPractice, "Dr Mere Smith", "Stoke Medical Centre", null, Doc.Id("Dr Ben Hall"),
            ServiceType.Surgical, Funder.SelfPay, null, "Lump"), "rec");
        await refs.TriageAsync(r!.Id, new TriageDecision(false, null, "Better managed by the public service", null, default, default, 0, default, null), "dr");
        var svc = s.ServiceProvider.GetRequiredService<LetterService>();
        var (l, _) = await svc.CreateDraftAsync(new(p.Id, "REFDEC", null, r.Id), "rec", "Sam");
        Assert.Equal("Dr Mere Smith", l!.RecipientName);
        Assert.Equal("Stoke Medical Centre", l.RecipientAddress);
        Assert.Contains("Reason: Better managed by the public service", l.Body);
        Assert.Contains("2 November 2026 (R000001)", l.Body);
        var other = await h.PatientAsync("Other", "Person", null, new DateOnly(1990, 1, 1));
        var (_, wrong) = await svc.CreateDraftAsync(new(other.Id, "REFDEC", null, r.Id), "rec", "Sam");
        Assert.Contains("not for this patient", wrong[0]);
    }
}

public class PatientContactTests
{
    [Fact]
    public async Task Contact_and_gp_can_be_updated_with_validation_and_audit()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<PatientService>();
        Assert.Equal(3, (await svc.UpdateContactAsync(p.Id, new(null, null, null, "bad", null, null, null, null, "70", "F1"), "rec")).Count);
        Assert.Empty(await svc.UpdateContactAsync(p.Id, new("Aroha", "021 555 0101", null, "aroha@example.nz", "1 Test Road", null, null, "Nelson", "7010", "f12345-a"), "rec"));
        var after = await s.ServiceProvider.GetRequiredService<PatientsDbContext>().Patients.AsNoTracking().SingleAsync(x => x.Id == p.Id);
        Assert.Equal("F12345-A", after.GpPracticeHpiFacilityId);
        var audit = await s.ServiceProvider.GetRequiredService<PasDbContext>().AuditEvents.Where(a => a.EntityType == "Patient" && a.Action == "Update").SingleAsync();
        Assert.Contains("GP practice", audit.Reason);
    }
}
