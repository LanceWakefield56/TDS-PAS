using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Data;
using BrightPAS.Modules.Consent;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Reminders;
using BrightPAS.Modules.Theatre;

namespace BrightPAS.Modules.Tests;

/// <summary>0.29.0 e-consent: sheets (validation, approval by a second clinician, versions), prepare/send, the patient link (date of birth, lockout,
/// read every section, questions, sign), paper consent, confirm on the day (theatre check), withdraw, booking gate, merge.</summary>
public class ConsentTests
{
    private static readonly string Png = "data:image/png;base64," + Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0]);
    private static readonly string Pdf = Convert.ToBase64String("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n"u8.ToArray());

    private static async Task<Guid> PublishedAsync(IServiceProvider sp, string code = "LAPCHOLE")
    {
        var sheets = sp.GetRequiredService<ConsentSheetService>();
        await sheets.SeedExamplesAsync("drwriter");
        var s = (await sheets.ListAsync(false)).Single(x => x.Code == code);
        Assert.Null(await sheets.ApproveAsync(s.Id, "Reviewed", "drhall"));
        return s.Id;
    }

    private static async Task<(Guid PatientId, Guid BookingId)> BookingAsync(ModulesHost h, IServiceProvider sp, string procedure = "Laparoscopic cholecystectomy", DateOnly? date = null, DateOnly? dob = null)
    {
        var p = await h.PatientAsync(dob: dob);
        var pdb = sp.GetRequiredService<PatientsDbContext>();
        (await pdb.Patients.SingleAsync(x => x.Id == p.Id)).MobilePhone = "021 555 0123";
        await pdb.SaveChangesAsync();
        var th = sp.GetRequiredService<TheatreService>();
        var loc = await sp.GetRequiredService<PasDbContext>().Locations.SingleAsync(l => l.Code == "TH1");
        var (session, err) = await th.CreateSessionAsync(new(loc.Id, date ?? new DateOnly(2026, 11, 17), new(8, 0), new(12, 0), SessionType.Surgical, Doc.Id("Dr Ben Hall"), null, null), "rec");
        Assert.Null(err);
        var b = (await th.BookAsync(new(session!.Id, p.Id, procedure, Laterality.NotApplicable, 60, AnaestheticType.General, null, false, PaymentType.SelfFunded), "rec")).Booking!;
        return (p.Id, b.Id);
    }

    [Fact]
    public async Task Sheets_are_checked_approved_by_a_second_clinician_and_versioned()
    {
        await using var h = new ModulesHost();
        await using var scope = h.Scope();
        var svc = scope.ServiceProvider.GetRequiredService<ConsentSheetService>();
        var bad = await svc.SaveDraftAsync(null, new SheetEdit("bad code!", "", null, "", "", "", "", null, null, null, [], [new("Video", "http://example.com")]), "dr");
        Assert.Contains(bad.Errors, e => e.StartsWith("Enter a short code", StringComparison.Ordinal));
        Assert.Contains("Add at least one risk.", bad.Errors);
        Assert.Contains(bad.Errors, e => e.Contains("https://", StringComparison.Ordinal));

        Assert.Equal(3, await svc.SeedExamplesAsync("drwriter"));
        Assert.Equal(0, await svc.SeedExamplesAsync("drwriter"));
        var draft = (await svc.ListAsync(false)).Single(x => x.Code == "COLON");
        Assert.Equal(SheetStatus.Draft, draft.Status);
        Assert.Null(await svc.SuggestAsync("Colonoscopy and polypectomy")); // drafts are never used
        Assert.Contains("different clinician", await svc.ApproveAsync(draft.Id, null, "drwriter"), StringComparison.Ordinal);
        Assert.Null(await svc.ApproveAsync(draft.Id, null, "drhall"));
        Assert.Equal(draft.Id, (await svc.SuggestAsync("Colonoscopy and polypectomy"))!.Id);

        // Published sheets don't change: a new version is drafted, and approving it retires the old one.
        Assert.NotNull((await svc.SaveDraftAsync(draft.Id, ConsentExamples.Sheets[1], "drwriter")).Errors.SingleOrDefault());
        var (v2, e2) = await svc.NewVersionAsync(draft.Id, "drwriter");
        Assert.Null(e2);
        Assert.Equal(2, v2!.Version);
        Assert.NotNull((await svc.NewVersionAsync(draft.Id, "drwriter")).Error); // one draft at a time
        Assert.Null(await svc.ApproveAsync(v2.Id, null, "drhall"));
        Assert.Equal(SheetStatus.Retired, (await svc.GetAsync(draft.Id))!.Status);
        Assert.Equal(v2.Id, (await svc.SuggestAsync("colonoscopy"))!.Id);
        Assert.Equal(7, (await svc.GetAsync((await svc.ListAsync(false)).Single(x => x.Code == "LAPCHOLE").Id))!.Risks.Count);
    }

    [Fact]
    public async Task Patient_reads_asks_and_signs_through_the_link_then_it_is_confirmed_on_the_day()
    {
        await using var h = new ModulesHost();
        await using var scope = h.Scope();
        var sp = scope.ServiceProvider;
        var sheetId = await PublishedAsync(sp);
        var (pid, bid) = await BookingAsync(h, sp);
        var svc = sp.GetRequiredService<ConsentService>();

        var todo = await svc.ToPrepareAsync();
        Assert.Equal(sheetId, Assert.Single(todo).SuggestedSheetId);
        var (c, errors) = await svc.PrepareAsync(bid, sheetId, "Higher risk of bleeding because you take warfarin", null, "nurse");
        Assert.Empty(errors);
        Assert.Equal("CN000001", c!.Number);
        Assert.Equal("Dr Ben Hall", c.ClinicianName);
        Assert.Empty(await svc.ToPrepareAsync());
        Assert.NotEmpty((await svc.PrepareAsync(bid, sheetId, null, null, "nurse")).Errors); // one per booking

        Assert.Contains("public web address", (await svc.SendAsync(c.Id, "nurse")).Error, StringComparison.Ordinal);
        Assert.NotEmpty(await svc.SaveSettingsAsync("http://insecure.example", 14, false, 28, "admin"));
        Assert.Empty(await svc.SaveSettingsAsync("https://pas.example.nz/", 14, false, 28, "admin"));
        var (link, delivered, error) = await svc.SendAsync(c.Id, "nurse");
        Assert.Null(error);
        Assert.True(delivered);
        var token = link!.Split("/e/")[1];
        var msg = await sp.GetRequiredService<RemindersDbContext>().Messages.SingleAsync(m => m.Kind == MessageKind.ConsentLink && m.Channel == Channel.Sms);
        Assert.Contains("[consent link]", msg.Body, StringComparison.Ordinal);
        Assert.Equal(ConsentService.Hash(token), (await svc.GetAsync(c.Id))!.TokenHash);

        var dob = new DateOnly(1980, 5, 17);
        Assert.Equal("open", await svc.PublicStatusAsync(token));
        for (var i = 0; i < 5; i++) Assert.Null((await svc.VerifyAsync(token, dob.AddDays(1))).Consent);
        Assert.Contains("Too many", (await svc.VerifyAsync(token, dob)).Error, StringComparison.Ordinal);
        h.Clock.Advance(TimeSpan.FromMinutes(31));
        var (open, _, patient) = await svc.VerifyAsync(token, dob);
        var view = await svc.ViewAsync(open!, patient!);
        Assert.Equal("Aroha", view.GivenName);
        Assert.Contains(view.Sections.Single(s => s.Key == "risks").Risks!, r => r.Text.Contains("warfarin", StringComparison.Ordinal));
        Assert.True(view.Sections.Single(s => s.Key == "risks").Risks![0].Serious); // serious risks first

        var sign = new SignInput(SignerKind.Patient, "Aroha Mere Ngātai", null, null, Png, true, true, true);
        Assert.Contains(await svc.SignAsync(token, dob, sign), e => e.StartsWith("Please read every section", StringComparison.Ordinal));
        foreach (var s in view.Sections) Assert.Null((await svc.MarkReadAsync(token, dob, s.Key)).Error);
        Assert.NotNull((await svc.MarkReadAsync(token, dob, "nope")).Error);

        // A question must be answered before signing.
        Assert.Null(await svc.AskAsync(token, dob, "Can I keep taking my blood pressure tablets?"));
        Assert.Equal(1, await svc.OpenQuestionsAsync());
        Assert.Contains(await svc.SignAsync(token, dob, sign), e => e.StartsWith("We have not answered", StringComparison.Ordinal));
        var q = (await svc.GetAsync(c.Id))!.Questions.Single();
        Assert.Null(await svc.AnswerAsync(q.Id, "Yes – take them with a sip of water on the morning.", "drhall"));

        Assert.Contains(await svc.SignAsync(token, dob, sign with { Agree = false }), e => e.Contains("three statements", StringComparison.Ordinal));
        Assert.Empty(await svc.SignAsync(token, dob, sign));
        var signed = (await svc.GetAsync(c.Id))!;
        Assert.Equal(ConsentStatus.Signed, signed.Status);
        Assert.NotEqual(Convert.FromBase64String(Png[(Png.IndexOf(',', StringComparison.Ordinal) + 1)..]), signed.SignatureImage); // encrypted
        var docs = sp.GetRequiredService<DocumentService>();
        var filed = Assert.Single(await docs.ListAsync(pid));
        Assert.Equal("Consent", filed.Category);
        Assert.Equal("signed", await svc.PublicStatusAsync(token));
        Assert.NotEmpty(await svc.SignAsync(token, dob, sign)); // once only

        // On the day.
        Assert.Null(await svc.ConfirmAsync(c.Id, "Gone through with the patient", "nurse"));
        var confirmed = (await svc.GetAsync(c.Id))!;
        Assert.Equal(ConsentStatus.Confirmed, confirmed.Status);
        Assert.Null(confirmed.TokenHash);
        Assert.True(await sp.GetRequiredService<TheatreDbContext>().Checks.AnyAsync(k => k.BookingId == bid && k.ItemKey == "consent"));
        var now = Assert.Single(await docs.ListAsync(pid)); // the confirmed copy replaced the signed one
        Assert.Contains("confirmed", now.Title, StringComparison.Ordinal);
        Assert.Equal(now.Id, confirmed.DocumentId);
        Assert.NotNull(await svc.ConfirmAsync(c.Id, null, "nurse"));
        var pdf = sp.GetRequiredService<IConsentPdf>() as TestConsentPdf;
        Assert.Equal("Yes – take them with a sip of water on the morning.", pdf!.Rendered[^1].Questions.Single().Answer);
    }

    [Fact]
    public async Task Paper_consent_withdrawal_gate_flags_and_merge()
    {
        await using var h = new ModulesHost();
        await using var scope = h.Scope();
        var sp = scope.ServiceProvider;
        var sheetId = await PublishedAsync(sp, "CTR");
        var (pid, bid) = await BookingAsync(h, sp, "Right carpal tunnel release", dob: new DateOnly(2012, 3, 1));
        var svc = sp.GetRequiredService<ConsentService>();

        // Gate off by default; on, it stops confirmation until signed.
        Assert.Null(await svc.GateAsync(bid));
        Assert.Empty(await svc.SaveSettingsAsync(null, 14, true, 28, "admin"));
        Assert.NotNull(await svc.GateAsync(bid));

        var bad = await svc.RecordPaperAsync(new PaperInput(bid, null, SignerKind.Parent, "Mere Ngātai", null, null, new DateOnly(2026, 12, 1), "consent.pdf", Pdf), "reception");
        Assert.Contains(bad.Errors, e => e.StartsWith("Say how", StringComparison.Ordinal));
        Assert.Contains("The signing date cannot be in the future.", bad.Errors);
        var (paper, errors) = await svc.RecordPaperAsync(new PaperInput(bid, null, SignerKind.Parent, "Mere Ngātai", "Mother", "Te reo Māori – Hemi Walker", new DateOnly(2026, 11, 5), "consent.pdf", Pdf), "reception");
        Assert.Empty(errors);
        Assert.Equal(sheetId, paper!.SheetId); // chosen from the procedure
        Assert.Equal(ConsentMethod.Paper, paper.Method);
        Assert.Null(await svc.GateAsync(bid));
        var row = Assert.Single(await svc.ListAsync("signed"));
        Assert.Contains(row.Flags, f => f.StartsWith("Signed by Mere Ngātai (Mother)", StringComparison.Ordinal));
        Assert.Contains(row.Flags, f => f.StartsWith("Interpreter", StringComparison.Ordinal));
        Assert.Single(await sp.GetRequiredService<DocumentService>().ListAsync(pid));

        Assert.Null(await svc.ConfirmAsync(paper.Id, null, "nurse"));
        Assert.NotNull(await svc.WithdrawAsync(paper.Id, "no", "nurse"));
        Assert.Null(await svc.WithdrawAsync(paper.Id, "The family want to wait", "nurse"));
        Assert.False(await sp.GetRequiredService<TheatreDbContext>().Checks.AnyAsync(k => k.BookingId == bid && k.ItemKey == "consent"));
        Assert.NotNull(await svc.GateAsync(bid));
        Assert.Single(await svc.ListAsync("withdrawn"));
        Assert.Single(await svc.ToPrepareAsync()); // needs a new consent

        // Under 16 and signed for themselves is flagged.
        var under = new ConsentRecord { Signer = SignerKind.Patient, SignedAt = h.Clock.GetUtcNow(), Status = ConsentStatus.Signed };
        Assert.Contains(ConsentService.Flags(under, new DateOnly(2012, 3, 1), null, new DateOnly(2026, 11, 9)), f => f.Contains("under 16", StringComparison.Ordinal));

        var keep = await h.PatientAsync("Keep", "Me", null);
        Assert.Equal(1, await svc.MergeAsync(keep.Id, pid));
        Assert.Equal(keep.Id, (await svc.GetAsync(paper.Id))!.PatientId);
    }

    [Fact]
    public async Task Patient_can_withdraw_and_staff_cannot_change_a_sent_consent()
    {
        await using var h = new ModulesHost();
        await using var scope = h.Scope();
        var sp = scope.ServiceProvider;
        var sheetId = await PublishedAsync(sp);
        var (_, bid) = await BookingAsync(h, sp);
        var svc = sp.GetRequiredService<ConsentService>();
        Assert.Empty(await svc.SaveSettingsAsync("https://pas.example.nz", 14, false, 28, "admin"));
        var c = (await svc.PrepareAsync(bid, sheetId, null, null, "nurse")).Consent!;
        Assert.Null(await svc.EditDraftAsync(c.Id, "Extra", "Please read before your appointment", "nurse"));
        var token = (await svc.SendAsync(c.Id, "nurse")).Link!.Split("/e/")[1];
        Assert.NotNull(await svc.EditDraftAsync(c.Id, "Changed", null, "nurse"));
        var token2 = (await svc.SendAsync(c.Id, "nurse")).Link!.Split("/e/")[1]; // a new link stops the old one
        Assert.Null(await svc.PublicStatusAsync(token));
        Assert.Null(await svc.PatientWithdrawAsync(token2, new DateOnly(1980, 5, 17), "I want to talk to my GP first"));
        var w = (await svc.GetAsync(c.Id))!;
        Assert.Equal(ConsentStatus.Withdrawn, w.Status);
        Assert.Equal(ConsentService.PatientUser, w.WithdrawnBy);
        Assert.Equal("closed", await svc.PublicStatusAsync(token2) ?? "closed");
        h.Clock.Advance(TimeSpan.FromDays(20));
        Assert.Contains("expired", ConsentService.Flags(new ConsentRecord { Status = ConsentStatus.Sent, LinkExpiresAt = h.Clock.GetUtcNow().AddDays(-1) }, default, null, BrightPAS.Core.NzTime.Today(h.Clock))[0], StringComparison.Ordinal);
    }
}
