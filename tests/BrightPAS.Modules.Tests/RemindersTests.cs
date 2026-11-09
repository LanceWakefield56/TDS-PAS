using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Data;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Reminders;
using BrightPAS.Modules.Theatre;

namespace BrightPAS.Modules.Tests;

public class ReminderHelperTests
{
    [Theory]
    [InlineData("021 123 4567", "+64211234567", true)]
    [InlineData("+64 21 123 4567", "+64211234567", true)]
    [InlineData("0064211234567", "+64211234567", true)]
    [InlineData("64211234567", "+64211234567", true)]
    [InlineData("(03) 544 1234", "+6435441234", false)]
    [InlineData("hello", null, false)]
    [InlineData("", null, false)]
    public void Normalises_nz_numbers(string raw, string? e164, bool mobile)
    {
        Assert.Equal(e164, Addresses.ToE164(raw));
        Assert.Equal(mobile, Addresses.IsNzMobile(Addresses.ToE164(raw)));
    }

    [Theory]
    [InlineData("Yes", ReplyMeaning.Confirm)]
    [InlineData(" y ", ReplyMeaning.Confirm)]
    [InlineData("Āe", ReplyMeaning.Confirm)]
    [InlineData("YES see you then!", ReplyMeaning.Confirm)]
    [InlineData("no", ReplyMeaning.Cancel)]
    [InlineData("Can't make it", ReplyMeaning.Cancel)]
    [InlineData("STOP", ReplyMeaning.OptOut)]
    [InlineData("start", ReplyMeaning.OptIn)]
    [InlineData("What time is it?", ReplyMeaning.Unknown)]
    public void Interprets_replies(string body, ReplyMeaning meaning) => Assert.Equal(meaning, ReminderService.Interpret(body));

    [Fact]
    public void Renders_placeholders_and_drops_unknown_values()
    {
        var text = ReminderService.Render("Kia ora {FirstName}, {Date} at {ArrivalTime}. {ClinicPhone}", new Dictionary<string, string?> { ["FirstName"] = "Aroha", ["Date"] = "Tue 10 Nov", ["ArrivalTime"] = "7:00am" });
        Assert.Equal("Kia ora Aroha, Tue 10 Nov at 7:00am.", text);
    }

    [Fact]
    public void Generic_vendor_body_escapes_message_text()
    {
        var body = GenericHttpGateway.BuildBody("""{"to":"{to}","message":"{message}"}""", "+64211234567", "Say \"hi\"\n\\ok", "r1");
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("Say \"hi\"\n\\ok", doc.RootElement.GetProperty("message").GetString());
    }
}

public class ModicaGatewayTests
{
    private sealed class Handler(HttpStatusCode code, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request; public string? Sent;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request; Sent = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(code) { Content = new StringContent(body) };
        }
    }
    private sealed class Factory(HttpMessageHandler h) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(h, false); }

    private static ReminderSettings Settings(ISecretProtector p) => new() { ModicaApplication = "tds-app", ModicaPasswordProtected = p.Protect("s3cret"), ModicaMask = "TDS" };

    [Fact]
    public async Task Sends_rest_v1_request_with_basic_auth()
    {
        var p = new ReversibleTestProtector();
        var h = new Handler(HttpStatusCode.Created, "1234567890");
        var r = await new ModicaGateway(new Factory(h), p).SendAsync(Settings(p), "+64211234567", "Hello", "ref1", default);
        Assert.True(r.Ok);
        Assert.Equal("1234567890", r.ProviderMessageId);
        Assert.Equal("https://api.modicagroup.com/rest/gateway/messages", h.Request!.RequestUri!.ToString());
        Assert.Equal("Basic", h.Request.Headers.Authorization!.Scheme);
        Assert.Equal("tds-app:s3cret", Encoding.UTF8.GetString(Convert.FromBase64String(h.Request.Headers.Authorization.Parameter!)));
        using var doc = JsonDocument.Parse(h.Sent!);
        Assert.Equal("+64211234567", doc.RootElement.GetProperty("destination").GetString());
        Assert.Equal("Hello", doc.RootElement.GetProperty("content").GetString());
        Assert.Equal("TDS", doc.RootElement.GetProperty("mask").GetString());
    }

    [Fact]
    public async Task Server_errors_retry_and_auth_errors_are_permanent()
    {
        var p = new ReversibleTestProtector();
        var retry = await new ModicaGateway(new Factory(new Handler(HttpStatusCode.ServiceUnavailable, "busy")), p).SendAsync(Settings(p), "+64211234567", "x", "r", default);
        Assert.False(retry.Ok); Assert.False(retry.Permanent);
        var perm = await new ModicaGateway(new Factory(new Handler(HttpStatusCode.Unauthorized, "")), p).SendAsync(Settings(p), "+64211234567", "x", "r", default);
        Assert.False(perm.Ok); Assert.True(perm.Permanent);
    }

    [Fact]
    public void Missing_credentials_are_reported()
    {
        var g = new ModicaGateway(new Factory(new Handler(HttpStatusCode.OK, "")), new ReversibleTestProtector());
        Assert.Contains("application name and password", g.ConfigurationProblem(new ReminderSettings()));
    }
}

public class RemindersTests
{
    private static readonly DateOnly Tomorrow = new(2026, 11, 10);
    /// <summary>Monday 9 November 2026, 09:00 New Zealand time.</summary>
    private static readonly DateTimeOffset MondayNineAm = new(2026, 11, 8, 20, 0, 0, TimeSpan.Zero);

    private static async Task<(ModulesHost H, Patient P, Guid Booking)> Setup(string? mobile = "021 123 4567", string? email = "aroha@example.nz", DateOnly? date = null)
    {
        var h = new ModulesHost();
        h.Clock.SetUtcNow(MondayNineAm);
        await using var s = h.Scope();
        var sp = s.ServiceProvider;
        await sp.GetRequiredService<ReminderService>().SettingsAsync(); // confirmations apply to bookings made from now on
        h.Clock.Advance(TimeSpan.FromSeconds(1));
        var reg = await sp.GetRequiredService<PatientService>().RegisterAsync(new PatientRegistration("Ngātai", "Aroha Mere", new DateOnly(1980, 5, 17), BirthSex.Female,
            "ZSC21TN", MobilePhone: mobile, Email: email, ConfirmedNotDuplicate: true), "reception");
        Assert.True(reg.Succeeded, string.Join("; ", reg.Errors));
        var th1 = await sp.GetRequiredService<PasDbContext>().Locations.SingleAsync(l => l.Code == "TH1");
        var theatre = sp.GetRequiredService<TheatreService>();
        var (session, _) = await theatre.CreateSessionAsync(new(th1.Id, date ?? Tomorrow, new(8, 0), new(12, 0), SessionType.Surgical, Doc.Id("Dr Ben Hall"), null, null), "rec");
        var b = await theatre.BookAsync(new(session!.Id, reg.Patient!.Id, "Inguinal hernia repair", Laterality.Left, 60, AnaestheticType.General, null), "rec");
        Assert.NotNull(b.Booking);
        return (h, reg.Patient, b.Booking!.Id);
    }

    private static async Task<List<OutboundMessage>> Messages(ModulesHost h)
    {
        await using var s = h.Scope();
        return await s.ServiceProvider.GetRequiredService<RemindersDbContext>().Messages.AsNoTracking().OrderBy(m => m.CreatedAt).ToListAsync();
    }

    private static async Task Cycle(ModulesHost h)
    {
        await using var s = h.Scope();
        await s.ServiceProvider.GetRequiredService<ReminderService>().RunCycleAsync();
    }

    [Fact]
    public async Task New_booking_gets_sms_and_email_confirmation_once_without_clinical_detail()
    {
        var (h, _, booking) = await Setup();
        await using var _h = h;
        await Cycle(h);
        await Cycle(h);
        var msgs = await Messages(h);
        Assert.Equal(2, msgs.Count);
        Assert.All(msgs, m => { Assert.Equal(MessageKind.BookingConfirmation, m.Kind); Assert.Equal(MessageStatus.Sent, m.Status); Assert.Equal(booking, m.BookingId); });
        var sms = msgs.Single(m => m.Channel == Channel.Sms);
        Assert.Equal("+64211234567", sms.Address);
        Assert.Contains("Aroha", sms.Body);
        Assert.Contains("Tue 10 Nov", sms.Body);
        Assert.Contains("7:00am", sms.Body); // planned 8:00 minus 60 minutes
        Assert.DoesNotContain("hernia", sms.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hernia", msgs.Single(m => m.Channel == Channel.Email).Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Patient_without_mobile_is_recorded_as_held_not_sent()
    {
        var (h, _, _) = await Setup(mobile: "03 544 1234", email: null);
        await using var _h = h;
        await Cycle(h);
        var m = Assert.Single(await Messages(h));
        Assert.Equal(MessageStatus.Suppressed, m.Status);
        Assert.Contains("mobile", m.LastError);
    }

    [Fact]
    public async Task Day_before_reminder_waits_for_send_hour_and_skips_fresh_confirmations()
    {
        var (h, _, _) = await Setup(email: null, date: new DateOnly(2026, 11, 11));
        await using var _h = h;
        await Cycle(h);                                   // Monday 09:00: confirmation only
        h.Clock.Advance(TimeSpan.FromHours(24 + 5));     // Tuesday 14:00: booking is tomorrow
        await Cycle(h);
        var msgs = await Messages(h);
        Assert.Equal([MessageKind.BookingConfirmation, MessageKind.DayBeforeReminder], msgs.Select(m => m.Kind));
        Assert.Contains("tomorrow", msgs[1].Body);
    }

    [Fact]
    public async Task Quiet_hours_hold_automatic_messages_until_morning()
    {
        var (h, _, _) = await Setup(email: null);
        await using var _h = h;
        h.Clock.Advance(TimeSpan.FromHours(12));          // 21:00
        await Cycle(h);
        Assert.Equal(MessageStatus.Queued, (await Messages(h)).Single().Status);
        h.Clock.Advance(TimeSpan.FromHours(11));          // 08:00 next day
        await Cycle(h);
        Assert.Equal(MessageStatus.Sent, (await Messages(h)).Single().Status);
    }

    [Fact]
    public async Task Cancelled_booking_is_not_messaged()
    {
        var (h, _, booking) = await Setup(email: null);
        await using var _h = h;
        h.Clock.Advance(TimeSpan.FromHours(12)); // quiet hours: message queued, not sent
        await Cycle(h);
        await using (var s = h.Scope())
            Assert.Null(await s.ServiceProvider.GetRequiredService<TheatreService>().SetStatusAsync(booking, BookingStatus.Cancelled, "Patient unwell", "rec"));
        h.Clock.Advance(TimeSpan.FromHours(11));
        await Cycle(h);
        var m = (await Messages(h)).Single();
        Assert.Equal(MessageStatus.Cancelled, m.Status);
    }

    [Fact]
    public async Task Yes_reply_confirms_booking_and_thanks_patient()
    {
        var (h, _, booking) = await Setup(email: null);
        await using var _h = h;
        await Cycle(h);
        await using (var s = h.Scope())
        {
            var reply = await s.ServiceProvider.GetRequiredService<ReminderService>().HandleReplyAsync("021 123 4567", "Yes", "TestOutbox", "mo-1", null);
            Assert.Equal(ReplyMeaning.Confirm, reply.Meaning);
            Assert.Equal(ReplyStatus.Handled, reply.Status);
            Assert.Equal(booking, reply.BookingId);
            var b = await s.ServiceProvider.GetRequiredService<TheatreDbContext>().Bookings.AsNoTracking().SingleAsync(x => x.Id == booking);
            Assert.Equal(BookingStatus.Confirmed, b.Status);
            // The same vendor message delivered twice is only processed once.
            var again = await s.ServiceProvider.GetRequiredService<ReminderService>().HandleReplyAsync("021 123 4567", "Yes", "TestOutbox", "mo-1", null);
            Assert.Equal(reply.Id, again.Id);
        }
        await Cycle(h);
        Assert.Contains(await Messages(h), m => m.Kind == MessageKind.ReplyConfirmed && m.Status == MessageStatus.Sent);
    }

    [Fact]
    public async Task No_reply_goes_to_staff_and_does_not_cancel_automatically()
    {
        var (h, _, booking) = await Setup(email: null);
        await using var _h = h;
        await Cycle(h);
        await using var s = h.Scope();
        var reply = await s.ServiceProvider.GetRequiredService<ReminderService>().HandleReplyAsync("+64211234567", "No sorry", "TestOutbox", "mo-2", null);
        Assert.Equal(ReplyStatus.New, reply.Status);
        Assert.Contains("phone", reply.ActionTaken);
        var b = await s.ServiceProvider.GetRequiredService<TheatreDbContext>().Bookings.AsNoTracking().SingleAsync(x => x.Id == booking);
        Assert.Equal(BookingStatus.Booked, b.Status);
    }

    [Fact]
    public async Task Stop_opts_out_and_later_messages_are_held_until_start()
    {
        var (h, p, _) = await Setup(email: null);
        await using var _h = h;
        await Cycle(h);
        await using (var s = h.Scope())
            await s.ServiceProvider.GetRequiredService<ReminderService>().HandleReplyAsync("0211234567", "STOP", "TestOutbox", "mo-3", null);
        await Cycle(h);
        Assert.Contains(await Messages(h), m => m.Kind == MessageKind.OptOutConfirmed && m.Status == MessageStatus.Sent);
        await using (var s = h.Scope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ReminderService>();
            var held = await svc.QueueFormLinkAsync(p.Id, null, "https://pas/q/secret-token", sms: true, email: false, "nurse");
            Assert.Equal(MessageStatus.Suppressed, Assert.Single(held).Status);
            await svc.HandleReplyAsync("0211234567", "START", "TestOutbox", "mo-4", null);
            var ok = await svc.QueueFormLinkAsync(p.Id, null, "https://pas/q/secret-token2", sms: true, email: false, "nurse");
            Assert.Equal(MessageStatus.Queued, Assert.Single(ok).Status);
        }
    }

    [Fact]
    public async Task Questionnaire_link_is_removed_from_storage_once_sent()
    {
        var (h, p, _) = await Setup();
        await using var _h = h;
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<ReminderService>();
        await svc.QueueFormLinkAsync(p.Id, null, "https://pas/q/TOPSECRETTOKEN", sms: true, email: true, "nurse");
        await svc.SendDueAsync();
        var links = (await svc.MessagesAsync(null, p.Id)).Where(m => m.Kind == "FormLink").ToList();
        Assert.Equal(2, links.Count);
        Assert.All(links, m => { Assert.Equal("Sent", m.Status); Assert.DoesNotContain("TOPSECRETTOKEN", m.Body); Assert.Contains("[questionnaire link]", m.Body); });
        Assert.All(await s.ServiceProvider.GetRequiredService<RemindersDbContext>().Messages.AsNoTracking().Where(m => m.Kind == MessageKind.FormLink).ToListAsync(),
            m => Assert.Null(m.SecretLink));
    }

    [Fact]
    public async Task Vendor_passwords_are_stored_encrypted_and_blank_keeps_them()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<ReminderService>();
        var settings = await svc.SettingsAsync();
        Assert.Equal(SmsProvider.TestOutbox, settings.SmsProvider); // installation default from Setup
        ReminderService.SettingsUpdate U(string? pw) => new(SmsProvider.Modica, EmailProvider.Smtp, true, true, true, 14, 60, new(20, 0), new(8, 0), "Tasman Day Surgery", "03 544 0000",
            null, "tds-app", pw, null, null, null, null, null, "smtp.example.nz", 587, true, null, null, "noreply@tds.example.nz", null);
        Assert.Empty(await svc.UpdateSettingsAsync(U("pa55word"), "admin"));
        var stored = (await svc.SettingsAsync()).ModicaPasswordProtected!;
        Assert.StartsWith("ENC:", stored);
        Assert.DoesNotContain("pa55word", stored);
        Assert.Empty(await svc.UpdateSettingsAsync(U(null), "admin"));
        Assert.Equal(stored, (await svc.SettingsAsync()).ModicaPasswordProtected);
        Assert.Contains(await svc.UpdateSettingsAsync(U(null) with { DayBeforeHour = 23 }, "admin"), e => e.Contains("6 am"));
    }

    [Fact]
    public async Task Callback_key_is_checked_in_constant_time_and_can_be_rotated()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<ReminderService>();
        var key = await svc.WebhookKeyAsync();
        Assert.True(key.Length >= 32);
        Assert.True(await svc.WebhookKeyMatchesAsync(key));
        Assert.False(await svc.WebhookKeyMatchesAsync(key[..^1] + "x"));
        Assert.False(await svc.WebhookKeyMatchesAsync(null));
        var rotated = await svc.RotateWebhookKeyAsync("admin");
        Assert.False(await svc.WebhookKeyMatchesAsync(key));
        Assert.True(await svc.WebhookKeyMatchesAsync(rotated));
    }

    [Fact]
    public async Task Templates_reject_unknown_placeholders_and_link_message_needs_link()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<ReminderService>();
        Assert.Contains("Unknown placeholder", await svc.SaveTemplateAsync(MessageKind.BookingConfirmation, Channel.Sms, "en", null, "Hi {Procedure}", true, "admin"));
        Assert.Contains("{Link}", await svc.SaveTemplateAsync(MessageKind.FormLink, Channel.Sms, "en", null, "Please fill in our form", true, "admin"));
        Assert.Null(await svc.SaveTemplateAsync(MessageKind.BookingConfirmation, Channel.Sms, "mi", null, "Kia ora {FirstName} – {Date}", true, "admin"));
    }
}
