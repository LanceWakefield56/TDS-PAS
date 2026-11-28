using BrightPAS.Core.Audit;
using Microsoft.EntityFrameworkCore;

namespace BrightPAS.Modules.Messaging;

public sealed record TaskEdit(string Title, string? Detail, string Role, int? DueMinutes, string? EscalateToRole, WorkTaskPriority Priority = WorkTaskPriority.Normal, Guid? PatientId = null);
public sealed record TaskCounts(int Mine, int Overdue, int Escalated);

/// <summary>
/// People's tasks (0.35.0, ADR 0048): made by a flow's Task step or by staff. Everyone with the task's role sees it; one person can take it;
/// when it is overdue it moves to the escalation role (once) and is marked escalated. Done and cancelled tasks are kept with who and when.
/// </summary>
public sealed class WorkflowService(MsgDbContext db, MessagingService messages, AuditWriter audit, TimeProvider clock)
{
    public static readonly string[] Scopes = ["mine", "overdue", "all", "done"];

    public async Task<List<WorkTask>> ListAsync(string scope, IReadOnlyCollection<string> roles, string user, bool seeAll, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var q = db.Tasks.AsNoTracking();
        if (!seeAll) q = q.Where(t => roles.Contains(t.Role) || t.AssignedTo == user || t.CreatedBy == user);
        q = scope switch
        {
            "done" => q.Where(t => t.Status != WorkTaskStatus.Open).OrderByDescending(t => t.DoneAt),
            "overdue" => q.Where(t => t.Status == WorkTaskStatus.Open && t.DueAt < now).OrderBy(t => t.DueAt),
            "all" => q.Where(t => t.Status == WorkTaskStatus.Open).OrderBy(t => t.DueAt == null).ThenBy(t => t.DueAt),
            _ => q.Where(t => t.Status == WorkTaskStatus.Open && (t.AssignedTo == user || (t.AssignedTo == null && roles.Contains(t.Role))))
                .OrderByDescending(t => t.Priority).ThenBy(t => t.DueAt == null).ThenBy(t => t.DueAt),
        };
        return await q.Take(500).ToListAsync(ct);
    }

    public async Task<TaskCounts> CountsAsync(IReadOnlyCollection<string> roles, string user, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var open = db.Tasks.AsNoTracking().Where(t => t.Status == WorkTaskStatus.Open && (t.AssignedTo == user || (t.AssignedTo == null && roles.Contains(t.Role))));
        return new(await open.CountAsync(ct), await open.CountAsync(t => t.DueAt < now, ct), await open.CountAsync(t => t.EscalatedAt != null, ct));
    }

    public Task<WorkTask?> GetAsync(Guid id, CancellationToken ct = default) => db.Tasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);

    public async Task<(WorkTask? Task, IReadOnlyList<string> Errors)> CreateAsync(TaskEdit e, IReadOnlyCollection<string> knownRoles, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var title = (e.Title ?? "").Trim();
        if (title.Length is < 3 or > 200) errors.Add("Say what has to be done (3–200 characters).");
        if (!knownRoles.Contains(e.Role)) errors.Add("Choose who does it (a role).");
        if (e.DueMinutes is < 1 or > 43200) errors.Add("The time to do it is 1 minute to 30 days.");
        if (!string.IsNullOrWhiteSpace(e.EscalateToRole))
        {
            if (!knownRoles.Contains(e.EscalateToRole)) errors.Add("Choose a role to escalate to.");
            else if (e.EscalateToRole == e.Role) errors.Add("Escalate to a different role.");
            if (e.DueMinutes is null) errors.Add("Escalation needs a time to do it by.");
        }
        if (!Enum.IsDefined(e.Priority)) errors.Add("Choose the priority.");
        if (errors.Count > 0) return (null, errors);
        var s = await messages.SettingsAsync(ct);
        var now = clock.GetUtcNow();
        var t = new WorkTask
        {
            Number = $"T{s.NextTaskNumber++:D6}",
            Title = title,
            Detail = Cut(e.Detail, 2000),
            Role = e.Role,
            Priority = e.Priority,
            DueAt = e.DueMinutes is { } m ? now.AddMinutes(m) : null,
            EscalateToRole = string.IsNullOrWhiteSpace(e.EscalateToRole) ? null : e.EscalateToRole,
            PatientId = e.PatientId,
            Source = "Staff",
            CreatedAt = now,
            CreatedBy = user,
        };
        db.Tasks.Add(t);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "Task", t.Id.ToString(), t.PatientId?.ToString(), reason: $"{t.Number} for {t.Role}: {t.Title}", ct: ct);
        return (t, []);
    }

    private static bool Mine(WorkTask t, IReadOnlyCollection<string> roles, string user, bool seeAll) =>
        seeAll || t.AssignedTo == user || roles.Contains(t.Role) || t.CreatedBy == user;

    /// <summary>I'll do it: the task is assigned to me (or back to the role when release is true).</summary>
    public async Task<string?> TakeAsync(Guid id, bool release, IReadOnlyCollection<string> roles, string user, bool seeAll, CancellationToken ct = default)
    {
        var t = await db.Tasks.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (t is null || !Mine(t, roles, user, seeAll)) return "Task not found.";
        if (t.Status != WorkTaskStatus.Open) return "This task is already finished.";
        if (release)
        {
            if (t.AssignedTo != user) return "Only the person doing the task can hand it back.";
            t.AssignedTo = null;
        }
        else
        {
            if (t.AssignedTo is { } other && other != user) return $"{other} is already doing this task.";
            t.AssignedTo = user;
        }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, release ? "Release" : "Take", "Task", t.Id.ToString(), t.PatientId?.ToString(), reason: t.Number, ct: ct);
        return null;
    }

    public async Task<string?> FinishAsync(Guid id, bool done, string? outcome, IReadOnlyCollection<string> roles, string user, bool seeAll, CancellationToken ct = default)
    {
        var t = await db.Tasks.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (t is null || !Mine(t, roles, user, seeAll)) return "Task not found.";
        if (t.Status != WorkTaskStatus.Open) return "This task is already finished.";
        if (!done && string.IsNullOrWhiteSpace(outcome)) return "Say why the task is not needed.";
        t.Status = done ? WorkTaskStatus.Done : WorkTaskStatus.Cancelled; t.Outcome = Cut(outcome, 500); t.DoneAt = clock.GetUtcNow(); t.DoneBy = user;
        t.AssignedTo ??= user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, done ? "Done" : "Cancel", "Task", t.Id.ToString(), t.PatientId?.ToString(), reason: $"{t.Number} {t.Outcome}", ct: ct);
        return null;
    }

    /// <summary>Overdue open tasks with an escalation role move to that role (once). Run by the engine every cycle. Returns the tasks escalated.</summary>
    public async Task<List<WorkTask>> EscalateAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var list = await db.Tasks.Where(t => t.Status == WorkTaskStatus.Open && t.EscalatedAt == null && t.EscalateToRole != null && t.DueAt < now).Take(200).ToListAsync(ct);
        foreach (var t in list)
        {
            t.EscalatedFrom = t.Role; t.Role = t.EscalateToRole!; t.EscalatedAt = now; t.AssignedTo = null;
            t.Priority = WorkTaskPriority.Urgent;
        }
        await db.SaveChangesAsync(ct);
        foreach (var t in list) await audit.WriteAsync("system", "Escalate", "Task", t.Id.ToString(), t.PatientId?.ToString(), reason: $"{t.Number} {t.EscalatedFrom} → {t.Role}", ct: ct);
        return list;
    }

    private static string? Cut(string? s, int n) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Length > n ? s.Trim()[..n] : s.Trim();
}
