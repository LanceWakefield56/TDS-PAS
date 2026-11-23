using Microsoft.EntityFrameworkCore;
using BrightPAS.Modules.Patients;

namespace BrightPAS.Web.Integration;

// Each module moves its own records from a duplicate patient to the surviving record (patient merge, 0.10.0).
// Updates are idempotent (WHERE PatientId = duplicate), so an interrupted merge can simply be run again.

public sealed class TheatreMerge(BrightPAS.Modules.Theatre.TheatreDbContext db) : IPatientMergeParticipant
{
    public string Name => "Theatre & appointments";
    public async Task<int> MoveAsync(Guid from, Guid to, CancellationToken ct) =>
        await db.Bookings.Where(x => x.PatientId == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, to), ct)
        + await db.Appointments.Where(x => x.PatientId == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, to), ct);
}

public sealed class FormsMerge(BrightPAS.Modules.Forms.FormsDbContext db) : IPatientMergeParticipant
{
    public string Name => "Pre-admission forms";
    public Task<int> MoveAsync(Guid from, Guid to, CancellationToken ct) => db.Requests.Where(x => x.PatientId == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, to), ct);
}

public sealed class BillingMerge(BrightPAS.Modules.Billing.BillingDbContext db) : IPatientMergeParticipant
{
    public string Name => "Estimates & invoices";
    public Task<int> MoveAsync(Guid from, Guid to, CancellationToken ct) => db.Documents.Where(x => x.PatientId == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, to), ct);
}

public sealed class LabsMerge(BrightPAS.Modules.Labs.LabsDbContext db) : IPatientMergeParticipant
{
    public string Name => "Lab results";
    public Task<int> MoveAsync(Guid from, Guid to, CancellationToken ct) => db.Reports.Where(x => x.PatientId == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, to), ct);
}

public sealed class MessagingMerge(BrightPAS.Modules.Messaging.MsgDbContext db) : IPatientMergeParticipant
{
    public string Name => "Interface messages";
    public Task<int> MoveAsync(Guid from, Guid to, CancellationToken ct) => db.Messages.Where(x => x.PatientId == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, (Guid?)to), ct);
}

public sealed class ReferralsMerge(BrightPAS.Modules.Referrals.ReferralsDbContext db) : IPatientMergeParticipant
{
    public string Name => "Referrals & waitlist";
    public async Task<int> MoveAsync(Guid from, Guid to, CancellationToken ct) =>
        await db.Referrals.Where(x => x.PatientId == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, to), ct)
        + await db.Waitlist.Where(x => x.PatientId == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, to), ct);
}

public sealed class LettersMerge(BrightPAS.Modules.Letters.LettersDbContext db) : IPatientMergeParticipant
{
    public string Name => "Letters";
    public Task<int> MoveAsync(Guid from, Guid to, CancellationToken ct) => db.Letters.Where(x => x.PatientId == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, to), ct);
}

public sealed class RemindersMerge(BrightPAS.Modules.Reminders.RemindersDbContext db) : IPatientMergeParticipant
{
    public string Name => "SMS & email";
    public async Task<int> MoveAsync(Guid from, Guid to, CancellationToken ct)
    {
        var n = await db.Messages.Where(x => x.PatientId == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, (Guid?)to), ct)
            + await db.Replies.Where(x => x.PatientId == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, (Guid?)to), ct);
        // Contact preferences are keyed by patient: keep the survivor's; otherwise carry the duplicate's across.
        var dup = await db.Preferences.SingleOrDefaultAsync(x => x.PatientId == from, ct);
        if (dup is not null)
        {
            if (!await db.Preferences.AnyAsync(x => x.PatientId == to, ct))
            {
                var copy = db.Entry(dup).CurrentValues.Clone().ToObject() as BrightPAS.Modules.Reminders.ContactPreference;
                copy!.PatientId = to;
                db.Preferences.Add(copy);
            }
            db.Preferences.Remove(dup);
            await db.SaveChangesAsync(ct);
            n++;
        }
        return n;
    }
}

public sealed class PortalMerge(BrightPAS.Modules.Portal.PortalDbContext db) : IPatientMergeParticipant
{
    public string Name => "Portal";
    public async Task<int> MoveAsync(Guid from, Guid to, CancellationToken ct) =>
        await db.Requests.Where(x => x.PatientId == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, to), ct)
        + await db.Access.Where(x => x.PatientId == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, to), ct);
}

public sealed class AssetsMerge(BrightPAS.Modules.Assets.AssetsDbContext db) : IPatientMergeParticipant
{
    public string Name => "Sterile items, scopes, stock used and implants";
    public async Task<int> MoveAsync(Guid from, Guid to, CancellationToken ct) =>
        await db.Issues.Where(x => x.PatientId == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, (Guid?)to), ct)
        + await db.ScopeEvents.Where(x => x.PatientId == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, (Guid?)to), ct)
        + await db.Movements.Where(x => x.PatientId == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, (Guid?)to), ct)
        + await db.Implants.Where(x => x.PatientId == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, to), ct);
}

public sealed class EmrMerge(BrightPAS.Modules.Emr.EmrDbContext db) : IPatientMergeParticipant
{
    public string Name => "Clinical record (episodes, controlled drug register)";
    public async Task<int> MoveAsync(Guid from, Guid to, CancellationToken ct) =>
        await db.Episodes.Where(x => x.PatientId == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, to), ct)
        + await db.CdRegister.Where(x => x.PatientId == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, (Guid?)to), ct);
}

public sealed class EndoscopyMerge(BrightPAS.Modules.Endoscopy.EndoDbContext db) : IPatientMergeParticipant
{
    public string Name => "Endoscopy (procedures, pictures and videos)";
    public async Task<int> MoveAsync(Guid from, Guid to, CancellationToken ct) =>
        await db.Procedures.Where(x => x.PatientId == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, to), ct)
        + await db.Media.Where(x => x.PatientId == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, (Guid?)to), ct);
}

public sealed class ScribeMerge(BrightPAS.Modules.Scribe.ScribeDbContext db) : IPatientMergeParticipant
{
    public string Name => "Scribe (sessions and AI-recording consent)";
    public async Task<int> MoveAsync(Guid from, Guid to, CancellationToken ct) =>
        await db.Sessions.Where(x => x.PatientId == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, to), ct)
        + await db.Consents.Where(x => x.PatientId == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, to), ct);
}

public sealed class QualityMerge(BrightPAS.Modules.Quality.QualityDbContext db) : IPatientMergeParticipant
{
    public string Name => "Quality (incidents, complaints)";
    public async Task<int> MoveAsync(Guid from, Guid to, CancellationToken ct) =>
        await db.Incidents.Where(x => x.PatientId == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, to), ct)
        + await db.Complaints.Where(x => x.PatientId == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, to), ct);
}

public sealed class DevicesMerge(BrightPAS.Modules.Devices.DevicesDbContext db) : IPatientMergeParticipant
{
    public string Name => "Devices & SaferSleep (monitor connections, anaesthetic records)";
    public async Task<int> MoveAsync(Guid from, Guid to, CancellationToken ct) =>
        await db.Associations.Where(x => x.PatientId == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, to), ct)
        + await db.Records.Where(x => x.PatientId == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, to), ct);
}

public sealed class CodingMerge(BrightPAS.Modules.Coding.CodingDbContext db) : IPatientMergeParticipant
{
    public string Name => "Coding & statutory reporting (coded events, NBRS entries)";
    public async Task<int> MoveAsync(Guid from, Guid to, CancellationToken ct) =>
        await db.Episodes.Where(x => x.PatientId == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, to), ct)
        + await db.NbrsEntries.Where(x => x.PatientId == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, to), ct);
}

public sealed class EstimatesMerge(BrightPAS.Modules.Estimates.EstDbContext db) : IPatientMergeParticipant
{
    public string Name => "Procedure estimates";
    public Task<int> MoveAsync(Guid from, Guid to, CancellationToken ct) => db.Estimates.Where(x => x.PatientId == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, (Guid?)to), ct);
}
