using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Data;
using BrightPAS.Identity;
using BrightPAS.Modules.Portal;
using BrightPAS.Modules.Rostering;
using BrightPAS.Web.Api;

namespace BrightPAS.Web.Integration;

/// <summary>
/// Staff leaving TDS: one place to switch off everything – sign-in (all sessions ended), roster (future shifts removed and listed for cover,
/// staff record inactive, open swaps and leave closed), clinician register entry, and PA/specialist portal links. Fully audited.
/// </summary>
public sealed class OffboardingService(UserManager<PasUser> users, UserAdminService admin, RosterDbContext roster, PasDbContext core, PortalDbContext portal,
    AuditWriter audit, TimeProvider clock)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    public async Task<object?> PreviewAsync(Guid id)
    {
        var u = await users.FindByIdAsync(id.ToString());
        if (u is null) return null;
        var staff = await roster.Staff.AsNoTracking().Where(s => s.UserName == u.UserName).ToListAsync();
        var ids = staff.Select(s => s.Id).ToList();
        var shifts = await roster.Shifts.AsNoTracking().Where(s => ids.Contains(s.StaffId) && s.Date >= Today).OrderBy(s => s.Date).ToListAsync();
        var clinicians = await core.Clinicians.AsNoTracking().Where(c => c.UserName == u.UserName).Select(c => new { c.Id, c.Name, c.IsActive }).ToListAsync();
        var delegates = await portal.Delegates.AsNoTracking().CountAsync(d => d.UserName == u.UserName);
        return new
        {
            u.Id, u.UserName, u.DisplayName, u.IsActive,
            roles = await users.GetRolesAsync(u),
            staff = staff.Select(s => new { s.Id, s.Name, s.IsActive }),
            futureShifts = shifts.Select(s => new { s.Id, s.Date, s.Start, s.End, s.AllDay, s.Area }),
            clinicians, portalLinks = delegates,
        };
    }

    public async Task<(IReadOnlyList<string> Errors, IReadOnlyList<string> Done)> OffboardAsync(Guid id, PatientRecordsEndpoints.OffboardRequest r, string actor)
    {
        var u = await users.FindByIdAsync(id.ToString());
        if (u is null) return (["User not found."], []);
        if (string.Equals(u.UserName, actor, StringComparison.OrdinalIgnoreCase)) return (["You cannot offboard your own account."], []);
        var lastDay = r.LastDay ?? Today;
        var done = new List<string>();
        if (r.DeactivateAccount && u.IsActive)
        {
            var res = await admin.SetActiveAsync(id, false, actor);
            if (!res.Succeeded) return (res.Errors, done);
            done.Add("Sign-in switched off and all sessions ended.");
        }
        if (r.EndRoster)
        {
            var staff = await roster.Staff.Where(s => s.UserName == u.UserName).ToListAsync();
            var ids = staff.Select(s => s.Id).ToList();
            var future = await roster.Shifts.Where(s => ids.Contains(s.StaffId) && s.Date > lastDay).ToListAsync();
            if (future.Count > 0)
            {
                done.Add($"{future.Count} shift(s) after {lastDay:d MMM yyyy} removed – need cover: " + string.Join(", ", future.OrderBy(f => f.Date).Take(20).Select(f => $"{f.Date:ddd d MMM}{(f.AllDay ? "" : $" {f.Start:HH\\:mm}")}")) + (future.Count > 20 ? "…" : ""));
                var futureIds = future.Select(f => f.Id).ToList();
                roster.Swaps.RemoveRange(await roster.Swaps.Where(x => futureIds.Contains(x.ShiftId) || (x.OfferedShiftId != null && futureIds.Contains(x.OfferedShiftId.Value))).ToListAsync());
                roster.Shifts.RemoveRange(future);
            }
            var templates = await roster.Templates.Where(t => ids.Contains(t.StaffId) && t.IsActive).ToListAsync();
            foreach (var t in templates) { t.IsActive = false; t.EndsOn = lastDay; }
            if (templates.Count > 0) done.Add($"{templates.Count} regular shift(s) stopped.");
            foreach (var sw in await roster.Swaps.Where(x => (ids.Contains(x.FromStaffId) || (x.ToStaffId != null && ids.Contains(x.ToStaffId.Value))) && (x.Status == SwapStatus.Requested || x.Status == SwapStatus.Accepted)).ToListAsync())
            { sw.Status = SwapStatus.Cancelled; sw.DecidedBy = actor; sw.DecidedAt = clock.GetUtcNow(); sw.DecisionNote = "Staff member left"; }
            foreach (var l in await roster.Leave.Where(x => ids.Contains(x.StaffId) && x.Status == LeaveStatus.Requested).ToListAsync())
            { l.Status = LeaveStatus.Cancelled; l.DecidedBy = actor; l.DecidedAt = clock.GetUtcNow(); }
            foreach (var s in staff) s.IsActive = false;
            await roster.SaveChangesAsync();
            if (staff.Count > 0) done.Add("Roster record set inactive.");
        }
        if (r.DeactivateClinician)
        {
            var cl = await core.Clinicians.Where(c => c.UserName == u.UserName && c.IsActive).ToListAsync();
            foreach (var c in cl) c.IsActive = false;
            await core.SaveChangesAsync();
            if (cl.Count > 0) done.Add($"Clinician register: {string.Join(", ", cl.Select(c => c.Name))} set inactive (existing lists and letters keep the name).");
        }
        if (r.RemovePortalLinks)
        {
            var rows = await portal.Delegates.Where(d => d.UserName == u.UserName).ToListAsync();
            portal.Delegates.RemoveRange(rows);
            await portal.SaveChangesAsync();
            if (rows.Count > 0) done.Add($"{rows.Count} PA portal link(s) removed.");
        }
        await audit.WriteAsync(actor, "StaffOffboarded", "User", u.Id.ToString(), reason: $"{u.UserName}; last day {lastDay:yyyy-MM-dd}; {r.Reason}; " + string.Join(" ", done));
        if (done.Count == 0) done.Add("Nothing needed changing.");
        return ([], done);
    }
}
