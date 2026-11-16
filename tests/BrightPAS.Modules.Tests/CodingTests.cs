using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Modules.Coding;
using BrightPAS.Modules.Emr;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Referrals;

namespace BrightPAS.Modules.Tests;

/// <summary>0.20.0 Coding &amp; statutory reporting: edit checks (ACS and NZ conventions), the NMDS private hospital load file, NBRS batches and acknowledgements.</summary>
public class CodingTests
{
    private static readonly DateOnly Adm = new(2026, 11, 2);

    private static ClinicalCode C(string code, string type, string desc, string? sex = null, bool never = false) =>
        new() { System = "16", Code = CodingChecks.Normalise(code), Type = type, Description = desc, Sex = sex, NeverAssign = never };

    /// <summary>A handful of codes for tests (descriptions written for the tests, not the classification's).</summary>
    public static readonly ClinicalCode[] Table =
    [
        C("K63.5", "A", "Polyp of colon"), C("Z12.1", "V", "Screening for neoplasm of intestinal tract"), C("N80.0", "A", "Endometriosis of uterus", "F"),
        C("S52.50", "B", "Fracture of lower end of radius"), C("W19", "E", "Unspecified fall"), C("Y92.09", "E", "Home"), C("U73.9", "E", "Unspecified activity"),
        C("Z03.6", "V", "Observation for suspected toxic effect"), C("X49", "E", "Accidental poisoning"), C("R69", "A", "Unknown causes", never: true),
        C("32093-00", "O", "Fibreoptic colonoscopy to caecum"), C("32229-00", "O", "Endoscopic polypectomy of large intestine"), C("92515-99", "O", "Sedation, ASA 99"),
        C("92514-19", "O", "General anaesthesia, ASA 1"), C("92513-19", "O", "Infiltration of local anaesthetic"), C("30614-00", "O", "Inguinal hernia repair with mesh"),
        C("F64.0", "A", "Gender incongruence"), C("Z41.89", "V", "Other procedures for purposes other than remedying health state"),
    ];

    private static Dictionary<string, ClinicalCode> Dict() => Table.ToDictionary(c => c.Code);

    private static CodedItem I(string type, string code, string desc, DateOnly? date = null) => new() { DiagnosisType = type, Code = CodingChecks.Normalise(code), Description = desc, Date = date };

    private static EventFacts Facts(string sex = "F", string? anaesthetic = "Sedation", DateOnly? discharge = null) =>
        new(sex, new DateOnly(1960, 5, 1), Adm, discharge ?? Adm, anaesthetic, "06", "DR", "R");

    private static IReadOnlyList<CodingCheck> Run(EventFacts f, params CodedItem[] items) => CodingChecks.Check(f, items, Dict(), [], new DateOnly(2026, 11, 9));

    [Fact]
    public void Clean_colonoscopy_passes_and_display_formats_codes()
    {
        var r = Run(Facts(), I("A", "K63.5", "Sigmoid polyp 8 mm"), I("O", "32093-00", "Colonoscopy to caecum", Adm), I("O", "32229-00", "Cold snare polypectomy", Adm), I("O", "92515-99", "IV sedation", Adm));
        Assert.DoesNotContain(r, c => c.Level != CheckLevel.Advice);
        Assert.Equal("K63.5", CodingChecks.Display("K635")); Assert.Equal("32093-00", CodingChecks.Display("3209300")); Assert.Equal("3209300", CodingChecks.Normalise("32093-00"));
    }

    [Fact]
    public void Edit_checks_catch_NMDS_ACS_and_NZ_convention_errors()
    {
        string[] Errors(IReadOnlyList<CodingCheck> r) => r.Where(c => c.Level == CheckLevel.Error).Select(c => c.Rule).ToArray();
        Assert.Contains("NMDS", Errors(Run(Facts(), I("O", "32093-00", "Colonoscopy", Adm)))); // no principal diagnosis
        Assert.Contains("NMDS", Errors(Run(Facts(), I("A", "K63.5", "Polyp"), I("A", "N80.0", "Endometriosis")))); // two principals
        Assert.Contains("Sex edit", Errors(Run(Facts(sex: "M"), I("A", "N80.0", "Endometriosis"))));
        Assert.Contains("ACS 0049", Errors(Run(Facts(), I("A", "R69", "Unwell"))));
        Assert.Contains("Code table", Errors(Run(Facts(), I("A", "Q99.9", "Not in table"))));
        Assert.Contains("Code table", Errors(Run(Facts(), I("A", "32093-00", "Procedure as diagnosis"))));
        Assert.Contains("NMDS", Errors(Run(Facts(), I("A", "K63.5", "Polyp"), I("O", "32093-00", "Colonoscopy")))); // procedure without date
        Assert.Contains("NMDS", Errors(Run(Facts(), I("A", "K63.5", "Polyp"), I("O", "32093-00", "Colonoscopy", Adm.AddDays(3))))); // outside admission
        Assert.Contains("ACS 0031", Errors(Run(Facts(), I("A", "K63.5", "Polyp"), I("O", "32093-00", "Colonoscopy", Adm), I("O", "92513-19", "Local", Adm))));
        Assert.Contains("ACS 2001", Errors(Run(Facts(), I("A", "S52.50", "Distal radius fracture"), I("O", "32093-00", "x", Adm))));
        // Z03.6: substance alone is not enough – place (Y92) and activity (U50–U73) too.
        Assert.Contains("NZ convention (ACS 2001)", Errors(Run(Facts(), I("A", "Z03.6", "Observation after ingestion"), I("E", "X49", "Took grandmother's tablets", Adm))));
        Assert.DoesNotContain("NZ convention (ACS 2001)", Errors(Run(Facts(), I("A", "Z03.6", "Observation after ingestion"), I("E", "X49", "Took tablets", Adm), I("E", "Y92.09", "At home", Adm), I("E", "U73.9", "Not known", Adm))));
        Assert.Contains("NMDS", Errors(Run(Facts(discharge: new DateOnly(2026, 11, 20)), I("A", "K63.5", "Polyp")))); // future discharge
    }

    [Fact]
    public void Warnings_for_free_text_anaesthesia_mesh_and_gender_reaffirming_surgery()
    {
        string[] Warn(IReadOnlyList<CodingCheck> r) => r.Where(c => c.Level == CheckLevel.Warning).Select(c => c.Rule).ToArray();
        // Injury described with the classification title, not free text.
        Assert.Contains("NZ NMDS", Warn(Run(Facts(), I("A", "S52.50", "Fracture of lower end of radius"), I("E", "W19", "Tripped on rug", Adm))));
        Assert.Contains("ACS 0031", Warn(Run(Facts(anaesthetic: "General"), I("A", "K63.5", "Polyp"), I("O", "32093-00", "Colonoscopy", Adm))));
        Assert.DoesNotContain("ACS 0031", Warn(Run(Facts(anaesthetic: "Local"), I("A", "K63.5", "Polyp"), I("O", "32093-00", "Colonoscopy", Adm))));
        Assert.Contains("NZ NMDS", Warn(Run(Facts(), I("A", "K63.5", "x"), I("O", "30614-00", "Hernia repair with mesh", Adm), I("O", "92514-19", "GA", Adm))));
        Assert.DoesNotContain("NZ NMDS", Warn(Run(Facts(), I("A", "K63.5", "x"), I("O", "30614-00", "Hernia repair, ProGrip 15x9 cm mesh", Adm), I("O", "92514-19", "GA", Adm))));
        Assert.Contains("NZ convention", Warn(Run(Facts(), I("A", "F64.0", "Gender incongruence"), I("O", "30614-00", "x 1", Adm), I("O", "92514-19", "GA", Adm))));
        Assert.DoesNotContain("NZ convention", Warn(Run(Facts(), I("A", "Z41.89", "Gender affirming"), I("B", "F64.0", "Gender incongruence"), I("O", "30614-00", "x 1", Adm), I("O", "92514-19", "GA", Adm))));
        // Advice is shown only when it applies in NZ.
        var advice = new[] { new CodingAdvice { Reference = "Q1", Title = "NZ", CodePrefixes = "K63", Summary = "s", AppliesToNz = true }, new CodingAdvice { Reference = "Q2", Title = "13th", CodePrefixes = "K63", Summary = "s", AppliesToNz = false } };
        var r = CodingChecks.Check(Facts(), [I("A", "K63.5", "Polyp")], Dict(), advice, new DateOnly(2026, 11, 9));
        Assert.Single(r, c => c.Level == CheckLevel.Advice); Assert.Equal("Q1", r.Single(c => c.Level == CheckLevel.Advice).Rule);
    }

    [Fact]
    public void Nmds_load_file_layout_quotes_names_and_repeating_groups()
    {
        var p = new Patient { Mrn = "TDS0000001", Nhi = "ZSC21TN", FamilyName = "Ngātai", GivenNames = "Aroha Mere Jane", DateOfBirth = new DateOnly(1960, 5, 1), Sex = BirthSex.Female, AddressLine1 = "1 Main Road", City = "Nelson" };
        p.Ethnicities.Add(new PatientEthnicity { Code = "21111", Order = 1 }); p.Ethnicities.Add(new PatientEthnicity { Code = "61199", Order = 2 });
        var e = new CodedEpisode { EpisodeId = Guid.NewGuid(), AdmissionDate = Adm, DischargeDate = Adm, PurchaserCode = "06", AdmissionSource = "R", EventEndType = "DR" };
        var csv = NmdsService.ToAscii(NmdsService.BuildCsv([new NmdsRecord(NmdsService.RecordId(e), "06", "1234", "5678", p, e,
            [I("A", "K63.5", "Sigmoid polyp, \"8 mm\""), I("O", "32093-00", "Colonoscopy", Adm)], "16")]));
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("ID,PURCHASER CODE,HEALTH AGENCY,HAF,NHI,SURNAME", lines[0]);
        Assert.EndsWith("DIAGTYPE02,DESC02,DIAGCODE02,CODESYS02,OPDATE02", lines[0]);
        var row = Csv.Parse(lines[1])[0];
        Assert.Equal(lines[0].Split(',').Length, row.Count);
        Assert.Equal(14, row[0].Length); Assert.StartsWith("TDS", row[0]);
        Assert.Equal("Ngatai", row[5]); Assert.Equal(["Aroha", "Mere", "Jane"], row[6..9]);
        Assert.Equal("19601101".Length, row[14].Length); Assert.Equal("21", row[17]); Assert.Equal("NZ Maori", row[18]);
        Assert.Equal("", row[19]); Assert.Equal("Other ethnicity", row[20]); // 61 is not in the load file table: description only
        Assert.Equal("F", row[23]); Assert.Equal("20261102", row[26]); Assert.Equal("DR", row[28]);
        Assert.Equal(["A", "Sigmoid polyp, '8 mm'", "K635", "16", ""], row[33..38]);
        Assert.Equal(["O", "Colonoscopy", "3209300", "16", "20261102"], row[38..43]);
        Assert.Equal("ABC02611.CSV", NmdsService.FileName("abc", new DateOnly(2026, 11, 30)));
    }

    [Fact]
    public void Nbrs_batch_records_follow_the_specification_and_acknowledgements_are_read()
    {
        var n = new NbrsEntry
        {
            LocalId = "W123",
            FacilityCode = "F123",
            BookingSource = "2",
            ReferralDate = new(2026, 8, 1),
            FirstAssessmentDate = new(2026, 8, 20),
            CpacDate = new(2026, 8, 20),
            CpacScore = "65",
            CpacSystem = "GS01",
            PurchaserCode = "35",
            HealthSpecialty = "S05",
            ClinicalCode = "3209300",
            ClinicalCodeType = "O",
            DomicileCode = "2077",
            ProfessionalGroup = "MC",
            ResponsibilityCode = "12345",
            AssessorCode = "12345",
            AssessorGroup = "MC",
            StagedFlag = "1",
        };
        var book = new NbrsEvent { Action = "A", StatusCode = "01", StatusDate = new(2026, 9, 1), BookingMadeDate = new(2026, 9, 1), BookedForDate = new(2026, 11, 2), EventLocalId = 0 };
        Assert.Empty(NbrsService.Validate(n, book, "ZSC21TN", [], "A123"));
        var be = NbrsService.BeRecord(n, book, "ZSC21TN", "16").Split(',');
        Assert.Equal(35, be.Length);
        Assert.Equal(["BE", "A", "F123", "W123", "01", "20260901", "ZSC21TN", "20260801", "2", "20260820", "20260820", "65", "GS01"], be[..13]);
        Assert.Equal(["20260901", "20261102", "35"], be[14..17]);
        Assert.Equal(["S05", "", "3209300", "O", "16"], be[20..25]);
        Assert.Equal(["00", "MC", "12345", "2077", "12345", "MC"], be[29..35]);
        book.Result = NbrsResult.Accepted;

        // Treated exit: needs the client system identifier and a date after the last status date.
        var exit = new NbrsEvent { Action = "A", StatusCode = "20", ExitCategory = "11", ExitDate = new(2026, 11, 2), TreatmentFacility = "F123" };
        Assert.Contains(NbrsService.Validate(n, exit, "ZSC21TN", [book], "A123"), e => e.StartsWith("E48", StringComparison.Ordinal));
        exit.ClientSystemId = "TDS0123456789A";
        Assert.Empty(NbrsService.Validate(n, exit, "ZSC21TN", [book], "A123"));
        var ex = NbrsService.BeRecord(n, exit, "ZSC21TN", "16").Split(',');
        Assert.Equal("20", ex[4]); Assert.Equal("", ex[5]); Assert.Equal("TDS0123456789A", ex[19]); Assert.Equal(["20261102", "11"], ex[26..28]);
        Assert.Contains(NbrsService.Validate(n, new NbrsEvent { Action = "A", StatusCode = "01", StatusDate = new(2026, 8, 30), BookingMadeDate = new(2026, 9, 1), BookedForDate = new(2026, 11, 2) }, "ZSC21TN", [book], "A123"),
            e => e.StartsWith("E8", StringComparison.Ordinal));
        Assert.Contains(NbrsService.Validate(n, book, "ZZZ0000", [], "A123"), e => e.StartsWith("E4", StringComparison.Ordinal));

        var batch = NbrsService.BuildBatch("A123", "abc", 7, "CMPL", new DateTimeOffset(2026, 11, 9, 14, 5, 0, TimeSpan.FromHours(13)), [string.Join(',', be)]);
        var lines = batch.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("BH,A123,ABC00007.NBR,00001,20261109,20261109,1405,CMPL,V04.0", lines[0]);
        Assert.Equal(2, lines.Length); Assert.EndsWith("\r\n", batch, StringComparison.Ordinal);

        var ack = NbrsService.ParseAck("AH,A123,ABC00007.NBK,00002,20261109,20261109,1405,CMPL,V04.0,00002,00000,00001,00001,00001,20261110\r\n" +
            "AC,00002,NBR4004,\"Data record accepted\"\r\nWN,NBR3005,\"Booked procedure code ignored\"\r\nRJ,00003,NBR4005,\"Data record rejected\"\r\nER,NBR1048,\"Client system identifier not supplied\"\r\n");
        Assert.Equal("ABC00007.NBK", ack.FileName); Assert.Equal((1, 1, 1), (ack.Accepted, ack.Rejected, ack.Warnings));
        Assert.Equal((2, true), (ack.Lines[0].Line, ack.Lines[0].Ok)); Assert.Contains("Warning NBR3005", ack.Lines[0].Messages[0]);
        Assert.Equal((3, false), (ack.Lines[1].Line, ack.Lines[1].Ok)); Assert.Contains("Client system identifier", ack.Lines[1].Messages[0]);
    }

    private static async Task<(Episode Episode, Patient Patient)> DischargedAsync(ModulesHost h, IServiceProvider sp)
    {
        var (e, p) = await DevicesTests.AdmittedAsync(h, sp);
        var emr = sp.GetRequiredService<EmrDbContext>();
        var ep = await emr.Episodes.SingleAsync(x => x.Id == e.Id);
        ep.Stage = EpisodeStage.Discharged; ep.DischargedAt = h.Clock.GetUtcNow();
        await emr.SaveChangesAsync();
        var patients = sp.GetRequiredService<PatientsDbContext>();
        var pp = await patients.Patients.SingleAsync(x => x.Id == p.Id);
        pp.AddressLine1 = "12 Lord Rutherford Road"; pp.City = "Brightwater";
        await patients.SaveChangesAsync();
        return (ep, pp);
    }

    [Fact]
    public async Task Queue_code_send_return_correct_and_resend()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var sp = s.ServiceProvider;
        var db = sp.GetRequiredService<CodingDbContext>();
        db.Codes.AddRange(Table.Select(c => new ClinicalCode { System = c.System, Code = c.Code, Type = c.Type, Description = c.Description, Sex = c.Sex, NeverAssign = c.NeverAssign }));
        await db.SaveChangesAsync();
        var coding = sp.GetRequiredService<CodingService>();
        var nmds = sp.GetRequiredService<NmdsService>();
        var (ep, p) = await DischargedAsync(h, sp);

        var q = await coding.QueueAsync("open");
        var row = Assert.Single(q);
        Assert.Equal("ToCode", row.Status); Assert.Equal("06", row.Purchaser);
        var today = BrightPAS.Core.NzTime.Today(h.Clock);

        // Errors keep it "in progress"; fixed, it is coded.
        var (e1, checks, errors) = await coding.SaveAsync(row.Id, new CodingSave(0, "06", "R", "DR", null, [new("O", "32093-00", "Colonoscopy", today)], true), "coder");
        Assert.Equal(CodingStatus.InProgress, e1!.Status); Assert.NotEmpty(errors); Assert.Contains(checks, c => c.Message.Contains("principal", StringComparison.Ordinal));
        Assert.NotEmpty((await coding.SaveAsync(row.Id, new CodingSave(0, "06", "R", "DR", null, [], false), "coder")).Errors); // stale version
        var (e2, _, err2) = await coding.SaveAsync(row.Id, new CodingSave(1, "06", "R", "DR", null,
            [new("O", "32093-00", "Colonoscopy to caecum", today), new("A", "K63.5", "Sigmoid polyp", null), new("O", "92515-99", "IV sedation", today)], true), "coder");
        Assert.Empty(err2); Assert.Equal(CodingStatus.Coded, e2!.Status);
        Assert.Equal(["A", "O", "O"], e2.Items.OrderBy(i => i.Sequence).Select(i => i.DiagnosisType)); // principal first

        // NMDS: identifiers needed, then the file.
        Assert.NotEmpty((await nmds.CreateAsync(today, "coder")).Errors);
        Assert.Empty(await coding.SaveSettingsAsync(new CodingSettingsEdit("1234", "5678", "TDS", "06", "06", "A0", "35", "06", false, 21, "1234", "CMPL", "16", "ICD-10-AM/ACHI/ACS Twelfth Edition"), "admin"));
        var (file, ferr) = await nmds.CreateAsync(today, "coder");
        Assert.Empty(ferr); Assert.Equal(1, file!.Records); Assert.Equal($"TDS0{today:yyMM}.CSV", file.FileName); Assert.False(file.IsResubmission);
        var text = Encoding.ASCII.GetString(file.Content);
        Assert.Contains("\"K635\"", text); Assert.Contains("\"Ngatai\"", text);
        Assert.Equal(CodingStatus.Submitted, (await db.Episodes.AsNoTracking().SingleAsync()).Status);
        Assert.Null(await nmds.MarkAsync(file.Id, SubmissionStatus.Sent, null, "coder"));

        // Health NZ returns an error: corrected, it goes in the next file as a resubmission with the same record id.
        Assert.Null(await coding.RecordHealthNzErrorAsync(row.Id, "Domicile could not be derived", "coder"));
        Assert.Equal("Rejected", Assert.Single(await coding.QueueAsync("open")).Status);
        var cur = await db.Episodes.AsNoTracking().SingleAsync();
        Assert.Empty((await coding.SaveAsync(row.Id, new CodingSave(cur.Version, "06", "R", "DR", "Address corrected", [new("A", "K63.5", "Sigmoid polyp", null), new("O", "32093-00", "Colonoscopy", today), new("O", "92515-99", "IV sedation", today)], true), "coder")).Errors);
        var (again, _) = await nmds.CreateAsync(today, "coder");
        Assert.True(again!.IsResubmission);
        var ids = await db.SubmissionEvents.AsNoTracking().Select(x => x.RecordId).Distinct().ToListAsync();
        Assert.Single(ids);
    }

    [Fact]
    public async Task Contracted_Health_NZ_events_are_not_reported_by_TDS_unless_set()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var sp = s.ServiceProvider;
        var coding = sp.GetRequiredService<CodingService>();
        var (ep, _) = await DischargedAsync(h, sp);
        var theatre = sp.GetRequiredService<BrightPAS.Modules.Theatre.TheatreDbContext>();
        var session = new BrightPAS.Modules.Theatre.TheatreSession { LocationId = Guid.NewGuid(), SurgeonId = Guid.NewGuid(), Date = new DateOnly(2026, 11, 9), StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(12, 0) };
        theatre.Sessions.Add(session);
        theatre.Bookings.Add(new BrightPAS.Modules.Theatre.TheatreBooking { Id = ep.BookingId, SessionId = session.Id, PatientId = ep.PatientId, Procedure = "Colonoscopy", Payment = BrightPAS.Modules.Theatre.PaymentType.HealthNz, StatusAt = h.Clock.GetUtcNow(), CreatedAt = h.Clock.GetUtcNow() });
        await theatre.SaveChangesAsync();
        var row = Assert.Single(await coding.QueueAsync("NotReportable"));
        Assert.Equal("35", row.Purchaser);
        Assert.NotNull(await coding.SetReportableAsync(row.Id, false, " ", "coder"));
        Assert.Null(await coding.SetReportableAsync(row.Id, true, null, "coder"));
        Assert.Equal("ToCode", Assert.Single(await coding.QueueAsync("open")).Status);
    }

    [Fact]
    public async Task Code_table_import_reads_the_Health_NZ_columns_and_updates()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var coding = s.ServiceProvider.GetRequiredService<CodingService>();
        var csv = "clinical_code_system,clinical_code,clinical_code_type,clinical_code_description,sex_flag,never_assigned_diagnosis_flag,end_date\r\n" +
                  "16,K635,A,\"Polyp of colon, test\",,N,\r\n16,N800,A,Endometriosis of uterus,F,N,\r\n16,R69,A,Unknown,,Y,\r\n16,K999,A,Retired,,N,20200101\r\n16,,A,,,,\r\n16,XX,Z,Bad type,,,\r\n";
        var r = await coding.ImportCodesAsync(csv, "admin");
        Assert.Equal(4, r.Added); Assert.Single(r.Problems);
        Assert.Equal(3, await coding.CodeCountAsync());
        Assert.Equal("Polyp of colon, test", (await coding.SearchCodesAsync("K63", null)).Single().Description);
        Assert.Equal("F", (await coding.SearchCodesAsync("endometriosis", "A")).Single().Sex);
        Assert.Equal(4, (await coding.ImportCodesAsync(csv, "admin")).Updated);
    }

    [Fact]
    public async Task Nbrs_entry_from_waitlist_events_batch_numbering_and_acknowledgement()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var sp = s.ServiceProvider;
        var coding = sp.GetRequiredService<CodingService>();
        var nbrs = sp.GetRequiredService<NbrsService>();
        var p = await h.PatientAsync();
        var referrals = sp.GetRequiredService<ReferralsDbContext>();
        var w = new WaitlistEntry { PatientId = p.Id, Procedure = "Colonoscopy", Funder = Funder.HealthNz, Service = ServiceType.Endoscopy, AddedOn = new DateOnly(2026, 8, 20), TargetDate = new DateOnly(2026, 12, 1), StatusAt = h.Clock.GetUtcNow(), CreatedAt = h.Clock.GetUtcNow() };
        referrals.Waitlist.Add(w); await referrals.SaveChangesAsync();
        Assert.Single(await nbrs.UnreportedWaitlistAsync());
        Assert.Empty(await coding.SaveSettingsAsync(new CodingSettingsEdit("1234", "F123", "TDS", "06", "06", "A0", "35", "06", true, 21, "A123", "CMPL", "16", "Twelfth Edition"), "admin"));
        var d = await nbrs.DraftFromWaitlistAsync(w.Id);
        Assert.Equal("35", d.PurchaserCode);
        var (n, errs) = await nbrs.SaveEntryAsync(d with
        {
            BookingSource = "2",
            ReferralDate = new(2026, 8, 1),
            FirstAssessmentDate = new(2026, 8, 20),
            CpacDate = new(2026, 8, 20),
            CpacScore = "60",
            CpacSystem = "GS01",
            HealthSpecialty = "S05",
            ClinicalCode = "32093-00",
            DomicileCode = "2077",
            ResponsibilityCode = "12345",
            AssessorCode = "12345",
        }, "coder");
        Assert.Empty(errs);
        Assert.NotEmpty((await nbrs.AddEventAsync(n!.Id, new NbrsEventEdit("treated", null, null, null, null, null, new(2026, 11, 2), null, null, null, "X"), "coder")).Errors); // not booked yet
        var (ev, everr) = await nbrs.AddEventAsync(n.Id, new NbrsEventEdit("book", new(2026, 9, 1), null, new(2026, 9, 1), new(2026, 11, 2), null, null, null, null, null, null), "coder");
        Assert.Empty(everr);
        Assert.NotEmpty((await nbrs.AddEventAsync(n.Id, new NbrsEventEdit("book", new(2026, 9, 2), null, new(2026, 9, 2), new(2026, 11, 2), null, null, null, null, null, null), "coder")).Errors); // earlier not sent
        var (b1, berr) = await nbrs.CreateBatchAsync("coder");
        Assert.Empty(berr); Assert.Equal("TDS00001.NBR", b1!.FileName);
        Assert.Contains("BH,A123,TDS00001.NBR,00001,", Encoding.ASCII.GetString(b1.Content));
        Assert.NotEmpty((await nbrs.CreateBatchAsync("coder")).Errors); // nothing waiting
        var (ack, aerr) = await nbrs.ImportAckAsync("AH,A123,TDS00001.NBK,00001\r\nAC,00002,NBR4004,\"Data record accepted\"\r\n", "coder");
        Assert.Empty(aerr); Assert.Equal(1, ack!.Accepted);
        var db = sp.GetRequiredService<CodingDbContext>();
        Assert.Equal(NbrsResult.Accepted, (await db.NbrsEvents.SingleAsync(x => x.Id == ev!.Id)).Result);
        var (_, terr) = await nbrs.AddEventAsync(n.Id, new NbrsEventEdit("treated", null, null, null, null, null, new(2026, 11, 2), null, null, null, "TDS0123456789A"), "coder");
        Assert.Empty(terr);
        Assert.Equal("TDS00002.NBR", (await nbrs.CreateBatchAsync("coder")).Batch!.FileName); // gap-free numbering
    }
}
