using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Reminders;

namespace BrightPAS.Modules.Tests;

/// <summary>0.11.0: OCR text is stored encrypted, retried, and searched per patient; portal links go out by SMS without keeping the secret.</summary>
public class DocumentTextAndPortalLinkTests
{
    private static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF"u8.ToArray();

    private sealed class FakeExtractor(string? text, string? problem = null) : IDocumentTextExtractor
    {
        public int Calls;
        public Task<(string? Text, string Method, string? Problem)> ExtractAsync(string contentType, byte[] content, CancellationToken ct)
        { Calls++; return Task.FromResult((text, "ocr", problem)); }
    }

    [Fact]
    public async Task Document_text_is_encrypted_searchable_per_patient_and_failures_retry_three_times()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<DocumentService>();
        var (doc, _) = await svc.AddAsync(new(p.Id, null, DocumentCategory.Consent, "Consent", "c.pdf", Convert.ToBase64String(Pdf), DocumentSource.Scan), "rn");
        var (doc2, _) = await svc.AddAsync(new(p.Id, null, DocumentCategory.Referral, "Referral", "r.pdf", Convert.ToBase64String(Pdf), DocumentSource.Scan), "rn");
        Assert.Equal(2, (await svc.PendingTextAsync(10)).Count);
        Assert.Equal("Pending", (await svc.ListAsync(p.Id))[0].TextStatus);

        Assert.Equal(TextStatus.Done, await svc.ProcessTextAsync(doc!.Id, new FakeExtractor("Allergy: PENICILLIN anaphylaxis. Consent signed.")));
        var db = s.ServiceProvider.GetRequiredService<PatientsDbContext>();
        var row = await db.DocumentTexts.AsNoTracking().SingleAsync(t => t.DocumentId == doc.Id);
        Assert.DoesNotContain("PENICILLIN", System.Text.Encoding.UTF8.GetString(row.Content!)); // stored encrypted

        var bad = new FakeExtractor(null, "engine down");
        Assert.Equal(TextStatus.Pending, await svc.ProcessTextAsync(doc2!.Id, bad));
        Assert.Equal(TextStatus.Pending, await svc.ProcessTextAsync(doc2.Id, bad));
        Assert.Equal(TextStatus.Failed, await svc.ProcessTextAsync(doc2.Id, bad));
        Assert.Empty(await svc.PendingTextAsync(10));
        Assert.Equal(1, await svc.RetryFailedTextAsync());
        Assert.Single(await svc.PendingTextAsync(10));

        var hits = await svc.SearchAsync(p.Id, "penicillin", "dr");
        var hit = Assert.Single(hits);
        Assert.Equal(doc.Id, hit.Id);
        Assert.Contains("PENICILLIN", hit.Snippet);
        Assert.Empty(await svc.SearchAsync(Guid.NewGuid(), "penicillin", "dr")); // another patient sees nothing
        Assert.Empty(await svc.SearchAsync(p.Id, "warfarin", "dr"));
    }

    [Fact]
    public async Task Portal_link_sms_replaces_older_queued_link_and_does_not_keep_the_secret()
    {
        var (h, p, _) = await RemindersTests.Setup();
        await using var _h = h;
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<ReminderService>();
        await svc.QueuePortalLinkAsync(p.Id, "https://pas/my/OLDSECRET", sms: true, email: false, "rec");
        var list = await svc.QueuePortalLinkAsync(p.Id, "https://pas/my/NEWSECRET", sms: true, email: false, "rec");
        Assert.Single(list);
        h.Clock.Advance(TimeSpan.FromMinutes(3));
        await svc.SendDueAsync();
        var msgs = (await svc.MessagesAsync(null, p.Id)).Where(m => m.Kind == "PortalLink").ToList();
        Assert.Contains(msgs, m => m.Status == "Cancelled");
        var sent = Assert.Single(msgs, m => m.Status == "Sent");
        Assert.DoesNotContain("NEWSECRET", sent.Body); Assert.Contains("[portal link]", sent.Body);
        Assert.All(await s.ServiceProvider.GetRequiredService<RemindersDbContext>().Messages.AsNoTracking().Where(m => m.Kind == MessageKind.PortalLink).ToListAsync(),
            m => Assert.Null(m.SecretLink));
    }
}
