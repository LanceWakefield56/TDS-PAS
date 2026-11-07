using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Data;
using BrightPAS.Modules.Referrals;
using BrightPAS.Modules.Theatre;

namespace BrightPAS.Modules.Tests;

public class ReferralsTests
{
    // ModulesHost clock: Mon 9 Nov 2026 09:00 NZ.
    private static readonly DateOnly Today = new(2026, 11, 9);

    private static NewReferral Referral(Guid patientId, Funder funder = Funder.Insurer) =>
        new(patientId, Today.AddDays(-3), ReferralSource.GeneralPractice, "Dr Mere Smith", "Stoke Medical Centre", "12ABC345", "Dr Ben Hall", ServiceType.Surgical,
            funder, "SX-1234", "Symptomatic gallstones, three episodes of biliary colic.");

    private static TriageDecision Accept(Priority p = Priority.SemiUrgent, DateOnly? decided = null) =>
        new(true, "Suitable for day surgery.", null, "Laparoscopic cholecystectomy", Laterality.NotApplicable, AnaestheticType.General, 90, p, decided ?? Today);

    [Fact]
    public async Task Referral_is_numbered_validated_and_audited()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<ReferralService>();

        var (bad, errors) = await svc.CreateReferralAsync(Referral(p.Id) with { ReferrerName = " ", Reason = "", ReceivedOn = Today.AddDays(1), ReferrerHpi = "nonsense!" }, "rec");
        Assert.Null(bad);
        Assert.Equal(4, errors.Count);

        var (r1, _) = await svc.CreateReferralAsync(Referral(p.Id), "rec");
        var (r2, _) = await svc.CreateReferralAsync(Referral(p.Id), "rec");
        Assert.Equal("R000001", r1!.Number);
        Assert.Equal("R000002", r2!.Number);
        Assert.Equal(2, await svc.ToTriageCountAsync());
        var rows = await svc.ReferralsAsync("to-triage", null);
        Assert.Equal(3, rows[0].DaysSinceReceived);
        Assert.StartsWith("NGĀTAI", rows[0].PatientName);
        Assert.True(await s.ServiceProvider.GetRequiredService<PasDbContext>().AuditEvents.AnyAsync(a => a.EntityType == "Referral" && a.Action == "Create"));
    }

    [Fact]
    public async Task Accepting_puts_patient_on_waitlist_with_target_date_and_declining_needs_a_reason()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<ReferralService>();
        var (r, _) = await svc.CreateReferralAsync(Referral(p.Id, Funder.HealthNz), "rec");

        var (entry, errors) = await svc.TriageAsync(r!.Id, Accept(Priority.SemiUrgent, Today.AddDays(-100)), "dr.hall");
        Assert.Empty(errors);
        Assert.Equal(Today.AddDays(-10), entry!.TargetDate); // 90 days after decision
        var row = (await svc.WaitlistAsync("waiting", null, null, null)).Single();
        Assert.True(row.Overdue);
        Assert.Equal(100, row.DaysWaiting);
        Assert.True(row.PubliclyFunded);
        Assert.Equal("R000001", row.ReferralNumber);

        var (_, again) = await svc.TriageAsync(r.Id, Accept(), "dr.hall");
        Assert.Contains("already", again[0]);

        var (r2, _) = await svc.CreateReferralAsync(Referral(p.Id), "rec");
        var (_, declineErrors) = await svc.TriageAsync(r2!.Id, new TriageDecision(false, null, " ", null, default, default, 0, default, null), "dr.hall");
        Assert.Contains("reason", declineErrors[0]);
        var (_, ok) = await svc.TriageAsync(r2.Id, new TriageDecision(false, null, "Better managed by the public service.", null, default, default, 0, default, null), "dr.hall");
        Assert.Empty(ok);
        Assert.Equal("Declined", (await svc.ReferralsAsync("all", p.Id)).Single(x => x.Id == r2.Id).Status);
    }

    [Fact]
    public async Task Duplicate_waitlist_entries_are_refused_and_priority_change_moves_target()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<ReferralService>();
        var n = new NewWaitlistEntry(p.Id, "Right inguinal hernia repair", Laterality.Right, AnaestheticType.General, 60, ServiceType.Surgical, "Dr Ben Hall", Funder.SelfPay, Priority.Routine, Today, null);
        var (e, _) = await svc.AddToWaitlistAsync(n, "rec");
        Assert.Equal(Today.AddDays(120), e!.TargetDate);
        var (_, dup) = await svc.AddToWaitlistAsync(n, "rec");
        Assert.Contains("already on the waitlist", dup[0]);
        Assert.Null(await svc.UpdateEntryAsync(e.Id, new WaitlistUpdate(e.Procedure, e.Laterality, e.Anaesthetic, 60, e.Specialist, Priority.Urgent, null), "rec"));
        Assert.Equal(Today.AddDays(30), (await svc.WaitlistAsync("waiting", null, null, null)).Single().TargetDate);
    }

    [Fact]
    public async Task Hold_resume_and_remove_rules()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<ReferralService>();
        var (e, _) = await svc.AddToWaitlistAsync(new(p.Id, "Colonoscopy", Laterality.NotApplicable, AnaestheticType.Sedation, 30, ServiceType.Endoscopy, "Dr Ana Lee", Funder.Acc, Priority.Urgent, null, null), "rec");
        Assert.Contains("why", await svc.HoldAsync(e!.Id, true, null, null, "rec"));
        Assert.Null(await svc.HoldAsync(e.Id, true, "Patient overseas", Today.AddDays(14), "rec"));
        Assert.Single(await svc.WaitlistAsync("on-hold", null, null, null));
        Assert.Contains("on hold", (await svc.BookAsync(e.Id, Guid.NewGuid(), false, "rec")).Errors[0]);

        h.Clock.Advance(TimeSpan.FromDays(15)); // hold end date passes
        Assert.Equal(1, await svc.SyncWithTheatreAsync());
        Assert.Single(await svc.WaitlistAsync("waiting", null, null, null));

        Assert.Contains("reason", await svc.RemoveAsync(e.Id, "", "rec"));
        Assert.Null(await svc.RemoveAsync(e.Id, "Treated elsewhere", "rec"));
        Assert.Empty(await svc.WaitlistAsync("waiting", null, null, null));
    }

    [Fact]
    public async Task Booking_from_waitlist_warns_then_books_and_follows_theatre_status()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<ReferralService>();
        var theatre = s.ServiceProvider.GetRequiredService<TheatreService>();
        var th1 = await s.ServiceProvider.GetRequiredService<PasDbContext>().Locations.SingleAsync(l => l.Code == "TH1");
        var (list, _) = await theatre.CreateSessionAsync(new(th1.Id, Today.AddDays(7), new(8, 0), new(12, 0), SessionType.Surgical, "Dr Kate Ford", null, null), "rec");
        var (r, _) = await svc.CreateReferralAsync(Referral(p.Id), "rec");
        var (e, _) = await svc.TriageAsync(r!.Id, Accept(), "dr.hall");

        var first = await svc.BookAsync(e!.Id, list!.Id, acceptWarnings: false, "rec");
        Assert.Null(first.Booking);
        Assert.Contains(first.Warnings, w => w.Contains("Dr Kate Ford"));

        var booked = await svc.BookAsync(e.Id, list.Id, acceptWarnings: true, "rec");
        Assert.NotNull(booked.Booking);
        var row = (await svc.WaitlistAsync("booked", null, null, null)).Single();
        Assert.Equal(Today.AddDays(7), row.BookedFor);
        Assert.Contains("Cancel the theatre booking first", await svc.RemoveAsync(e.Id, "x", "rec"));

        // Theatre cancels: patient goes back on the waitlist.
        Assert.Null(await theatre.SetStatusAsync(booked.Booking!.Id, BookingStatus.Cancelled, "Surgeon unwell", "rec"));
        Assert.Equal(1, await svc.SyncWithTheatreAsync());
        var back = (await svc.WaitlistAsync("waiting", null, null, null)).Single();
        Assert.Null(back.BookingId);
        Assert.Contains("cancelled", back.Notes);

        // Rebook and complete the day: entry closes.
        var again = await svc.BookAsync(e.Id, list.Id, true, "rec");
        foreach (var st in new[] { BookingStatus.Arrived, BookingStatus.InTheatre, BookingStatus.Recovery, BookingStatus.Discharged })
            Assert.Null(await theatre.SetStatusAsync(again.Booking!.Id, st, null, "rec"));
        await svc.SyncWithTheatreAsync();
        Assert.Equal("Completed", (await svc.WaitlistAsync("closed", null, null, null)).Single().Status);
    }

    [Fact]
    public async Task Csv_export_neutralises_spreadsheet_formulas_and_is_audited()
    {
        Assert.Equal("\"'=HYPERLINK(1)\"", ReferralService.Csv("=HYPERLINK(1)"));
        Assert.Equal("\"Say \"\"hi\"\"\"", ReferralService.Csv("Say \"hi\""));
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<ReferralService>();
        await svc.AddToWaitlistAsync(new(p.Id, "+cmd|' /C calc'!A0", Laterality.Left, AnaestheticType.Local, 20, ServiceType.Surgical, "Dr Ben Hall", Funder.SelfPay, Priority.Routine, null, null), "rec");
        var csv = await svc.ExportCsvAsync("all-open", "rec");
        Assert.Contains("\"'+cmd", csv);
        Assert.True(await s.ServiceProvider.GetRequiredService<PasDbContext>().AuditEvents.AnyAsync(a => a.Action == "Export" && a.EntityType == "Waitlist"));
    }

    [Fact]
    public async Task Target_settings_must_be_ordered()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<ReferralService>();
        Assert.NotEmpty(await svc.UpdateSettingsAsync(100, 30, 120, "admin"));
        Assert.Empty(await svc.UpdateSettingsAsync(14, 60, 120, "admin"));
        Assert.Equal(14, (await svc.SettingsAsync()).UrgentDays);
    }
}
