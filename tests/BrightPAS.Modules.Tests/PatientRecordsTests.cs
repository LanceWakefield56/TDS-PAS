using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Data;
using BrightPAS.Core.Organisation;
using BrightPAS.Modules.Credentialing;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Portal;
using BrightPAS.Modules.Referrals;
using BrightPAS.Modules.Rostering;
using BrightPAS.Modules.Theatre;

namespace BrightPAS.Modules.Tests;

/// <summary>0.10.0: documents, identity correction, merge, labels, swaps, payroll, NBRS, credentialing, portals.</summary>
public class PatientRecordsTests
{
    private static readonly DateOnly Mon = new(2026, 11, 9);
    private static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF"u8.ToArray();

    [Fact]
    public async Task Documents_accept_only_real_scans_are_encrypted_hash_checked_and_soft_deleted()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<DocumentService>();
        var (bad, errors) = await svc.AddAsync(new(p.Id, null, DocumentCategory.Consent, null, "evil.pdf", Convert.ToBase64String("<script>alert(1)</script>"u8.ToArray()), DocumentSource.Upload), "rn");
        Assert.Null(bad);
        Assert.Contains(errors, e => e.Contains("PDF, JPEG, PNG and TIFF"));
        var (doc, _) = await svc.AddAsync(new(p.Id, null, DocumentCategory.Consent, "Consent form", "../../Consent form.pdf", Convert.ToBase64String(Pdf), DocumentSource.Scan), "rn");
        Assert.NotNull(doc);
        Assert.Equal("Consent form.pdf", doc!.FileName);
        Assert.NotEqual(Pdf, doc.Content); // stored encrypted
        var opened = await svc.OpenAsync(doc.Id, "dr", null);
        Assert.Equal(Pdf, opened!.Value.Bytes);
        // Tampering is detected.
        var db = s.ServiceProvider.GetRequiredService<PatientsDbContext>();
        var row = await db.Documents.SingleAsync(d => d.Id == doc.Id);
        row.Content = new TestDocumentCipher().Protect([.. Pdf, 0x20]);
        await db.SaveChangesAsync();
        Assert.Null(await svc.OpenAsync(doc.Id, "dr", null));
        Assert.NotNull(await svc.DeleteAsync(doc.Id, "x", "rn"));
        Assert.Null(await svc.DeleteAsync(doc.Id, "Scanned to the wrong patient", "rn"));
        Assert.Empty(await svc.ListAsync(p.Id));
    }

    [Fact]
    public async Task Identity_correction_needs_a_second_person()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<IdentityService>();
        var (none, e0) = await svc.RequestCorrectionAsync(new(p.Id, null, null, null, null, null, false, "Typo", null), "rec");
        Assert.Null(none);
        Assert.Contains(e0, e => e.Contains("Nothing to change"));
        var (c, _) = await svc.RequestCorrectionAsync(new(p.Id, "Ngatai-Smith", null, new DateOnly(1980, 5, 18), null, null, false, "Name changed on marriage; DOB typo", "Passport"), "rec");
        Assert.NotNull(c);
        Assert.Contains("second person", await svc.DecideCorrectionAsync(c!.Id, new(true, null), "rec"));
        Assert.Null(await svc.DecideCorrectionAsync(c.Id, new(true, "Seen passport"), "privacy"));
        var after = await s.ServiceProvider.GetRequiredService<PatientsDbContext>().Patients.AsNoTracking().SingleAsync(x => x.Id == p.Id);
        Assert.Equal("Ngatai-Smith", after.FamilyName);
        Assert.Equal(new DateOnly(1980, 5, 18), after.DateOfBirth);
        Assert.Equal("NGATAI-SMITH", after.FamilyNameSearch);
    }

    [Fact]
    public async Task Merge_moves_records_hides_the_duplicate_and_keeps_the_nhi()
    {
        await using var h = new ModulesHost();
        var survivor = await h.PatientAsync(nhi: null);
        var dup = await h.PatientAsync("Ngatai", "Aroha", nhi: "ZSC21TN");
        await using var s = h.Scope();
        var sp = s.ServiceProvider;
        await sp.GetRequiredService<PatientService>().AddAlertAsync(dup.Id, AlertType.Allergy, AlertSeverity.Critical, "Penicillin – anaphylaxis", "rn");
        await sp.GetRequiredService<DocumentService>().AddAsync(new(dup.Id, null, DocumentCategory.Referral, null, "r.pdf", Convert.ToBase64String(Pdf), DocumentSource.Upload), "rec");
        var pdb = sp.GetRequiredService<PatientsDbContext>();
        foreach (var x in await pdb.Patients.ToListAsync()) x.IsTestPatient = true; // ZSC21TN is a test NHI
        await pdb.SaveChangesAsync();
        var svc = sp.GetRequiredService<IdentityService>();
        var (m, errors) = await svc.RequestMergeAsync(new(survivor.Id, dup.Id, "Same person: confirmed DOB, address and NHI with patient"), "rec");
        Assert.Empty(errors);
        Assert.Contains("second person", (await svc.DecideMergeAsync(m!.Id, new(true, null), "rec")).Error);
        var (error, result) = await svc.DecideMergeAsync(m.Id, new(true, null), "privacy");
        Assert.Null(error);
        Assert.Contains("Alerts: 1", result);
        Assert.Contains("Documents: 1", result);
        var db = sp.GetRequiredService<PatientsDbContext>();
        var sv = await db.Patients.AsNoTracking().Include(x => x.Alerts).SingleAsync(x => x.Id == survivor.Id);
        Assert.Equal("ZSC21TN", sv.Nhi);
        Assert.Single(sv.Alerts);
        Assert.Equal(survivor.Id, (await db.Patients.AsNoTracking().SingleAsync(x => x.Id == dup.Id)).MergedIntoId);
        Assert.DoesNotContain(await sp.GetRequiredService<PatientService>().SearchAsync("Ngatai"), x => x.Id == dup.Id);
        Assert.NotNull((await svc.DecideMergeAsync(m.Id, new(true, null), "privacy")).Error); // cannot run twice once complete
    }

    [Fact]
    public void Wristband_zpl_strips_command_characters_and_flags_allergy()
    {
        var p = new Patient { Mrn = "TDS0000001", FamilyName = "Ngātai^XZ", GivenNames = "Aroha~JA", DateOfBirth = new(1980, 5, 17), Nhi = "ZSC21TN", Sex = BirthSex.Female };
        var z = LabelService.Wristband(p, hasAllergy: true);
        Assert.StartsWith("^XA^CI28", z);
        Assert.Contains("NGĀTAIXZ, ArohaJA", z);
        Assert.Contains("ALLERGY", z);
        Assert.EndsWith("^XZ", z);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(z, @"\^XZ"));
        Assert.Contains("^PQ3^XZ", LabelService.Label(p, 3));
        Assert.False(LabelService.AllowedPort(22));
    }

    [Fact]
    public async Task Shift_swap_is_accepted_by_colleague_then_approved_by_manager()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<RosterService>();
        var (a, _) = await svc.SaveStaffAsync(new(null, "Jacqui", StaffRole.Nurse, "jacqui", null, 32, null, true, "E001"), "mgr");
        var (b, _) = await svc.SaveStaffAsync(new(null, "Mere", StaffRole.Nurse, "mere", null, 32, null, true, "E002"), "mgr");
        var sa = (await svc.SaveShiftAsync(new(null, a!.Id, Mon.AddDays(2), false, new(7, 30), new(16, 0), 30, "Theatre", false, null), "mgr")).Shift!;
        var sb = (await svc.SaveShiftAsync(new(null, b!.Id, Mon.AddDays(3), false, new(7, 30), new(16, 0), 30, "Recovery", false, null), "mgr")).Shift!;
        var (bad, e1) = await svc.RequestSwapAsync(new(sb.Id, null, null, null), a.Id, "jacqui");
        Assert.Contains("own shifts", e1[0]);
        var (sw, _) = await svc.RequestSwapAsync(new(sa.Id, b.Id, sb.Id, "Appointment"), a.Id, "jacqui");
        Assert.NotNull(sw);
        Assert.NotNull(await svc.DecideSwapAsync(sw!.Id, SwapStatus.Approved, null, true, null, "mgr")); // not accepted yet
        Assert.Null(await svc.AcceptSwapAsync(sw.Id, b.Id, "mere"));
        Assert.NotNull(await svc.DecideSwapAsync(sw.Id, SwapStatus.Approved, null, false, b.Id, "mere")); // only manager approves
        Assert.Null(await svc.DecideSwapAsync(sw.Id, SwapStatus.Approved, "OK", true, null, "mgr"));
        var shifts = await svc.ShiftsAsync(Mon, Mon.AddDays(6), null);
        Assert.Equal(b.Id, shifts.Single(x => x.Id == sa.Id).StaffId);
        Assert.Equal(a.Id, shifts.Single(x => x.Id == sb.Id).StaffId);
    }

    [Fact]
    public async Task Payroll_timesheet_has_hours_per_day_and_skips_tentative()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<RosterService>();
        var (a, _) = await svc.SaveStaffAsync(new(null, "Jacqui", StaffRole.Nurse, null, null, 32, null, true, "E001", "Ordinary Hours"), "mgr");
        await svc.SaveShiftAsync(new(null, a!.Id, Mon, false, new(7, 30), new(16, 0), 30, "Theatre", false, null), "mgr");
        await svc.SaveShiftAsync(new(null, a.Id, Mon.AddDays(1), false, new(8, 0), new(12, 0), 0, "Theatre", true, null), "mgr");
        var (ts, detail, people, hours) = await svc.PayrollAsync(Mon, Mon.AddDays(13), "mgr");
        Assert.Equal(1, people);
        Assert.Equal(8m, hours);
        Assert.Contains("\"Jacqui\",\"E001\",\"Ordinary Hours\",\"8\"", ts);
        Assert.DoesNotContain("12:00", detail);
    }

    [Fact]
    public async Task Nbrs_extract_reports_public_patients_and_lists_problems()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        var noNhi = await h.PatientAsync("Brown", "Sam", nhi: null, dob: new DateOnly(1970, 1, 1));
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<ReferralService>();
        await svc.AddToWaitlistAsync(new(p.Id, "Lap chole", Laterality.NotApplicable, AnaestheticType.General, 90, ServiceType.Surgical, Doc.Id("Dr Ben Hall"), Funder.HealthNz, Priority.Urgent, Mon, null), "rec");
        await svc.AddToWaitlistAsync(new(noNhi.Id, "Hernia", Laterality.Left, AnaestheticType.General, 60, ServiceType.Surgical, Doc.Id("Dr Ben Hall"), Funder.HealthNz, Priority.Routine, Mon, null), "rec");
        await svc.AddToWaitlistAsync(new(p.Id, "Colonoscopy", Laterality.NotApplicable, AnaestheticType.Sedation, 30, ServiceType.Endoscopy, Doc.Id("Dr Ben Hall"), Funder.Insurer, Priority.Routine, Mon, null), "rec");
        var (_, problems0, rows0) = await svc.NbrsExtractAsync(Mon.AddDays(-30), Mon, "rec");
        Assert.Equal(0, rows0);
        Assert.Contains(problems0, x => x.Contains("agency and facility"));
        Assert.Empty(await svc.UpdateNbrsCodesAsync(new("NMDHB", "F04067", "S00", "M10"), "admin"));
        var (csv, problems, rows) = await svc.NbrsExtractAsync(Mon.AddDays(-30), Mon, "rec");
        Assert.Equal(1, rows);
        Assert.Contains("\"ZSC21TN\",\"19800517\",\"F\"", csv);
        Assert.Contains("\"1\"", csv); // urgent
        Assert.Contains(problems, x => x.Contains("no NHI"));
        Assert.DoesNotContain("Colonoscopy", csv);
    }

    [Fact]
    public async Task Credentialing_warns_on_expired_credentials_and_out_of_scope_procedures()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var org = s.ServiceProvider.GetRequiredService<OrganisationService>();
        var hall = await s.ServiceProvider.GetRequiredService<PasDbContext>().Clinicians.SingleAsync(c => c.Id == Doc.Id("Dr Ben Hall"));
        await org.AddExampleProceduresAsync("admin");
        var svc = s.ServiceProvider.GetRequiredService<CredentialingService>();
        Assert.Equal("Missing", (await svc.StatusAsync()).Single(c => c.Name == "Dr Ben Hall").State);
        Assert.NotEmpty(await svc.SaveCredentialAsync(new(null, hall.Id, CredentialType.Apc, "APC-1", "MCNZ", null, null, null, true), "admin")); // APC needs expiry
        await svc.SaveCredentialAsync(new(null, hall.Id, CredentialType.Apc, "APC-1", "MCNZ", Mon.AddMonths(-11), Mon.AddDays(20), null, true), "admin");
        await svc.SaveCredentialAsync(new(null, hall.Id, CredentialType.Indemnity, "MPS-9", "MPS", null, Mon.AddYears(1), null, true), "admin");
        await svc.SaveCredentialAsync(new(null, hall.Id, CredentialType.CredentialingApproval, "Min 2026/4", null, null, Mon.AddYears(3), null, true), "admin");
        await svc.SavePrivilegeAsync(new(null, hall.Id, "Endoscopy", null, Mon.AddYears(-1), null, null, true), "admin");
        Assert.Equal("ExpiringSoon", (await svc.StatusAsync()).Single(c => c.Name == "Dr Ben Hall").State);
        Assert.DoesNotContain(await svc.CheckCaseAsync(Mon.AddDays(5), hall.Id, null, "Colonoscopy"), w => w.Contains("practising"));
        var later = await svc.CheckCaseAsync(Mon.AddDays(30), hall.Id, null, "Laparoscopic cholecystectomy");
        Assert.Contains(later, w => w.Contains("practising certificate expires before"));
        Assert.Contains(later, w => w.Contains("outside"));
    }

    [Fact]
    public async Task Patient_portal_link_needs_date_of_birth_and_locks_after_five_attempts()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<PortalService>();
        var (token, _, _) = await svc.CreateLinkAsync(p.Id, "rec");
        Assert.NotNull(token);
        Assert.Equal(PortalOpenError.NotFound, (await svc.OpenAsync("nope", p.DateOfBirth, null)).Error);
        var ok = await svc.OpenAsync(token!, p.DateOfBirth, null);
        Assert.Equal(PortalOpenError.None, ok.Error);
        var (req, e) = await svc.AddRequestAsync(ok.Access!, p.Id, new(PortalRequestKind.Question, null, "Can I drive home?"));
        Assert.Null(e);
        for (var i = 0; i < 4; i++) Assert.Equal(PortalOpenError.WrongDetails, (await svc.OpenAsync(token!, new DateOnly(1999, 1, 1), null)).Error);
        Assert.Equal(PortalOpenError.Locked, (await svc.OpenAsync(token!, new DateOnly(1999, 1, 1), null)).Error);
        Assert.Equal(PortalOpenError.Locked, (await svc.OpenAsync(token!, p.DateOfBirth, null)).Error);
        // A new link revokes the old one.
        var (token2, _, _) = await svc.CreateLinkAsync(p.Id, "rec");
        Assert.Equal(PortalOpenError.NotFound, (await svc.OpenAsync(token!, p.DateOfBirth, null)).Error);
        Assert.Equal(PortalOpenError.None, (await svc.OpenAsync(token2!, p.DateOfBirth, null)).Error);
        Assert.Null(await svc.HandleRequestAsync(req!.Id, PortalRequestStatus.Done, "Yes, if someone drives you.", "rec"));
    }
}

public class DymoAndNormalisationTests
{
    [Fact]
    public void Code128_has_start_checksum_and_stop()
    {
        var (bars, width) = Code128.Encode("ZAA0024");
        // Start B + 7 data + checksum + stop = 9 × 11 + 13 modules, plus 2 × 10 quiet zone.
        Assert.Equal(9 * 11 + 13 + 20, width);
        Assert.Equal(10, bars[0][0]);
        Assert.Equal(2, bars[^1][1]); // final termination bar
    }

    [Fact]
    public async Task Dymo_printer_needs_label_stock_and_cannot_print_wristbands()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<LabelService>();
        Assert.Contains(await svc.SavePrinterAsync(new(null, "Desk DYMO", PrinterKind.Wristband, "", 0, 300, 0, 0, true, true, PrinterLanguage.Dymo, "99012"), "admin"), e => e.Contains("wristbands"));
        Assert.Empty(await svc.SavePrinterAsync(new(null, "Desk DYMO", PrinterKind.Label, "", 0, 300, 0, 0, true, true, PrinterLanguage.Dymo, "99012"), "admin"));
        var printer = (await svc.PrintersAsync(true)).Single();
        Assert.Equal((89, 36), (printer.WidthMm, printer.LengthMm));
        Assert.Contains("DYMO", await svc.PrintAsync(p.Id, PrinterKind.Label, 2, null, "rec"));
        var (error, sheet) = await svc.DymoSheetAsync(p.Id, 3, null, "rec");
        Assert.Null(error);
        Assert.NotNull(sheet);
    }

    [Fact]
    public async Task Questionnaire_answers_and_booking_checks_are_rows_not_lists()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var forms = s.ServiceProvider.GetRequiredService<BrightPAS.Modules.Forms.FormsDbContext>();
        var theatre = s.ServiceProvider.GetRequiredService<TheatreDbContext>();
        Assert.NotNull(forms.Model.FindEntityType(typeof(BrightPAS.Modules.Forms.FormAnswer)));
        Assert.NotNull(theatre.Model.FindEntityType(typeof(BookingCheck)));
        var cred = s.ServiceProvider.GetRequiredService<CredentialingService>();
        Assert.Equal([CredentialType.Apc, CredentialType.Indemnity, CredentialType.CredentialingApproval], await cred.RequiredAsync());
    }
}

public class ClinicianReferenceTests
{
    [Fact]
    public async Task Lists_appointments_and_waitlist_store_clinician_ids_so_a_rename_shows_everywhere()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var sp = s.ServiceProvider;
        var theatre = sp.GetRequiredService<TheatreService>();
        var th1 = await sp.GetRequiredService<PasDbContext>().Locations.SingleAsync(l => l.Code == "TH1");
        var day = new DateOnly(2026, 11, 20);
        var (_, bad) = await theatre.CreateSessionAsync(new(th1.Id, day, new(8, 0), new(12, 0), SessionType.Surgical, Guid.NewGuid(), null, null), "rec");
        Assert.Contains("clinician register", bad);
        await theatre.CreateSessionAsync(new(th1.Id, day, new(8, 0), new(12, 0), SessionType.Surgical, Doc.Id("Dr Ben Hall"), Doc.Id("Dr Kate Ford"), null), "rec");
        await theatre.BookAppointmentAsync(new(p.Id, day, new(17, 0), 15, AppointmentKind.FollowUp, Doc.Id("Dr Kate Ford"), null, false, false, null, AcceptWarnings: true), "rec");
        await sp.GetRequiredService<ReferralService>().AddToWaitlistAsync(new(p.Id, "Hernia", Laterality.Left, AnaestheticType.General, 60, ServiceType.Surgical, Doc.Id("Dr Kate Ford"),
            Funder.SelfPay, Priority.Routine, null, null), "rec");
        var org = sp.GetRequiredService<OrganisationService>();
        var (_, errs) = await org.SaveClinicianAsync(new(Doc.Id("Dr Kate Ford"), "Dr Kate Ford-Smith", ClinicianType.Anaesthetist, null, null, null, null, null, null, true), "admin");
        Assert.Empty(errs);
        Assert.Equal("Dr Kate Ford-Smith", (await theatre.SessionsAsync(day, day)).Single().Anaesthetist);
        Assert.Equal("Dr Kate Ford-Smith", (await theatre.AppointmentsAsync(day, day, null)).Single().Clinician);
        Assert.Equal("Dr Kate Ford-Smith", (await sp.GetRequiredService<ReferralService>().WaitlistAsync("waiting", Doc.Id("Dr Kate Ford"), null, null)).Single().Specialist);
    }
}

public class AuditLengthTests
{
    [Fact]
    public async Task Long_audit_reasons_are_shortened_not_lost()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var e = await s.ServiceProvider.GetRequiredService<BrightPAS.Core.Audit.AuditWriter>().WriteAsync("rec", "Test", "Thing", "1", reason: new string('x', 900));
        Assert.Equal(500, e.Reason!.Length);
        Assert.EndsWith("…", e.Reason);
    }
}
