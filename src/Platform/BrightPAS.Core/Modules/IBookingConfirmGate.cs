namespace BrightPAS.Core.Modules;

/// <summary>
/// A module that must agree before a theatre booking is confirmed (0.22.0: triage sign-off, spec 7.25.4). Returns null to allow,
/// or the reason in plain English. Every registered gate is asked; the first refusal stops the confirmation.
/// </summary>
public interface IBookingConfirmGate
{
    Task<string?> CheckAsync(Guid bookingId, Guid patientId, string procedure, CancellationToken ct);
}
