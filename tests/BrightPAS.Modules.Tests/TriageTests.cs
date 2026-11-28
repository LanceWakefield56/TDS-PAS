using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core;
using BrightPAS.Core.Data;
using BrightPAS.Modules.Forms;
using BrightPAS.Modules.Theatre;
using BrightPAS.Modules.Triage;

namespace BrightPAS.Modules.Tests;

/// <summary>0.22.0 Patient triage and suitability (spec 7.25, ADR 0035).</summary>
public class TriageTests
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
            ["confirm"] = "Yes",
            ["snoring_loud"] = "No",
            ["daytime_tired"] = "No",
            ["observed_apnoea"] = "No",
            ["stairs"] = "Yes",
            ["recent_cardiac_stroke"] = "No",
            ["home_oxygen"] = "No",
            ["glp1"] = "No",
            ["medications"] = "None",
        };
        change?.Invoke(a);
        return a;
    }

    private static TriageFacts Facts(Action<Dictionary<string, string>>? change = null, int age = 45, string sex = "Female")
    {
        var a = Answers(change);
        a["age"] = age.ToString(System.Globalization.CultureInfo.InvariantCulture);
        a["sex"] = sex;
        a["questionnaire"] = "Yes";
        return TriageFacts.From(a);
    }

    private static RuleSet Rules() => TriageScoring.ExampleRuleSet(1, "test", DateTimeOffset.UnixEpoch);

    [Fact]
    public void Scores_bmi_stop_bang_asa_vte_cardiac_risk()
    {
        var healthy = TriageScoring.Calculate(Facts(), Rules(), "Colonoscopy", "Sedation", 30);
        Assert.Equal(25.7m, healthy.Bmi);
        Assert.Equal(0, healthy.StopBang);
        Assert.Equal(7, healthy.StopBangKnown); // neck size unknown
        Assert.Contains(healthy.Missing, m => m.Contains("Neck over 40 cm", StringComparison.Ordinal));
        Assert.Equal(1, healthy.SuggestedAsa);
        Assert.Equal(TriageOutcome.Suitable, healthy.Suggested);
        Assert.Equal(3, healthy.Vte); // age 41–60 +1, BMI > 25 +1, minor surgery +1

        // Obese, snoring, tired, hypertensive older man with a big neck: STOP-BANG 8, ASA 3 (BMI 40+), anaesthetist review.
        var osa = TriageScoring.Calculate(Facts(a => { a["weight_kg"] = "115"; a["height_cm"] = "168"; a["snoring_loud"] = "Yes"; a["daytime_tired"] = "Yes"; a["observed_apnoea"] = "Yes"; a["high_bp"] = "Yes"; a["neck_cm"] = "45"; }, 62, "Male"),
            Rules(), "Inguinal hernia repair", "General", 60);
        Assert.Equal(8, osa.StopBang);
        Assert.Equal(3, osa.SuggestedAsa);
        Assert.Equal(TriageOutcome.AnaesthetistReview, osa.Suggested);
        Assert.Contains(osa.Findings, f => f.Source == "OSA-HI");
        Assert.Contains(osa.Findings, f => f.Source == "BMI-40" && f.Precaution == "Bariatric table and equipment");
        Assert.Equal(5, osa.Vte); // age 61–74 +2, BMI over 25 +1, surgery over 45 minutes +2
        Assert.Equal(TriageOutcome.AnaesthetistReview, osa.Findings.Max(f => f.Outcome));

        // Recent heart attack: ASA 4 suggested, not suitable.
        var mi = TriageScoring.Calculate(Facts(a => { a["recent_cardiac_stroke"] = "Yes"; a["heart"] = "Yes"; }, 70), Rules(), "Cystoscopy", "General", 20);
        Assert.Equal(4, mi.SuggestedAsa);
        Assert.Equal(TriageOutcome.NotSuitable, mi.Suggested);
        Assert.Equal(1, mi.Rcri);

        // Insulin + creatinine 200 + stroke: cardiac risk 3; insulin medicine prompt and precaution.
        var dm = TriageScoring.Calculate(Facts(a => { a["diabetes"] = "Yes – insulin"; a["stroke"] = "Yes"; a["creatinine"] = "200"; a["medications"] = "Lantus 20 units nightly, metformin 1 g bd"; }), Rules(), "Gastroscopy", "Sedation", 15);
        Assert.Equal(3, dm.Rcri);
        Assert.Contains(dm.Findings, f => f.Source == "MED:Insulin");
        Assert.Contains(dm.Findings, f => f.Source == "MED:Metformin");
        Assert.Contains(dm.Precautions, p => p.Contains("diabetes plan", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Rules_respect_scope_medicines_missing_information_and_confirmed_asa()
    {
        // STOP-BANG rule only applies to a general anaesthetic.
        var f = Facts(a => { a["snoring_loud"] = "Yes"; a["daytime_tired"] = "Yes"; a["observed_apnoea"] = "Yes"; a["high_bp"] = "Yes"; a["neck_cm"] = "42"; }, 55, "Male");
        Assert.Contains(TriageScoring.Calculate(f, Rules(), "Hernia", "General", 60).Findings, x => x.Source == "OSA-HI");
        Assert.DoesNotContain(TriageScoring.Calculate(f, Rules(), "Colonoscopy", "Sedation", 30).Findings, x => x.Source == "OSA-HI");

        // GLP-1 from the yes/no question even without a drug name; DOAC from the blood thinner details.
        var g = TriageScoring.Calculate(Facts(a => { a["glp1"] = "Yes"; a["blood_thinners"] = "Yes"; a["blood_thinners_details"] = "Apixaban 5 mg bd"; }), Rules(), "Colonoscopy", "Sedation", 30);
        Assert.Contains(g.Findings, x => x.Source == "MED:GLP-1 agonist" && x.Outcome == TriageOutcome.AnaesthetistReview);
        Assert.Contains(g.Findings, x => x.Source == "MED:DOAC");

        // No questionnaire: more information needed; no escort named: more information.
        var none = TriageScoring.Calculate(TriageFacts.From(new Dictionary<string, string> { ["age"] = "40", ["sex"] = "Female" }), Rules(), "Colonoscopy", "Sedation", 30);
        Assert.Equal(TriageOutcome.MoreInformation, none.Suggested);
        Assert.Contains(none.Missing, m => m == "Pre-admission questionnaire");
        var noEscort = TriageScoring.Calculate(Facts(a => a.Remove("support_person")), Rules(), "Colonoscopy", "Sedation", 30);
        Assert.Contains(noEscort.Findings, x => x.Source == "ESC");

        // Over 65 without a frailty score is listed as missing; a confirmed ASA 2 replaces a suggested 3.
        var older = TriageScoring.Calculate(Facts(a => a["heart"] = "Yes", 72), Rules(), "Colonoscopy", "Sedation", 30);
        Assert.Contains(older.Missing, m => m.StartsWith("Clinical frailty", StringComparison.Ordinal));
        Assert.Equal(3, older.SuggestedAsa);
        Assert.Contains(older.Findings, x => x.Source == "ASA-3");
        var confirmed = TriageScoring.Calculate(Facts(a => { a["heart"] = "Yes"; a["asa_confirmed"] = "2"; a["cfs"] = "3"; }, 72), Rules(), "Colonoscopy", "Sedation", 30);
        Assert.DoesNotContain(confirmed.Findings, x => x.Source == "ASA-3");
        Assert.Equal(3, confirmed.Frailty);

        // A precaution alone makes the outcome "suitable with precautions".
        var latex = TriageScoring.Calculate(Facts(a => a["latex"] = "Yes"), Rules(), "Colonoscopy", "Sedation", 30);
        Assert.Equal(TriageOutcome.SuitableWithPrecautions, latex.Suggested);
    }

    private static async Task<(Guid PatientId, Guid BookingId)> BookedPatientAsync(ModulesHost h, IServiceProvider sp, string procedure = "Inguinal hernia repair", AnaestheticType an = AnaestheticType.General, int daysAhead = 20)
    {
        var p = await h.PatientAsync();
        var th = sp.GetRequiredService<TheatreService>();
        var loc = await sp.GetRequiredService<PasDbContext>().Locations.SingleAsync(l => l.Code == "TH1");
        var (session, err) = await th.CreateSessionAsync(new(loc.Id, new DateOnly(2026, 11, 9).AddDays(daysAhead), new(8, 0), new(12, 0), SessionType.Surgical, Doc.Id("Dr Ben Hall"), null, null), "rec");
        Assert.Null(err);
        var b = (await th.BookAsync(new(session!.Id, p.Id, procedure, Laterality.NotApplicable, 60, an, null, false, PaymentType.SelfFunded), "rec")).Booking!;
        return (p.Id, b.Id);
    }

    private static async Task SubmitAsync(IServiceProvider sp, Guid patientId, Guid bookingId, Dictionary<string, string> answers)
    {
        var forms = sp.GetRequiredService<FormService>();
        var dob = (await sp.GetRequiredService<BrightPAS.Modules.Patients.PatientsDbContext>().Patients.SingleAsync(p => p.Id == patientId)).DateOfBirth;
        var (_, token, err) = await forms.CreateAsync(patientId, bookingId, "nurse");
        Assert.Null(err);
        Assert.True((await forms.VerifyAsync(token!, dob)).Ok);
        var (ok, errors) = await forms.SubmitAsync(token!, dob, answers, true);
        Assert.True(ok, string.Join("; ", errors));
    }

    [Fact]
    public async Task Without_approved_rules_triage_gives_scores_only_and_needs_clinical_notes()
    {
        await using var h = new ModulesHost();
        await using var scope = h.Scope();
        var sp = scope.ServiceProvider;
        var (pid, bid) = await BookedPatientAsync(h, sp);
        await SubmitAsync(sp, pid, bid, Answers());
        var svc = sp.GetRequiredService<TriageService>();
        Assert.Equal(1, await svc.ScanAsync());
        var a = (await svc.WorklistAsync("open")).Single();
        Assert.False(a.RulesApproved);
        Assert.Equal(TriageOutcome.MoreInformation, a.Suggested);
        Assert.NotNull(await svc.SignOffAsync(a.Id, new(TriageOutcome.Suitable, null, null, null, "Clinical assessment"), false, "nurse", "Nurse"));
        Assert.Null(await svc.SignOffAsync(a.Id, new(TriageOutcome.Suitable, null, null, "Fit and well; phoned patient.", "No approved rules yet – clinical assessment"), false, "nurse", "Nurse"));
    }

    [Fact]
    public async Task Triage_flow_rules_approval_sign_off_booking_hold_and_retriage()
    {
        await using var h = new ModulesHost();
        await using var scope = h.Scope();
        var sp = scope.ServiceProvider;
        var rules = sp.GetRequiredService<TriageRulesService>();
        var svc = sp.GetRequiredService<TriageService>();
        // The theatre service as the web app builds it, with the triage booking gate (other module tests run without the gate).
        var theatre = ActivatorUtilities.CreateInstance<TheatreService>(sp, new object[] { new BrightPAS.Core.Modules.IBookingConfirmGate[] { new Gate(svc) } });
        // The booking hold is off until TDS turns it on.
        var hold = await svc.SettingsAsync();
        Assert.False(hold.HoldBookingUntilSignedOff);
        hold.HoldBookingUntilSignedOff = true;
        Assert.Empty(await svc.SaveSettingsAsync(hold, "fgrant"));

        // Example rules are a draft until a clinician approves them with a note.
        var rs = (await rules.SeedExampleAsync("fgrant"))!;
        Assert.Null(await rules.SeedExampleAsync("fgrant"));
        Assert.Equal(RuleSetStatus.Draft, rs.Status);
        Assert.NotNull(await rules.ApproveAsync(rs.Id, null, "fgrant", "Anaesthetist"));
        Assert.Null(await rules.ApproveAsync(rs.Id, "Anaesthetic meeting 20 Nov 2026", "fgrant", "Anaesthetist"));
        Assert.NotEmpty(await rules.SaveDraftAsync(rs.Id, new("x", null, [], []), "fgrant")); // approved = read-only

        var (pid, bid) = await BookedPatientAsync(h, sp);
        // Booking can't be confirmed before triage.
        Assert.Contains("Triage has not been done", await theatre.SetStatusAsync(bid, BookingStatus.Confirmed, null, "rec") ?? "", StringComparison.Ordinal);

        // Pacemaker → anaesthetist review; a nurse can't sign it off as suitable.
        await SubmitAsync(sp, pid, bid, Answers(a => a["pacemaker"] = "Yes"));
        Assert.Equal(1, await svc.ScanAsync());
        var first = (await svc.WorklistAsync("review")).Single();
        Assert.Equal(TriageTrigger.Initial, first.Trigger);
        Assert.Equal(TriageOutcome.AnaesthetistReview, first.Suggested);
        Assert.Contains("waiting for sign-off", await theatre.SetStatusAsync(bid, BookingStatus.Confirmed, null, "rec") ?? "", StringComparison.Ordinal);
        Assert.Contains("anaesthetist", await svc.SignOffAsync(first.Id, new(TriageOutcome.SuitableWithPrecautions, null, null, null, "ok"), false, "nurse", "Nurse") ?? "", StringComparison.Ordinal);
        Assert.Contains("reason", await svc.SignOffAsync(first.Id, new(TriageOutcome.SuitableWithPrecautions, 3, null, null, null), true, "fgrant", "Anaesthetist") ?? "", StringComparison.Ordinal);
        Assert.Null(await svc.SignOffAsync(first.Id, new(TriageOutcome.SuitableWithPrecautions, 3, "Device check on admission", "Seen in clinic", "Pacing check booked"), true, "fgrant", "Anaesthetist"));
        Assert.Null(await theatre.SetStatusAsync(bid, BookingStatus.Confirmed, null, "rec"));
        // The questionnaire's nurse triage is kept in step.
        var form = await sp.GetRequiredService<FormsDbContext>().Requests.AsNoTracking().SingleAsync(r => r.PatientId == pid);
        Assert.Equal(FormStatus.Triaged, form.Status);
        Assert.Equal(TriageCategory.Amber, form.TriageCategory);

        // Nothing new → no re-triage.
        Assert.Equal(0, await svc.ScanAsync());
        // New answers (recent heart attack) → re-triage, worse, earlier one superseded.
        h.Clock.Advance(TimeSpan.FromDays(2));
        await SubmitAsync(sp, pid, bid, Answers(a => { a["pacemaker"] = "Yes"; a["recent_cardiac_stroke"] = "Yes"; a["recent_cardiac_stroke_details"] = "Heart attack, stent October 2026"; }));
        Assert.Equal(1, await svc.ScanAsync());
        var second = (await svc.WorklistAsync("open")).Single();
        Assert.Equal(TriageTrigger.NewAnswers, second.Trigger);
        Assert.True(second.Worse);
        Assert.Equal(TriageOutcome.NotSuitable, second.Suggested);
        var detail = await svc.GetAsync(second.Id, "nurse", null);
        Assert.Contains("Outcome Suitable with precautions → Not suitable", detail!.ChangeSummary, StringComparison.Ordinal);
        Assert.Equal(2, (await svc.HistoryAsync(pid)).Count);
        Assert.Equal(AssessmentStatus.Superseded, (await svc.HistoryAsync(pid)).Single(x => x.Id == first.Id).Status);

        // Staff findings re-run triage: a confirmed ASA and frailty score are kept on later re-triage.
        var third = await svc.RunAsync(pid, bid, TriageTrigger.Manual, "clinic", "fgrant", [new("cfs", "3", null), new("bp_sys", "185", "Repeated twice")]);
        Assert.Contains(third.Findings, f => f.Source == "BP");
        Assert.Contains("Repeated twice", third.Notes, StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RunAsync(pid, bid, TriageTrigger.Manual, null, "nurse", [new("cfs", "12", null)]));

        // Booking change (procedure) → re-triage.
        var b = await sp.GetRequiredService<TheatreDbContext>().Bookings.SingleAsync(x => x.Id == bid);
        b.Procedure = "Laparoscopic hernia repair";
        await sp.GetRequiredService<TheatreDbContext>().SaveChangesAsync();
        Assert.Equal(1, await svc.ScanAsync());
        Assert.Equal(TriageTrigger.BookingChanged, (await svc.WorklistAsync("open")).Single().Trigger);

        // A new draft copies the approved set; approving it retires the old one.
        var (draftId, copyErr) = await rules.CopyAsync(rs.Id, "fgrant");
        Assert.Null(copyErr);
        var draft = (await rules.GetAsync(draftId!.Value))!;
        var edits = draft.Rules.Select(r => new RuleEdit(r.Code, r.Name, r.Fact, r.Operator, r.Code == "BMI-45" ? 42 : r.Threshold, r.TextValue, r.ProcedureContains, r.Anaesthetic, r.Outcome, r.Precaution, r.Explanation, r.SortOrder, r.IsActive)).ToList();
        Assert.Empty(await rules.SaveDraftAsync(draftId.Value, new("TDS rules v2", null, edits, draft.Medicines.Select(m => new MedicineEdit(m.Name, m.Keywords, m.Instruction, m.Outcome, m.IsActive)).ToList()), "fgrant"));
        Assert.NotEmpty(await rules.SaveDraftAsync(draftId.Value, new("bad", null, [new("X", "x", "nonsense", RuleOperator.AtLeast, null, null, null, null, TriageOutcome.Suitable, null, null, 0, true)], []), "fgrant"));
        Assert.Null(await rules.ApproveAsync(draftId.Value, "Approved at anaesthetic meeting", "fgrant", "Anaesthetist"));
        Assert.Equal(RuleSetStatus.Retired, (await rules.GetAsync(rs.Id))!.Status);
        Assert.Equal(42, (await svc.ApprovedRulesAsync())!.Rules.Single(r => r.Code == "BMI-45").Threshold);
        Assert.NotNull(await rules.DeleteDraftAsync(rs.Id, "fgrant"));
    }

    [Fact]
    public async Task Stale_sign_off_and_hold_settings()
    {
        await using var h = new ModulesHost();
        await using var scope = h.Scope();
        var sp = scope.ServiceProvider;
        var rules = sp.GetRequiredService<TriageRulesService>();
        var svc = sp.GetRequiredService<TriageService>();
        var rs = (await rules.SeedExampleAsync("fgrant"))!;
        await rules.ApproveAsync(rs.Id, "ok", "fgrant", "Anaesthetist");
        var (pid, bid) = await BookedPatientAsync(h, sp, "Colonoscopy", AnaestheticType.Sedation, 150);
        await SubmitAsync(sp, pid, bid, Answers());
        await svc.ScanAsync();
        var a = (await svc.WorklistAsync("open")).Single();
        Assert.Equal(TriageOutcome.Suitable, a.Suggested);
        Assert.Null(await svc.SignOffAsync(a.Id, new(TriageOutcome.Suitable, null, null, null, null), false, "nurse", "Nurse"));
        Assert.Null(await svc.CanConfirmAsync(bid, pid, "Colonoscopy"));
        // Surgery is 150 days away – more than 90 days after sign-off → re-triage due.
        Assert.Equal(1, await svc.ScanAsync());
        Assert.Equal(TriageTrigger.Stale, (await svc.WorklistAsync("open")).Single().Trigger);
        // Hold only for some procedures.
        var s = await svc.SettingsAsync();
        s.HoldBookingUntilSignedOff = true;
        s.HoldProcedures = "hernia, laparoscop";
        Assert.Empty(await svc.SaveSettingsAsync(s, "fgrant"));
        Assert.Null(await svc.CanConfirmAsync(bid, pid, "Colonoscopy"));
        Assert.NotNull(await svc.CanConfirmAsync(bid, pid, "Laparoscopic cholecystectomy"));
        Assert.NotEmpty(await svc.SaveSettingsAsync(new TriageSettings { RetriageAfterDays = 1 }, "fgrant"));
    }

    private sealed class Gate(TriageService t) : BrightPAS.Core.Modules.IBookingConfirmGate
    {
        public Task<string?> CheckAsync(Guid bookingId, Guid patientId, string procedure, CancellationToken ct) => t.CanConfirmAsync(bookingId, patientId, procedure, ct);
    }

    // ------------------------------------------------------------------ 0.36.0 early warning score on the day, anaesthetist reviews
    [Fact]
    public void Early_warning_score_on_the_day_suggests_review_at_threshold_or_a_single_three()
    {
        TriageResult Run(string ews, string three) => TriageScoring.Calculate(Facts(a => { a["ews"] = ews; a["ews_three"] = three; a["ews_review_at"] = "5"; a["ews_at"] = "07:30"; }), Rules(), "Colonoscopy", "Sedation", 30);
        var high = Run("5", "No");
        Assert.Equal(TriageOutcome.AnaesthetistReview, high.Suggested);
        Assert.Contains(high.Findings, f => f.Source == "EWS:day" && f.Text.Contains("07:30", StringComparison.Ordinal));
        Assert.Equal(TriageOutcome.AnaesthetistReview, Run("3", "Yes").Suggested);
        var low = Run("2", "No");
        Assert.True(low.Suggested < TriageOutcome.AnaesthetistReview);
        Assert.Contains(low.Findings, f => f.Source == "EWS:day" && f.Precaution != null);
        Assert.Contains(low.Scores, x => x.Name == "Early warning score");
        Assert.DoesNotContain(TriageScoring.Calculate(Facts(), Rules(), "Colonoscopy", "Sedation", 30).Findings, f => f.Source == "EWS:day");
    }

    [Fact]
    public async Task Observations_on_the_day_retriage_when_switched_on_and_a_review_can_be_booked()
    {
        await using var h = new ModulesHost();
        await using var scope = h.Scope();
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<TriageService>();
        var (pid, bid) = await BookedPatientAsync(h, sp, "Colonoscopy", AnaestheticType.Sedation, 0);
        await SubmitAsync(sp, pid, bid, Answers());
        Assert.Equal(1, await svc.ScanAsync());
        var emr = sp.GetRequiredService<BrightPAS.Modules.Emr.EmrService>();
        var ep = await emr.EpisodeAsync(bid, pid, "nurse");
        h.Clock.Advance(TimeSpan.FromMinutes(10));
        var (obs, oerr) = await emr.ObserveAsync(ep.Id, new(h.Clock.GetUtcNow(), 22, 95, false, null, 105, 70, 112, 37.2m, "A", 0, 0), "nurse");
        Assert.Empty(oerr);
        Assert.True(obs!.Ews >= 5);
        // Off by default: nothing changes.
        Assert.Equal(0, await svc.ScanAsync());
        var st = await svc.SettingsAsync();
        st.EwsOnTheDay = true; st.EwsReviewAt = 5;
        st.VideoLinkTemplate = "http://meet.example.nz/{code}";
        Assert.NotEmpty(await svc.SaveSettingsAsync(st, "fgrant"));
        st.VideoLinkTemplate = "https://meet.example.nz/tds-{code}";
        Assert.Empty(await svc.SaveSettingsAsync(st, "fgrant"));
        Assert.Equal(1, await svc.ScanAsync());
        var a = (await svc.WorklistAsync("review")).Single();
        Assert.Equal(TriageTrigger.Observations, a.Trigger);
        Assert.Equal(TriageOutcome.AnaesthetistReview, a.Suggested);
        Assert.Equal(0, await svc.ScanAsync());

        // Book a video review with the anaesthetist before surgery.
        var rv = sp.GetRequiredService<ReviewService>();
        var kate = (await rv.AnaesthetistsAsync()).Single(c => c.Name == "Dr Kate Ford");
        var surgeon = Doc.Id("Dr Ben Hall");
        var now = h.Clock.GetUtcNow().ToNz().DateTime;
        Assert.NotEmpty((await rv.BookAsync(new(a.Id, surgeon, ReviewMode.Video, now.AddMinutes(30), null, null, null), "nurse")).Errors);
        Assert.NotEmpty((await rv.BookAsync(new(a.Id, kate.Id, ReviewMode.Video, now.AddMinutes(-5), null, null, null), "nurse")).Errors);
        Assert.NotEmpty((await rv.BookAsync(new(a.Id, kate.Id, ReviewMode.Video, now.AddDays(1), null, null, null), "nurse")).Errors); // after surgery
        var (r, errs) = await rv.BookAsync(new(a.Id, kate.Id, ReviewMode.Video, now.AddMinutes(30), null, null, "EWS 5 on admission"), "nurse");
        Assert.Empty(errs);
        Assert.StartsWith("https://meet.example.nz/tds-", r!.JoinLink, StringComparison.Ordinal);
        Assert.Equal(15, r.Minutes);
        Assert.NotEmpty((await rv.BookAsync(new(a.Id, kate.Id, ReviewMode.Phone, now.AddMinutes(35), null, "021 555 0100", null), "nurse")).Errors); // clash + already booked
        Assert.NotNull(await rv.FinishAsync(r.Id, new(ReviewStatus.Done, null), "kford"));
        Assert.Null(await rv.FinishAsync(r.Id, new(ReviewStatus.Done, "Reviewed by video: anxious, obs settling. Proceed with precautions."), "kford"));
        Assert.NotNull(await rv.FinishAsync(r.Id, new(ReviewStatus.Cancelled, "x"), "kford"));
        var list = await rv.ListAsync(null, 1, null, null);
        Assert.Equal(ReviewStatus.Done, list.Single().Status);
    }
}
