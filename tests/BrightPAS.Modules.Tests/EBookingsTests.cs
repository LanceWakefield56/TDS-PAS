using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Data;
using BrightPAS.Modules.EBookings;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Referrals;
using BrightPAS.Modules.Theatre;

namespace BrightPAS.Modules.Tests;

/// <summary>0.28.0 bookings from specialists' rooms: submit, checks, query, match, accept onto the waitlist, file imports (JSON, HL7 REF^I12), merge.</summary>
public class EBookingsTests
{
    private static readonly Guid Hall = Doc.Id("Dr Ben Hall");
    private static readonly Guid Lee = Doc.Id("Dr Ana Lee");
    private static readonly string Pdf = Convert.ToBase64String(Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n"));

    private static EBookingInput Input(Guid? specialist = null, string? nhi = "ZSC21TN", string family = "Ngātai", string given = "Aroha Mere", string procedure = "Laparoscopic cholecystectomy",
        bool consent = true, bool adult = true, int? asa = 2, bool confirm = false, IReadOnlyList<EBookingFileInput>? files = null) =>
        new(specialist ?? Hall, "Nelson Surgical Rooms", "Rachel Moore", "03 555 0300", "rooms@example.co.nz", nhi, family, given, new DateOnly(1980, 5, 17), BirthSex.Female,
            "021 555 0101", null, "1 Example St", "Stoke", "Nelson", "7011", null, ServiceType.Surgical, null, procedure, Laterality.NotApplicable, AnaestheticType.General, 75,
            Priority.Routine, Funder.Insurer, "SX-1", "Southern Cross", new DateOnly(2026, 11, 20), new DateOnly(2027, 1, 30), "Not Fridays", "Gallstones.",
            84, 165, asa, false, false, false, null, null, "Hypertension", adult, consent, consent ? new DateOnly(2026, 11, 6) : null, null, files, confirm);

    private static string OtherNhi()
    {
        for (var n = 100; ; n++)
        {
            var nhi = $"ZAB{n:D4}";
            if (BrightPAS.Core.Patients.NhiValidator.Validate(nhi).IsValid) return nhi;
        }
    }

    [Fact]
    public async Task Rooms_send_a_request_and_the_checks_run()
    {
        await using var h = new ModulesHost();
        await using var scope = h.Scope();
        var svc = scope.ServiceProvider.GetRequiredService<EBookingService>();

        var notMine = await svc.SubmitAsync(Input(Lee), EBookingSource.Portal, "pa", [Hall]);
        Assert.Contains("Choose one of your specialists.", notMine.Errors);

        var bad = await svc.SubmitAsync(Input(nhi: "ABC1234", family: "", procedure: "") with { DateOfBirth = new DateOnly(2030, 1, 1), WeightKg = 900 }, EBookingSource.Portal, "pa", [Hall]);
        Assert.Contains(bad.Errors, e => e.StartsWith("NHI:", StringComparison.Ordinal));
        Assert.Contains("Enter the patient's family name.", bad.Errors);
        Assert.Contains("Enter the procedure.", bad.Errors);
        Assert.Contains("The date of birth cannot be in the future.", bad.Errors);
        Assert.Contains("Weight must be between 1 and 400 kg.", bad.Errors);

        var badFile = await svc.SubmitAsync(Input(files: [new(DocumentCategory.Referral, "x.exe", Convert.ToBase64String("MZ"u8.ToArray()))]), EBookingSource.Portal, "pa", [Hall]);
        Assert.Contains(badFile.Errors, e => e.Contains("only PDF", StringComparison.Ordinal));

        var ok = await svc.SubmitAsync(Input(consent: false, adult: false, asa: 4, files: [new(DocumentCategory.Referral, "letter.pdf", Pdf)]), EBookingSource.Portal, "pa", [Hall]);
        Assert.Empty(ok.Errors);
        Assert.Equal("EB000001", ok.Request!.Number);
        Assert.Contains(ok.Warnings, w => w.StartsWith("ASA 4", StringComparison.Ordinal));
        Assert.Contains("Consent not yet signed.", ok.Warnings);
        Assert.Contains(ok.Warnings, w => w.StartsWith("No responsible adult", StringComparison.Ordinal));
        var stored = await scope.ServiceProvider.GetRequiredService<EBookingsDbContext>().Files.AsNoTracking().SingleAsync();
        Assert.NotEqual(Convert.FromBase64String(Pdf), stored.Content); // encrypted at rest

        // Same patient and specialist again: confirm first.
        var dup = await svc.SubmitAsync(Input(procedure: "Umbilical hernia repair"), EBookingSource.Portal, "pa", [Hall]);
        Assert.True(dup.NeedsConfirm);
        Assert.Null(dup.Request);
        var dup2 = await svc.SubmitAsync(Input(procedure: "Umbilical hernia repair", confirm: true), EBookingSource.Portal, "pa", [Hall]);
        Assert.Equal("EB000002", dup2.Request!.Number);

        // The rooms only see their own specialists' requests.
        Assert.Equal(2, (await svc.ListAsync("all", [Hall])).Count);
        Assert.Empty(await svc.ListAsync("all", [Lee]));
        Assert.Null(await svc.GetAsync(ok.Request.Id, [Lee]));
        Assert.Equal(2, await svc.WaitingCountAsync());
    }

    [Fact]
    public async Task Booking_office_asks_matches_and_accepts_onto_the_waitlist()
    {
        await using var h = new ModulesHost();
        await using var scope = h.Scope();
        var svc = scope.ServiceProvider.GetRequiredService<EBookingService>();
        var r = (await svc.SubmitAsync(Input(files: [new(DocumentCategory.Referral, "letter.pdf", Pdf)]), EBookingSource.Portal, "pa", [Hall])).Request!;

        // Question and answer.
        Assert.Null(await svc.AddMessageAsync(r.Id, "Is the patient on any blood thinners?", true, "reception", null));
        Assert.Equal(EBookingStatus.Query, (await svc.GetAsync(r.Id, null))!.Status);
        var rows = await svc.ListAsync("open", [Hall]);
        Assert.True(rows[0].AwaitingRooms);
        Assert.Null(await svc.AddMessageAsync(r.Id, "No.", false, "pa", [Hall]));
        Assert.Equal(EBookingStatus.Submitted, (await svc.GetAsync(r.Id, null))!.Status);

        // Not matched yet: cannot accept.
        var (none, e1) = await svc.AcceptAsync(r.Id, new EBookingAccept(null, Laterality.NotApplicable, AnaestheticType.General, 75, Priority.Routine, null), "reception");
        Assert.Null(none);
        Assert.Contains(e1, e => e.StartsWith("Match the request", StringComparison.Ordinal));

        // A registered patient with a different NHI is refused; registering from the request works.
        var other = await h.PatientAsync("Ngātai", "Aroha Mere", OtherNhi());
        var full = await svc.GetAsync(r.Id, null);
        Assert.Contains(await svc.MatchesAsync(full!), m => m.Id == other.Id && m.Reason.Contains("different NHI", StringComparison.Ordinal));
        Assert.Contains("ZSC21TN", await svc.MatchPatientAsync(r.Id, other.Id, "reception"));
        var reg = await svc.RegisterPatientAsync(r.Id, true, "reception");
        Assert.True(reg.Succeeded, string.Join(" ", reg.Errors));

        var (accepted, errors) = await svc.AcceptAsync(r.Id, new EBookingAccept("Laparoscopic cholecystectomy", Laterality.NotApplicable, AnaestheticType.General, 90, Priority.SemiUrgent, "Second on the list"), "reception");
        Assert.Empty(errors);
        var entry = await scope.ServiceProvider.GetRequiredService<ReferralsDbContext>().Waitlist.AsNoTracking().SingleAsync(w => w.Id == accepted!.WaitlistEntryId);
        Assert.Equal(reg.Patient!.Id, entry.PatientId);
        Assert.Equal(Hall, entry.SpecialistId);
        Assert.Equal(90, entry.DurationMinutes);
        Assert.Equal(Funder.Insurer, entry.Funder);
        Assert.Contains("Booking request EB000001", entry.Notes, StringComparison.Ordinal);
        Assert.Contains("Not Fridays", entry.Notes, StringComparison.Ordinal);
        Assert.Contains("Second on the list", entry.Notes, StringComparison.Ordinal);
        var referral = await scope.ServiceProvider.GetRequiredService<ReferralsDbContext>().Referrals.AsNoTracking().SingleAsync(x => x.Id == accepted!.ReferralId);
        Assert.Equal(ReferralStatus.Accepted, referral.Status);
        Assert.Equal(ReferralSource.Specialist, referral.Source);
        Assert.Equal("Nelson Surgical Rooms", referral.ReferrerOrganisation);
        var docs = await scope.ServiceProvider.GetRequiredService<DocumentService>().ListAsync(reg.Patient.Id);
        Assert.Single(docs, d => d.Category == "Referral");

        var after = (await svc.ListAsync("all", [Hall])).Single();
        Assert.Equal("Accepted", after.Status);
        Assert.Equal("Waiting", after.WaitlistStatus);
        Assert.NotNull(await svc.DeclineAsync(r.Id, "too late", "reception")); // already accepted
        Assert.NotNull(await svc.AddMessageAsync(r.Id, "Any news?", false, "pa", [Hall])); // closed
    }

    [Fact]
    public async Task Declined_and_withdrawn_requests_need_a_reason()
    {
        await using var h = new ModulesHost();
        await using var scope = h.Scope();
        var svc = scope.ServiceProvider.GetRequiredService<EBookingService>();
        var a = (await svc.SubmitAsync(Input(), EBookingSource.Portal, "pa", [Hall])).Request!;
        var b = (await svc.SubmitAsync(Input(nhi: null, family: "Smith", given: "Jo"), EBookingSource.Portal, "pa", [Hall])).Request!;
        Assert.NotNull(await svc.DeclineAsync(a.Id, " ", "reception"));
        Assert.Null(await svc.DeclineAsync(a.Id, "ASA 4 – not suitable for day surgery", "reception"));
        Assert.NotNull(await svc.WithdrawAsync(b.Id, "", "pa", [Hall]));
        Assert.Equal("Booking request not found.", await svc.WithdrawAsync(b.Id, "Patient changed mind", "pa", [Lee]));
        Assert.Null(await svc.WithdrawAsync(b.Id, "Patient changed mind", "pa", [Hall]));
        Assert.Equal(2, (await svc.ListAsync("closed", null)).Count);
        Assert.Contains("No NHI number was sent.", EBookingService.Flags((await svc.GetAsync(b.Id, null))!));
    }

    [Fact]
    public async Task Json_booking_files_are_imported_once()
    {
        await using var h = new ModulesHost();
        await using var scope = h.Scope();
        var importer = scope.ServiceProvider.GetRequiredService<EBookingImporter>();
        var json = $$"""
            { "bookings": [
              { "reference": "R-1", "specialistName": "HALL, Ben", "contactName": "PA", "patient": { "nhi": "ZSC21TN", "familyName": "Ngātai", "givenNames": "Aroha", "dateOfBirth": "1980-05-17", "sex": "F" },
                "procedure": "Right carpal tunnel release", "durationMinutes": 30, "priority": "semi-urgent", "funder": "ACC", "funderReference": "ACC-9", "anaesthetic": "Local",
                "attachments": [ { "category": "Consent", "fileName": "consent.pdf", "contentBase64": "{{Pdf}}" } ] },
              { "reference": "R-2", "specialistName": "Dr Nobody", "patient": { "familyName": "X", "givenNames": "Y", "dateOfBirth": "1990-01-01" }, "procedure": "Something" },
              { "reference": "R-3", "specialistName": "Dr Ben Hall", "patient": { "familyName": "Lee", "givenNames": "Sam", "dateOfBirth": "1990-01-01" }, "procedure": "Excision", "anaesthetic": "Laughing gas" }
            ] }
            """;
        var r = await importer.ImportAsync("rooms.json", Encoding.UTF8.GetBytes(json), "");
        Assert.Equal(1, r.Received);
        Assert.Contains(r.Problems, p => p.StartsWith("Booking R-2: the specialist was not found", StringComparison.Ordinal));
        Assert.Contains(r.Problems, p => p.StartsWith("Booking R-3:", StringComparison.Ordinal) && p.Contains("anaesthetic", StringComparison.Ordinal));
        var req = await scope.ServiceProvider.GetRequiredService<EBookingsDbContext>().Requests.Include(x => x.Files).AsNoTracking().SingleAsync();
        Assert.Equal(Hall, req.SpecialistId);
        Assert.Equal(EBookingSource.File, req.Source);
        Assert.Equal(Laterality.Right, req.Laterality);
        Assert.Equal(Priority.SemiUrgent, req.Priority);
        Assert.Equal(Funder.Acc, req.Funder);
        Assert.Equal(AnaestheticType.Local, req.Anaesthetic);
        Assert.Equal(DocumentCategory.Consent, Assert.Single(req.Files).Category);
        Assert.Equal("file:rooms.json", req.SubmittedBy);

        var again = await importer.ImportAsync("rooms.json", Encoding.UTF8.GetBytes(json), "");
        Assert.Equal(0, again.Received);
        Assert.Contains(again.Problems, p => p.Contains("R-1 for this specialist has already been received", StringComparison.Ordinal));
        Assert.Single((await importer.ImportAsync("bad.json", "{ not json"u8.ToArray(), "")).Problems);
        Assert.Contains("not a BrightPAS booking file", (await importer.ImportAsync("x.csv", "a,b,c"u8.ToArray(), "")).Problems[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hl7_referrals_are_read()
    {
        await using var h = new ModulesHost();
        await using (var s0 = h.Scope())
        {
            var core = s0.ServiceProvider.GetRequiredService<PasDbContext>();
            (await core.Clinicians.SingleAsync(c => c.Id == Lee)).HpiCpn = "15DEME";
            await core.SaveChangesAsync();
        }
        await using var scope = h.Scope();
        var hl7 = string.Join("\r",
            @"MSH|^~\&|GASTROPMS|TASMAN GASTRO|BRIGHTPAS|TDS|202611090745||REF^I12^REF_I12|MSG001|P|2.4",
            "RF1|A|S|SURG|||TG-55120|20261109",
            "PRD|RP|Moore^Rachel|||03 555 0410^^^gastro@example.co.nz||",
            "PRD|RT|Lindqvist^Sarah^^^Dr|||||15DEME",
            "PID|1||M123^^^ROOMS^MR~ZSC21TN^^^NHI^NH||Tane^Wiremu^James||19791123|M|||22 Example Road^^Richmond^^7020||021 555 0123",
            "PR1|1||COLON^Colonoscopy",
            "DG1|1||K92.1^Iron deficiency anaemia",
            "AL1|1|DA|^Latex",
            "OBX|1|NM|29463-7^Body weight||88|kg",
            "OBX|2|NM|8302-2^Body height||176|cm",
            "OBX|3|ST|ASA^ASA grade||ASA 2",
            $"OBX|4|ED|REFLTR^Referral letter||^application^pdf^Base64^{Pdf}",
            "NTE|1||Positive FIT.",
            "IN1|1|SC|SX|Southern Cross||||||||||PA-90210");
        var r = await scope.ServiceProvider.GetRequiredService<EBookingImporter>().ImportAsync("gastro.hl7", Encoding.UTF8.GetBytes(hl7 + "\r\n" + hl7.Replace("MSG001", "MSG002", StringComparison.Ordinal).Replace("REF^I12", "ADT^A01", StringComparison.Ordinal)), "");
        Assert.Equal(1, r.Received);
        Assert.Contains(r.Problems, p => p.Contains("ADT^A01, not a referral", StringComparison.Ordinal));
        var req = await scope.ServiceProvider.GetRequiredService<EBookingsDbContext>().Requests.Include(x => x.Files).AsNoTracking().SingleAsync();
        Assert.Equal(Lee, req.SpecialistId);
        Assert.Equal(EBookingSource.Hl7, req.Source);
        Assert.Equal("TG-55120", req.ExternalRef);
        Assert.Equal("ZSC21TN", req.Nhi);
        Assert.Equal(("Tane", "Wiremu James"), (req.FamilyName, req.GivenNames));
        Assert.Equal(new DateOnly(1979, 11, 23), req.DateOfBirth);
        Assert.Equal(BirthSex.Male, req.Sex);
        Assert.Equal("Colonoscopy", req.Procedure);
        Assert.Equal(Priority.Urgent, req.Priority);
        Assert.Equal((88m, 176m, 2), (req.WeightKg!.Value, req.HeightCm!.Value, req.Asa!.Value));
        Assert.Equal("Latex", req.Allergies);
        Assert.Equal(Funder.Insurer, req.Funder);
        Assert.Equal(("Southern Cross", "PA-90210"), (req.InsurerName, req.FunderReference));
        Assert.Contains("Iron deficiency anaemia", req.ClinicalNotes, StringComparison.Ordinal);
        Assert.Contains("Positive FIT.", req.ClinicalNotes, StringComparison.Ordinal);
        Assert.Equal(("Rachel Moore", "gastro@example.co.nz"), (req.ContactName, req.ContactEmail));
        Assert.Equal(DocumentCategory.Referral, Assert.Single(req.Files).Category);
    }

    [Fact]
    public async Task Merging_patients_moves_requests_and_names_compare()
    {
        await using var h = new ModulesHost();
        var keep = await h.PatientAsync("A", "B", null);
        var dup = await h.PatientAsync("A", "B2", null);
        await using var scope = h.Scope();
        var svc = scope.ServiceProvider.GetRequiredService<EBookingService>();
        var r = (await svc.SubmitAsync(Input(nhi: null, family: "A", given: "B2"), EBookingSource.Portal, "pa", null)).Request!;
        Assert.Null(await svc.MatchPatientAsync(r.Id, dup.Id, "reception"));
        Assert.Equal(1, await svc.MergeAsync(keep.Id, dup.Id));
        Assert.Equal(keep.Id, (await svc.GetAsync(r.Id, null))!.PatientId);

        Assert.Equal(EBookingImporter.NameKey("Dr Jane Smith"), EBookingImporter.NameKey("SMITH, Jane"));
        Assert.Equal(Laterality.Left, EBookingImporter.Side(null, "Left knee arthroscopy"));
        Assert.Equal(Laterality.Bilateral, EBookingImporter.Side("B", null));
        Assert.Equal(Funder.Insurer, EBookingImporter.FunderOf(null, "nib"));
        Assert.Equal(25.0m, EBookingService.Bmi(new BookingRequest { WeightKg = 81, HeightCm = 180 }));
    }
}
