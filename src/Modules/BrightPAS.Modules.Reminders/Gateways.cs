using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BrightPAS.Modules.Reminders;

public sealed record SendResult(bool Ok, string? ProviderMessageId, string? Error, bool Permanent = false)
{
    public static SendResult Success(string? id) => new(true, id, null);
    public static SendResult Retry(string error) => new(false, null, error);
    public static SendResult Fail(string error) => new(false, null, error, Permanent: true);
}

/// <summary>An SMS vendor. Add a class and register it to support a new vendor; the settings screen picks one by <see cref="Provider"/>.</summary>
public interface ISmsGateway
{
    SmsProvider Provider { get; }
    /// <summary>Plain-English problem if the vendor is not fully set up, otherwise null.</summary>
    string? ConfigurationProblem(ReminderSettings s);
    Task<SendResult> SendAsync(ReminderSettings s, string toE164, string text, string reference, CancellationToken ct);
}

public interface IEmailGateway
{
    EmailProvider Provider { get; }
    string? ConfigurationProblem(ReminderSettings s);
    Task<SendResult> SendAsync(ReminderSettings s, string to, string subject, string body, CancellationToken ct);
}

/// <summary>
/// Modica Mobile Gateway REST v1: POST {base}/messages with Basic auth (application name + password) and JSON
/// { destination, content, reference, mask }. 201 Created returns the message id. Replies and delivery receipts
/// arrive at BrightPAS callback URLs configured in Modica (see the installation guide).
/// </summary>
public sealed class ModicaGateway(IHttpClientFactory http, ISecretProtector secrets) : ISmsGateway
{
    public const string HttpClientName = "modica";
    public const string DefaultBaseUrl = "https://api.modicagroup.com/rest/gateway";
    public SmsProvider Provider => SmsProvider.Modica;

    public string? ConfigurationProblem(ReminderSettings s) =>
        string.IsNullOrWhiteSpace(s.ModicaApplication) || string.IsNullOrWhiteSpace(s.ModicaPasswordProtected)
            ? "Modica is the SMS vendor but its application name and password have not been entered (Reminders → Settings)."
            : !Uri.TryCreate(s.ModicaBaseUrl, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps ? "The Modica address must start with https://." : null;

    public async Task<SendResult> SendAsync(ReminderSettings s, string toE164, string text, string reference, CancellationToken ct)
    {
        if (ConfigurationProblem(s) is { } problem) return SendResult.Retry(problem);
        var password = secrets.Unprotect(s.ModicaPasswordProtected) ?? "";
        var body = new JsonObject { ["destination"] = toE164, ["content"] = text, ["reference"] = reference };
        if (!string.IsNullOrWhiteSpace(s.ModicaMask)) body["mask"] = s.ModicaMask;
        using var req = new HttpRequestMessage(HttpMethod.Post, s.ModicaBaseUrl.TrimEnd('/') + "/messages")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{s.ModicaApplication}:{password}")));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        try
        {
            using var res = await http.CreateClient(HttpClientName).SendAsync(req, ct);
            var text2 = await res.Content.ReadAsStringAsync(ct);
            if (res.StatusCode == HttpStatusCode.Created || res.IsSuccessStatusCode)
            {
                var id = text2.Trim().Trim('"', '[', ']');
                if (string.IsNullOrEmpty(id) && res.Headers.Location is { } loc) id = loc.ToString().Split('/').Last();
                return SendResult.Success(id);
            }
            var error = $"Modica returned {(int)res.StatusCode}: {Short(text2)}";
            return res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity
                ? SendResult.Fail(error) : SendResult.Retry(error);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return SendResult.Retry($"Could not reach Modica: {ex.Message}"); }
    }

    internal static string Short(string s) => s.Length > 300 ? s[..300] : s;
}

/// <summary>Any other SMS vendor with an HTTP API: POSTs the configured JSON template ({to}, {message}, {reference}) with an optional auth header.</summary>
public sealed class GenericHttpGateway(IHttpClientFactory http, ISecretProtector secrets) : ISmsGateway
{
    public const string HttpClientName = "sms-generic";
    public SmsProvider Provider => SmsProvider.GenericHttp;

    public string? ConfigurationProblem(ReminderSettings s) =>
        !Uri.TryCreate(s.GenericUrl, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps
            ? "Enter the SMS vendor's https:// address (Reminders → Settings)."
            : !s.GenericBodyTemplate.Contains("{message}", StringComparison.Ordinal) || !s.GenericBodyTemplate.Contains("{to}", StringComparison.Ordinal)
                ? "The message template must contain {to} and {message}." : null;

    public static string BuildBody(string template, string to, string text, string reference)
    {
        static string J(string v) => JsonSerializer.Serialize(v)[1..^1];
        return template.Replace("{to}", J(to)).Replace("{message}", J(text)).Replace("{reference}", J(reference));
    }

    public async Task<SendResult> SendAsync(ReminderSettings s, string toE164, string text, string reference, CancellationToken ct)
    {
        if (ConfigurationProblem(s) is { } problem) return SendResult.Retry(problem);
        using var req = new HttpRequestMessage(HttpMethod.Post, s.GenericUrl)
        {
            Content = new StringContent(BuildBody(s.GenericBodyTemplate, toE164, text, reference), Encoding.UTF8, "application/json")
        };
        var auth = secrets.Unprotect(s.GenericAuthValueProtected);
        if (!string.IsNullOrWhiteSpace(s.GenericAuthHeader) && !string.IsNullOrEmpty(auth)) req.Headers.TryAddWithoutValidation(s.GenericAuthHeader, auth);
        try
        {
            using var res = await http.CreateClient(HttpClientName).SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (res.IsSuccessStatusCode) return SendResult.Success(ModicaGateway.Short(body.Trim()));
            var error = $"SMS vendor returned {(int)res.StatusCode}: {ModicaGateway.Short(body)}";
            return (int)res.StatusCode is >= 400 and < 500 and not 429 ? SendResult.Fail(error) : SendResult.Retry(error);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return SendResult.Retry($"Could not reach the SMS vendor: {ex.Message}"); }
    }
}

/// <summary>Test mode: nothing leaves the server; the message is shown in the outbox as sent.</summary>
public sealed class TestSmsGateway : ISmsGateway
{
    public SmsProvider Provider => SmsProvider.TestOutbox;
    public string? ConfigurationProblem(ReminderSettings s) => null;
    public Task<SendResult> SendAsync(ReminderSettings s, string toE164, string text, string reference, CancellationToken ct) =>
        Task.FromResult(SendResult.Success("TEST-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant()));
}

public sealed class SmtpEmailGateway(ISecretProtector secrets) : IEmailGateway
{
    public EmailProvider Provider => EmailProvider.Smtp;

    public string? ConfigurationProblem(ReminderSettings s) =>
        string.IsNullOrWhiteSpace(s.SmtpHost) || string.IsNullOrWhiteSpace(s.FromAddress)
            ? "Email is switched on but the SMTP server and 'from' address have not been entered (Reminders → Settings)." : null;

    public async Task<SendResult> SendAsync(ReminderSettings s, string to, string subject, string body, CancellationToken ct)
    {
        if (ConfigurationProblem(s) is { } problem) return SendResult.Retry(problem);
        try
        {
            using var msg = new MailMessage(new MailAddress(s.FromAddress!, s.FromName), new MailAddress(to))
            {
                Subject = subject, Body = body, IsBodyHtml = false, BodyEncoding = Encoding.UTF8, SubjectEncoding = Encoding.UTF8
            };
            var id = $"<{Guid.NewGuid():N}@brightpas>";
            msg.Headers.Add("Message-ID", id);
            using var client = new SmtpClient(s.SmtpHost, s.SmtpPort) { EnableSsl = s.SmtpStartTls, DeliveryMethod = SmtpDeliveryMethod.Network };
            var pw = secrets.Unprotect(s.SmtpPasswordProtected);
            if (!string.IsNullOrWhiteSpace(s.SmtpUser)) client.Credentials = new NetworkCredential(s.SmtpUser, pw);
            await client.SendMailAsync(msg, ct);
            return SendResult.Success(id);
        }
        catch (FormatException ex) { return SendResult.Fail($"Invalid email address: {ex.Message}"); }
        catch (SmtpFailedRecipientException ex) { return SendResult.Fail($"Email address refused: {ex.Message}"); }
        catch (Exception ex) when (ex is SmtpException or IOException or InvalidOperationException) { return SendResult.Retry($"Email server problem: {ex.Message}"); }
    }
}

public sealed class TestEmailGateway : IEmailGateway
{
    public EmailProvider Provider => EmailProvider.TestOutbox;
    public string? ConfigurationProblem(ReminderSettings s) => null;
    public Task<SendResult> SendAsync(ReminderSettings s, string to, string subject, string body, CancellationToken ct) =>
        Task.FromResult(SendResult.Success("TEST-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant()));
}

/// <summary>NZ phone and email helpers.</summary>
public static class Addresses
{
    /// <summary>Normalises an NZ number to E.164 (+64…). Returns null if it is not a valid number.</summary>
    public static string? ToE164(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digits = new string(raw.Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (digits.StartsWith("+", StringComparison.Ordinal)) digits = "+" + digits[1..].Replace("+", "");
        else if (digits.StartsWith("00", StringComparison.Ordinal)) digits = "+" + digits[2..];
        else if (digits.StartsWith("64", StringComparison.Ordinal) && digits.Length >= 10) digits = "+" + digits;
        else if (digits.StartsWith("0", StringComparison.Ordinal)) digits = "+64" + digits[1..];
        else return null;
        return digits.Length is >= 9 and <= 16 && digits[1..].All(char.IsDigit) ? digits : null;
    }

    /// <summary>NZ mobile numbers (+642x…) – only these can receive SMS.</summary>
    public static bool IsNzMobile(string? e164) => e164 is not null && e164.StartsWith("+642", StringComparison.Ordinal) && e164.Length is >= 11 and <= 13;

    public static string? NormaliseEmail(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try { var a = new MailAddress(raw.Trim()); return a.Address.ToLowerInvariant(); }
        catch (FormatException) { return null; }
    }

    public static string Mask(string address) =>
        address.Contains('@') ? address[..Math.Min(2, address.IndexOf('@'))] + "…" + address[address.IndexOf('@')..]
        : address.Length > 6 ? address[..4] + new string('•', address.Length - 7) + address[^3..] : address;

    public static string FormatDate(DateOnly d) => d.ToString("ddd d MMM", CultureInfo.GetCultureInfo("en-NZ"));
    public static string FormatTime(TimeOnly t) => t.ToString("h:mm tt", CultureInfo.GetCultureInfo("en-NZ")).ToLowerInvariant().Replace(" ", "");
}
