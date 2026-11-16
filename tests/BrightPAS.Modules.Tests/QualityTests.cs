using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Data;
using BrightPAS.Modules.Quality;

namespace BrightPAS.Modules.Tests;

/// <summary>0.19.0 Quality management: incidents (SAC, escalation, disclosure, closure rules), Right 10 working days, risks, corrective actions, policies, SPC.</summary>
public class QualityTests
{
    [Fact]
    public void Working_days_follow_the_Code_of_Rights_and_NZ_public_holidays()
    {
        // Public holidays 2026 (with Mondayisation).
        var h = WorkingDays.PublicHolidays(2026);
        Assert.Contains(new DateOnly(2026, 4, 3), h);  // Good Friday
        Assert.Contains(new DateOnly(2026, 4, 6), h);  // Easter Monday
        Assert.Contains(new DateOnly(2026, 4, 27), h); // ANZAC Day falls on Saturday → Monday
        Assert.Contains(new DateOnly(2026, 6, 1), h);  // King's Birthday
        Assert.Contains(new DateOnly(2026, 7, 10), h); // Matariki
        Assert.Contains(new DateOnly(2026, 10, 26), h); // Labour Day
        Assert.Contains(new DateOnly(2027, 2, 8), WorkingDays.PublicHolidays(2027)); // Waitangi Day on Saturday → Monday
        Assert.Equal(new DateOnly(2027, 3, 28), WorkingDays.EasterSunday(2027));
        // Right 10(6): 5 working days. Friday 20 Nov 2026 → Friday 27 Nov.
        Assert.Equal(new DateOnly(2026, 11, 27), WorkingDays.Add(new DateOnly(2026, 11, 20), 5));
        // 25 December – 15 January is not working days: Tue 22 Dec 2026 + 5 → Wed 20 Jan 2027.
        Assert.Equal(new DateOnly(2027, 1, 20), WorkingDays.Add(new DateOnly(2026, 12, 22), 5));
        // Extra non-working days from settings (e.g. Nelson Anniversary, Mon 1 Feb 2027).
        var extra = WorkingDays.ParseExtra("2027-02-01, nonsense");
        Assert.Single(extra);
        Assert.Equal(new DateOnly(2027, 2, 3), WorkingDays.Add(new DateOnly(2027, 1, 29), 2, extra));
    }

    [Fact]
    public async Task Incident_flow_escalates_serious_harm_and_cannot_close_until_everything_is_recorded()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var q = s.ServiceProvider.GetRequiredService<QualityService>();
        var p = await h.PatientAsync("Ngātai");
        var now = h.Clock.GetUtcNow();

        Assert.NotEmpty((await q.ReportAsync(new IncidentReport(now.AddHours(2), "Theatre 1", IncidentCategory.Fall, "Too short", null, null, null, null, null, Harm.None, false), "rn")).Errors);
        var (anon, _) = await q.ReportAsync(new IncidentReport(now.AddMinutes(-30), "Reception", IncidentCategory.InformationPrivacy, "Letter put in the wrong envelope; returned unopened.", null, null, null, null, null, Harm.None, true), "kiri");
        Assert.Null(anon!.ReportedBy); // anonymous: the reporter is not stored
        Assert.StartsWith("INC-2026-", anon.Number, StringComparison.Ordinal);
        var audits = await s.ServiceProvider.GetRequiredService<PasDbContext>().AuditEvents.Where(a => a.EntityId == anon.Id.ToString()).ToListAsync();
        Assert.All(audits, a => Assert.Equal("anonymous", a.UserId));

        var (i, errors) = await q.ReportAsync(new IncidentReport(now.AddHours(-1), "Recovery", IncidentCategory.Medication, "Second antibiotic dose given in recovery; rash treated.", "Antihistamine given.", p.Id, null, null, null, Harm.Moderate, false), "rn");
        Assert.Empty(errors);
        Assert.Null(i!.EscalatedAt);
        Assert.Contains("Severe harm is SAC 1 or 2.", await q.TriageAsync(i.Id, new IncidentTriage(3, Harm.Severe, false, null, []), "qm"));
        Assert.Empty(await q.TriageAsync(i.Id, new IncidentTriage(2, Harm.Moderate, false, "QM", [Agency.Hqsc]), "qm"));
        var t = await q.IncidentAsync(i.Id);
        Assert.NotNull(t!.EscalatedAt); // SAC 2 escalates
        Assert.Single(t.Notifications);

        // Closing needs findings, the notification, open disclosure (patient harmed) and no open actions.
        var close = await q.CloseIncidentAsync(i.Id, "qm");
        Assert.Contains(close, e => e.Contains("findings", StringComparison.Ordinal));
        Assert.Contains(close, e => e.Contains("Hqsc", StringComparison.Ordinal));
        Assert.Contains(close, e => e.Contains("open disclosure", StringComparison.Ordinal));
        Assert.Empty(await q.FindingsAsync(i.Id, new IncidentFindings("Dose not charted in theatre.", "Handover gap.", "Chart every theatre dose before leaving theatre."), "qm"));
        Assert.Empty(await q.NotifiedAsync(i.Id, Agency.Hqsc, "HQSC-123", "qm"));
        Assert.Empty(await q.DiscloseAsync(i.Id, new DisclosureEdit(now, "Patient", "In person", null), "qm"));
        var (a, ae) = await q.NewActionAsync(new ActionEdit(ActionSource.Incident, i.Id, null, "Chart theatre antibiotics at Sign Out", null, "Dr K", DateOnly.FromDateTime(now.UtcDateTime).AddDays(14)), "qm");
        Assert.Empty(ae);
        Assert.Equal("INC", a!.SourceRef![..3]);
        Assert.Equal(IncidentStatus.Actions, (await q.IncidentAsync(i.Id))!.Status);
        Assert.Contains(await q.CloseIncidentAsync(i.Id, "qm"), e => e.Contains("corrective actions", StringComparison.Ordinal));
        Assert.Empty(await q.ActionDoneAsync(a.Id, "Sign Out wording changed (checklist v4).", "rn"));
        Assert.Contains("Someone other than", (await q.ActionVerifyAsync(a.Id, "rn"))[0]);
        Assert.Empty(await q.ActionVerifyAsync(a.Id, "qm"));
        Assert.Empty(await q.CloseIncidentAsync(i.Id, "qm"));
        Assert.Contains("closed", (await q.TriageAsync(i.Id, new IncidentTriage(1, Harm.Death, true, null, []), "qm"))[0]);
    }

    [Fact]
    public async Task Complaint_deadlines_follow_Right_10_and_steps_go_in_order()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var q = s.ServiceProvider.GetRequiredService<QualityService>();
        var received = new DateOnly(2026, 11, 2); // Monday
        var (c, e) = await q.NewComplaintAsync(new ComplaintEdit(received, "Email", "Mr T", null, null, "Cancelled on the day.", "Waiting time", "QM", null), "qm");
        Assert.Empty(e);
        Assert.Equal(new DateOnly(2026, 11, 9), c!.AcknowledgeBy);
        Assert.Null(q.Overdue(c)); // the test clock is Mon 9 Nov – the due day itself is still on time
        Assert.Contains("Acknowledge the complaint first.", await q.ComplaintStepAsync(c.Id, new ComplaintStep("respond", received, "x", ComplaintOutcome.Upheld, null, null), "qm"));
        Assert.Empty(await q.ComplaintStepAsync(c.Id, new ComplaintStep("acknowledge", new DateOnly(2026, 11, 5), null, null, null, null), "qm"));
        var c2 = (await q.ComplaintsAsync(true)).Single();
        Assert.Equal(new DateOnly(2026, 11, 19), c2.DecideBy); // 10 working days after Thu 5 Nov
        Assert.Contains("reason", (await q.ComplaintStepAsync(c.Id, new ComplaintStep("extend", new DateOnly(2026, 11, 6), " ", null, null, null), "qm"))[0]);
        Assert.Contains("before closing", (await q.ComplaintStepAsync(c.Id, new ComplaintStep("close", new DateOnly(2026, 11, 6), null, null, null, null), "qm"))[0], StringComparison.Ordinal);
        Assert.Empty(await q.ComplaintStepAsync(c.Id, new ComplaintStep("respond", new DateOnly(2026, 11, 6), "Apologised and rebooked; HDC and Advocacy details given.", ComplaintOutcome.PartlyUpheld, false, false), "qm"));
        Assert.Empty(await q.ComplaintStepAsync(c.Id, new ComplaintStep("close", new DateOnly(2026, 11, 6), null, null, null, null), "qm"));
        Assert.Null(q.Overdue((await q.ComplaintsAsync(false)).Single()));
    }

    [Fact]
    public async Task Risks_policies_and_manual_indicators_validate_their_input()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var q = s.ServiceProvider.GetRequiredService<QualityService>();
        var today = DateOnly.FromDateTime(h.Clock.GetLocalNow().DateTime);
        Assert.Contains((await q.SaveRiskAsync(null, new RiskEdit(false, "Ransomware", RiskCategory.InformationTechnology, "", "GM", 3, 5, null, 4, 5, null, null, today.AddMonths(3), RiskStatus.Open), "qm")).Errors,
            e => e.Contains("residual", StringComparison.OrdinalIgnoreCase));
        var (r, re) = await q.SaveRiskAsync(null, new RiskEdit(false, "Ransomware", RiskCategory.InformationTechnology, "", "GM", 3, 5, "Backups", 2, 4, null, null, today.AddMonths(3), RiskStatus.Open), "qm");
        Assert.Empty(re);
        Assert.Equal("High", QualityService.Band(QualityService.Score(r!.Likelihood, r.Consequence)));
        Assert.StartsWith("RSK-", r.Number, StringComparison.Ordinal);
        Assert.StartsWith("HAZ-", (await q.SaveRiskAsync(null, new RiskEdit(true, "Hot surfaces", RiskCategory.HealthAndSafety, "", "CSSD", 2, 3, null, null, null, "CSSD", null, today.AddMonths(1), RiskStatus.Open), "qm")).Risk!.Number, StringComparison.Ordinal);

        var (p, pe) = await q.SavePolicyAsync(null, new PolicyEdit("tds-5.9", "Blood collection", "2.0", "DON", "https://sharepoint.example/tds-5.9", today.AddYears(1), true, true), "qm");
        Assert.Empty(pe);
        Assert.Equal("TDS-5.9", p!.Code);
        Assert.NotEmpty((await q.SavePolicyAsync(null, new PolicyEdit("TDS-5.9", "Again", "1", "x", "javascript:alert(1)", today, false, true), "qm")).Errors);
        Assert.Null(await q.AcknowledgeAsync(p.Id, "rn"));
        Assert.Null(await q.AcknowledgeAsync(p.Id, "rn")); // once per version
        Assert.Single(await q.AcknowledgementsAsync(p.Id));
        await q.SavePolicyAsync(p.Id, new PolicyEdit("TDS-5.9", "Blood collection", "2.1", "DON", null, today.AddYears(1), true, true), "qm");
        Assert.Null(await q.AcknowledgeAsync(p.Id, "rn")); // a new version needs a new acknowledgement
        Assert.Equal(2, (await q.AcknowledgementsAsync(p.Id)).Count);

        var ind = s.ServiceProvider.GetRequiredService<IndicatorService>();
        Assert.Equal(IndicatorService.Starter.Count, await ind.SeedStarterAsync());
        Assert.Equal(0, await ind.SeedStarterAsync());
        var psat = (await ind.ListAsync(false)).Single(i => i.Code == "PSAT");
        Assert.NotEmpty(await ind.EnterValueAsync(psat.Id, today, 120, 100, "qm")); // more than 100%
        Assert.Empty(await ind.EnterValueAsync(psat.Id, today.AddMonths(-1), 95, 100, "qm"));
        var dosc = (await ind.ListAsync(false)).Single(i => i.Code == "DOSC");
        Assert.NotEmpty(await ind.EnterValueAsync(dosc.Id, today, 1, 10, "qm")); // calculated – not entered by hand
        var series = await ind.SeriesAsync(psat.Id, 6);
        Assert.Equal(6, series.Points.Count);
        Assert.Equal(95m, series.Latest);
        Assert.Equal("green", series.Rag);
    }

    [Fact]
    public void Spc_limits_and_signals()
    {
        // p-chart: 2 cancellations in 100 each month, then one month of 12 in 100 → outside the upper limit.
        var months = Enumerable.Range(0, 12).Select(i => (new DateOnly(2026, 1, 1).AddMonths(i), i == 11 ? 12m : 2m, (decimal?)100m)).ToList();
        var (pts, centre, chart) = IndicatorService.Spc(months, 100);
        Assert.Equal("p", chart);
        Assert.Equal(2.83m, centre);
        Assert.True(pts[11].OutsideLimits);
        Assert.False(pts[0].OutsideLimits);
        Assert.Equal(0m, pts[0].Lcl);
        // Shift: 8 months in a row above the centre line.
        var shift = Enumerable.Range(0, 12).Select(i => (new DateOnly(2026, 1, 1).AddMonths(i), i < 4 ? 1m : 5m, (decimal?)100m)).ToList();
        Assert.True(IndicatorService.Spc(shift, 100).Points[11].Shift);
        // Trend: six rising points.
        var trend = Enumerable.Range(0, 6).Select(i => (new DateOnly(2026, 1, 1).AddMonths(i), (decimal)(i + 1), (decimal?)100m)).ToList();
        Assert.All(IndicatorService.Spc(trend, 100).Points, p => Assert.True(p.Trend));
        // c-chart for counts.
        Assert.Equal("c", IndicatorService.Spc([(new DateOnly(2026, 1, 1), 3m, null)], 1).Chart);
        Assert.Equal("Māori", IndicatorService.EquityGroup(["11", "21"]));
        Assert.Equal("Pacific", IndicatorService.EquityGroup(["33", "11"]));
        Assert.Equal("Not stated", IndicatorService.EquityGroup(["99"]));
    }
}
