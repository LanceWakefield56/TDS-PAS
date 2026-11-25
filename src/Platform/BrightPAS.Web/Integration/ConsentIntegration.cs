using BrightPAS.Core.Modules;
using BrightPAS.Licensing;
using BrightPAS.Modules.Consent;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Integration;

/// <summary>Optional (off by default): a theatre booking cannot be confirmed until the patient's consent is signed (Consent › Settings).</summary>
public sealed class ConsentBookingGate(LicenceCache licence, ConsentService consent) : IBookingConfirmGate
{
    public Task<string?> CheckAsync(Guid bookingId, Guid patientId, string procedure, CancellationToken ct) =>
        licence.Status.AccessTo(ConsentModule.Id) == ModuleAccess.Full ? consent.GateAsync(bookingId, ct) : Task.FromResult<string?>(null);
}
