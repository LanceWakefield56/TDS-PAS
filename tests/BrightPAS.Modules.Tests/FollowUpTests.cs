using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Data;
using BrightPAS.Modules.FollowUp;
using BrightPAS.Modules.Reminders;
using BrightPAS.Modules.Theatre;

namespace BrightPAS.Modules.Tests;

/// <summary>0.24.0 Post-operative follow-up and SSI surveillance (spec 7.26, ADR 0037).</summary>
public class FollowUpTests
{
    private static readonly Dictionary<string, string> Fine = new()
    {
        ["pain"] = "2",
        ["pain_relief_working"] = "yes",
        ["nausea"] = "No",
        ["bleeding"] = "None",
        ["wound_red"] = "no",
        ["wound_swollen"] = "no",
        ["wound_discharge"] = "Nothing",
        ["wound_open"] = "no",
        ["fever"] = "no",
        ["eating"] = "yes",
        ["bowels_bladder"] = "yes",
        ["mobility"] = "yes",
        ["satisfaction"] = "9"
    };

    [Fact]
    public void Warning_rules_and_wound_assessment()
    {
        Assert.Empty(FollowUpScript.Warnings(Fine));
        Assert.False(FollowUpScript.Wound(Fine).Suspected);
        var bad = new Dictionary<string, string>(Fine) { ["pain"] = "8", ["bleeding"] = "Soaking the dressing", ["fever"] = "yes" };
        var w = FollowUpScript.Warnings(bad);
        Assert.Contains(w, x => x.StartsWith("Pain 8", StringComparison.Ordinal));
        Assert.Contains(w, x => x.Contains("Soaking", StringComparison.Ordinal));
        Assert.Contains(w, x => x.Contains("Fever", StringComparison.Ordinal));
        // CDC: purulent drainage alone suggests an SSI; redness alone does not.
        Assert.True(FollowUpScript.Wound(new Dictionary<string, string>(Fine) { ["wound_discharge"] = "Cloudy, yellow/green or smelly" }).Suspected);
        Assert.False(FollowUpScript.Wound(new Dictionary<string, string>(Fine) { ["wound_red"] = "yes" }).Suspected);
        Assert.True(FollowUpScript.Wound(new Dictionary<string, string>(Fine) { ["wound_red"] = "yes", ["wound_swollen"] = "yes", ["fever"] = "yes" }).Suspected);
        Assert.True(FollowUpScript.Wound(new Dictionary<string, string>(Fine) { ["antibiotics"] = "yes" }).Suspected);
        // Unknown keys are dropped; long answers clipped.
        var clean = FollowUpScript.Clean(new Dictionary<string, string> { ["pain"] = " 3 ", ["evil"] = "x", ["concerns"] = new string('a', 2000) });
        Assert.False(clean.ContainsKey("evil"));
        Assert.Equal("3", clean["pain"]);
        Assert.Equal(1000, clean["concerns"].Length);
    }

    [Fact]
    public void Due_dates_use_NZ_working_days_and_implant_surveillance()
    {
        var call = new PlanStep { Kind = ContactKind.Call, Days = 1, WorkingDays = true };
        // Friday 18 Dec 2026 → next working day Monday 21 Dec.
        Assert.Equal(new DateOnly(2026, 12, 21), FollowUpScript.DueOn(new DateOnly(2026, 12, 18), call, false));
        // Thursday 24 Dec → Christmas (Fri), weekend, Boxing Day observed Monday 28: Tuesday 29 Dec.
        Assert.Equal(new DateOnly(2026, 12, 29), FollowUpScript.DueOn(new DateOnly(2026, 12, 24), call, false));
        var ssi = new PlanStep { Kind = ContactKind.SsiCheck, Days = 30, DaysWithImplant = 90 };
        Assert.Equal(new DateOnly(2026, 12, 9), FollowUpScript.DueOn(new DateOnly(2026, 11, 9), ssi, false));
        Assert.Equal(new DateOnly(2027, 2, 7), FollowUpScript.DueOn(new DateOnly(2026, 11, 9), ssi, true));
        // A calendar-day call landing on a weekend moves to Monday; leap day handled.
        // Nelson Anniversary Day (Monday nearest 1 Feb) is closed: 2027-02-01 is a Monday.
        Assert.Equal(new DateOnly(2027, 2, 1), FollowUpScript.NelsonAnniversary(2027));
        Assert.Equal(new DateOnly(2026, 2, 2), FollowUpScript.NelsonAnniversary(2026));
        Assert.Equal(new DateOnly(2027, 2, 2), FollowUpScript.DueOn(new DateOnly(2027, 1, 29), call, false));
        var day5 = new PlanStep { Kind = ContactKind.WoundCheck, Days = 5 };
        Assert.Equal(new DateOnly(2028, 3, 1), FollowUpScript.DueOn(new DateOnly(2028, 2, 25), day5, false));
    }

    [Fact]
    public void Plan_is_chosen_by_procedure_words_then_default()
    {
        var plans = new List<Plan> { FollowUpScript.ExamplePlan("t", DateTimeOffset.UnixEpoch), FollowUpScript.EndoscopyPlan("t", DateTimeOffset.UnixEpoch) };
        Assert.Equal("Endoscopy", FollowUpService.PlanFor(plans, "Colonoscopy + polypectomy")!.Name);
        Assert.Equal("Standard day surgery", FollowUpService.PlanFor(plans, "Inguinal hernia repair")!.Name);
        plans[0].IsActive = false;
        Assert.Null(FollowUpService.PlanFor(plans, "Inguinal hernia repair"));
    }

    private static async Task<(Guid PatientId, Guid BookingId, DateOnly Date)> DischargedAsync(ModulesHost h, IServiceProvider sp, string procedure = "Inguinal hernia repair", int daysAgo = 1)
    {
        var p = await h.PatientAsync();
        var th = sp.GetRequiredService<TheatreService>();
        var loc = await sp.GetRequiredService<PasDbContext>().Locations.SingleAsync(l => l.Code == "TH1");
        var date = new DateOnly(2026, 11, 10); // Tuesday; the clock is moved on after discharge
        var (session, err) = await th.CreateSessionAsync(new(loc.Id, date, new(8, 0), new(12, 0), SessionType.Surgical, Doc.Id("Dr Ben Hall"), null, null), "rec");
        Assert.Null(err);
        var b = (await th.BookAsync(new(session!.Id, p.Id, procedure, Laterality.NotApplicable, 60, AnaestheticType.General, null, false, PaymentType.SelfFunded), "rec")).Booking!;
        foreach (var s in new[] { BookingStatus.Arrived, BookingStatus.InTheatre, BookingStatus.Recovery, BookingStatus.Discharged })
            Assert.Null(await th.SetStatusAsync(b.Id, s, null, "nurse"));
        h.Clock.Advance(TimeSpan.FromDays(daysAgo + 1)); // today = operation day + daysAgo
        return (p.Id, b.Id, date);
    }

    [Fact]
    public async Task Contacts_are_made_at_discharge_calls_recorded_unreachable_after_attempts_and_escalations_tracked()
    {
        await using var h = new ModulesHost();
        await using var scope = h.Scope();
        var sp = scope.ServiceProvider;
        var fu = sp.GetRequiredService<FollowUpService>();
        Assert.Equal(2, await fu.SeedExamplesAsync("quality"));
        Assert.Equal(0, await fu.SeedExamplesAsync("quality"));
        var (pid, bid, date) = await DischargedAsync(h, sp);
        // No public address: contacts are made but no links are sent.
        var (created, linked, _, newBookings) = await fu.ScanAsync();
        Assert.Equal(1, created); Assert.Equal(0, linked); Assert.Equal([bid], newBookings);
        Assert.Equal(0, (await fu.ScanAsync()).Created);
        var contacts = await sp.GetRequiredService<FollowUpDbContext>().Contacts.Where(c => c.BookingId == bid).OrderBy(c => c.DueOn).ToListAsync();
        Assert.Equal(3, contacts.Count); // high-risk extra call not added
        Assert.Equal(new DateOnly(2026, 11, 11), contacts[0].DueOn); // next working day
        Assert.Contains(contacts, c => c.Kind == ContactKind.SsiCheck && c.DueOn == date.AddDays(30));

        var call = contacts[0];
        Assert.Single(await fu.WorklistAsync("today"));
        // No answer twice, then unreachable on the third.
        Assert.Empty((await fu.RecordAsync(call.Id, new(ContactStatus.NoAnswer, "Phone", null, Escalation.None, null, null), "nurse")).Errors);
        Assert.Empty((await fu.RecordAsync(call.Id, new(ContactStatus.NoAnswer, "Phone", null, Escalation.None, null, null), "nurse")).Errors);
        var third = await fu.RecordAsync(call.Id, new(ContactStatus.NoAnswer, "Phone", null, Escalation.None, null, null), "nurse");
        Assert.Contains(third.Warnings, w => w.Contains("unreachable", StringComparison.Ordinal));
        Assert.Single(await fu.WorklistAsync("unreachable"));

        // A concern escalated to the GP needs a note, shows as an open escalation, and is closed with an outcome.
        var wound = contacts[1];
        Assert.NotEmpty((await fu.RecordAsync(wound.Id, new(ContactStatus.Done, "Phone", Fine, Escalation.GpReview, null, null), "nurse")).Errors);
        var r = await fu.RecordAsync(wound.Id, new(ContactStatus.Done, "Phone", new Dictionary<string, string>(Fine) { ["wound_discharge"] = "Cloudy, yellow/green or smelly", ["fever"] = "yes" }, Escalation.GpReview, "GP seeing today", null), "nurse");
        Assert.Empty(r.Errors);
        Assert.True(r.SuspectedSsi);
        Assert.Single(await fu.WorklistAsync("escalations"));
        Assert.NotNull(await fu.CloseEscalationAsync(wound.Id, "", "nurse"));
        Assert.Null(await fu.CloseEscalationAsync(wound.Id, "GP started antibiotics", "nurse"));
        Assert.Empty(await fu.WorklistAsync("escalations"));
        Assert.NotEmpty((await fu.RecordAsync(wound.Id, new(ContactStatus.Done, "Phone", Fine, Escalation.None, null, null), "nurse")).Errors); // already done

        // SSI: validation, number, confirmation by the lead with evidence, incident.
        var ssi = sp.GetRequiredService<SsiService>();
        Assert.NotEmpty((await ssi.OpenAsync(new(bid, wound.Id, date.AddDays(-1), "pus", SsiSource.Patient, true, false, false, null), "nurse")).Errors);
        var (c, errors) = await ssi.OpenAsync(new(bid, wound.Id, date.AddDays(1), r.Signs, SsiSource.Patient, true, false, false, null), "nurse");
        Assert.Empty(errors);
        Assert.Equal("SSI-2026-001", c!.Number);
        Assert.NotEmpty((await ssi.OpenAsync(new(bid, null, date.AddDays(1), "again", SsiSource.Gp, false, false, false, null), "nurse")).Errors);
        Assert.NotEmpty((await ssi.RaiseIncidentAsync(c.Id, "quality")).Errors); // not confirmed yet
        Assert.NotNull(await ssi.ConfirmAsync(c.Id, SsiClass.Deep, null, "quality"));
        Assert.Null(await ssi.ConfirmAsync(c.Id, SsiClass.Superficial, null, "quality"));
        var (iid, ierr) = await ssi.RaiseIncidentAsync(c.Id, "quality");
        Assert.Empty(ierr); Assert.NotNull(iid);
        var csv = System.Text.Encoding.UTF8.GetString(await ssi.ExportCsvAsync(date.AddDays(-1), date.AddDays(1)));
        Assert.Contains("Superficial", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("Ngātai", csv, StringComparison.Ordinal); // no names in the export

        var report = await fu.ReportAsync(date.AddDays(-1), date.AddDays(1));
        Assert.Equal(1, report.SsiConfirmed);
        Assert.Equal(1, report.Escalations);
        Assert.Contains(report.Rates, x => x.Group == "Procedure" && x.Ssi == 1 && x.Percent == 100);
    }

    [Fact]
    public async Task Patient_check_in_link_needs_dob_locks_after_5_and_a_concern_creates_a_call_back_and_alerts_the_duty_nurse()
    {
        await using var h = new ModulesHost();
        await using var scope = h.Scope();
        var sp = scope.ServiceProvider;
        var fu = sp.GetRequiredService<FollowUpService>();
        await fu.SeedExamplesAsync("quality");
        Assert.NotEmpty(await fu.SaveSettingsAsync(new FollowUpSettings { PublicUrl = "http://insecure.example", LinkDays = 5 }, "quality"));
        Assert.Empty(await fu.SaveSettingsAsync(new FollowUpSettings { PublicUrl = "https://pas.example.nz/", DutyMobile = "021 555 0100", SendCheckInLinks = true, LinkDays = 5 }, "quality"));
        var (pid, bid, date) = await DischargedAsync(h, sp, daysAgo: 6);
        var pdb = sp.GetRequiredService<BrightPAS.Modules.Patients.PatientsDbContext>();
        (await pdb.Patients.SingleAsync(p => p.Id == pid)).MobilePhone = "021 555 0123";
        await pdb.SaveChangesAsync();
        await fu.ScanAsync();
        var db = sp.GetRequiredService<FollowUpDbContext>();
        var form = await db.Contacts.SingleAsync(c => c.BookingId == bid && c.Method == ContactMethod.PatientForm && c.Kind == ContactKind.WoundCheck);
        Assert.NotNull(form.LinkSentAt);
        // The SMS holds the link; the database only the hash.
        var msg = await sp.GetRequiredService<RemindersDbContext>().Messages.SingleAsync(m => m.Kind == MessageKind.FollowUpCheckIn && m.Channel == Channel.Sms);
        Assert.Contains("[check-in link]", msg.Body, StringComparison.Ordinal);
        var token = msg.SecretLink!.Split("/c/")[1];
        Assert.Equal(FollowUpService.Hash(token), form.TokenHash);

        var dob = (await sp.GetRequiredService<BrightPAS.Modules.Patients.PatientsDbContext>().Patients.SingleAsync(p => p.Id == pid)).DateOfBirth;
        Assert.Equal("open", (await fu.PublicInfoAsync(token))!.Status);
        for (var i = 0; i < 5; i++) Assert.False((await fu.VerifyAsync(token, dob.AddDays(1))).Ok);
        Assert.Contains("Too many", (await fu.VerifyAsync(token, dob)).Error, StringComparison.Ordinal);
        h.Clock.Advance(TimeSpan.FromMinutes(31));
        Assert.True((await fu.VerifyAsync(token, dob)).Ok);
        var (ok, errs, concern) = await fu.SubmitAsync(token, dob, new Dictionary<string, string>(Fine) { ["bleeding"] = "Heavy or won't stop" });
        Assert.True(ok, string.Join(";", errs));
        Assert.True(concern);
        Assert.Equal("submitted", (await fu.PublicInfoAsync(token))?.Status ?? "submitted");
        Assert.False((await fu.VerifyAsync(token, dob)).Ok); // used
        var callback = (await db.Contacts.Where(c => c.BookingId == bid).ToListAsync()).Single(c => c.Label.StartsWith("Call back", StringComparison.Ordinal));
        Assert.True(callback.Concern);
        Assert.Contains(await fu.WorklistAsync("concerns"), x => x.Id == callback.Id);
        var alert = await sp.GetRequiredService<RemindersDbContext>().Messages.SingleAsync(m => m.Address == "+64215550100");
        Assert.DoesNotContain("Ngātai", alert.Body, StringComparison.Ordinal); // no patient details in the staff SMS
        _ = date;
    }
}
