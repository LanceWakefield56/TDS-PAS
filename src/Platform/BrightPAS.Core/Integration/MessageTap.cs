namespace BrightPAS.Core.Integration;

/// <summary>
/// A copy of traffic that another module sends or receives itself (HealthLink letters, SMS, email), recorded in the messaging engine's
/// message store so every interface shows on one dashboard and one message viewer (ADR 0021). Content must not contain secrets (links, passwords).
/// </summary>
public sealed record TappedMessage(string System, bool Inbound, string? MessageType, string? ControlId, Guid? PatientId, string Content,
    bool Ok, string? Detail, string? Reference = null, string Format = "Text");

/// <summary>Implemented by the messaging engine when it is installed. Callers resolve <c>IEnumerable&lt;IMessageTap&gt;</c> so nothing is needed without it. Never throws.</summary>
public interface IMessageTap
{
    Task RecordAsync(TappedMessage message, CancellationToken ct = default);
}

public static class MessageTapExtensions
{
    public static async Task RecordAllAsync(this IEnumerable<IMessageTap> taps, TappedMessage message, CancellationToken ct = default)
    {
        foreach (var t in taps) await t.RecordAsync(message, ct);
    }
}
