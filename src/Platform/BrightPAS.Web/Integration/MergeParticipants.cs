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
        var n = await db.Messages.Where(x => x.PatientId == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, to), ct)
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
