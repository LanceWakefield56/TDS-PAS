using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;

namespace BrightPAS.Identity;

public sealed record RoleView(string Name, string? Description, bool IsDefault, int Users, IReadOnlyList<string> Permissions);

/// <summary>Lets System Administrators change which permissions each role has, and add custom roles. Every change is audited.</summary>
public sealed class RoleAdminService(RoleManager<PasRole> roles, UserManager<PasUser> users, AuditWriter audit)
{
    /// <summary>Permissions the System Administrator role must always keep, so nobody can lock the system out of its own administration.</summary>
    public static readonly string[] AdminMustKeep = [Permissions.UsersManage, Permissions.ModulesManage];

    /// <summary>Plain-language description of each permission for the role editor.</summary>
    public static readonly IReadOnlyDictionary<string, (string Group, string Text)> Catalogue = new Dictionary<string, (string, string)>
    {
        [Permissions.PatientView] = ("Patients", "View patient records"),
        [Permissions.PatientRegister] = ("Patients", "Register new patients"),
        [Permissions.PatientEdit] = ("Patients", "Edit patient details"),
        [Permissions.PatientMerge] = ("Patients", "Approve identity corrections and patient merges (second-person check)"),
        [Permissions.PatientAlertsManage] = ("Patients", "Add and remove clinical alerts"),
        [Permissions.BreakGlass] = ("Privacy", "Emergency access (break-glass)"),
        [Permissions.BreakGlassReview] = ("Privacy", "Review emergency access"),
        [Permissions.AuditView] = ("Privacy", "View the audit log"),
        [Permissions.UsersManage] = ("Administration", "Manage user accounts and roles"),
        [Permissions.OrganisationManage] = ("Administration", "Manage clinic details, rooms, clinicians and procedures"),
        [Permissions.ModulesManage] = ("Administration", "Licence and module set-up"),
        [Permissions.IntegrationManage] = ("Administration", "Interfaces, message flows, mappings and translation tables (change)"),
        [Permissions.MessagingView] = ("Messaging", "See the messaging dashboard, flows and messages (content views are audited)"),
        [Permissions.EmrView] = ("Clinical record", "Read the perioperative and clinical record"),
        [Permissions.EmrWrite] = ("Clinical record", "Record assessments, checklist, intra-op, counts, observations, medicines, notes and discharge"),
        [Permissions.EmrControlledDrugs] = ("Clinical record", "Keep the controlled drug register (receive, waste, stock check)"),
        [Permissions.AssetsView] = ("Assets & CSSD", "See equipment, trays, loads, scopes and stock"),
        [Permissions.AssetsManage] = ("Assets & CSSD", "Equipment register, maintenance, lockout, tray catalogue, suppliers"),
        [Permissions.CssdProcess] = ("Assets & CSSD", "Record washer/steriliser/AER loads, pack, issue and return trays, scope events, loan sets"),
        [Permissions.CssdRelease] = ("Assets & CSSD", "Release or fail steriliser loads (parametric release), recalls"),
        [Permissions.StockManage] = ("Assets & CSSD", "Use and adjust stock, record implants, draft and receive orders"),
        [Permissions.PurchasingApprove] = ("Assets & CSSD", "Approve purchase orders, match supplier invoices, export bills to Xero"),
        [Permissions.LabsView] = ("Lab results", "View lab results"),
        [Permissions.LabsAcknowledge] = ("Lab results", "Acknowledge lab results"),
        [Permissions.LabsMatch] = ("Lab results", "Match unmatched results to patients"),
        [Permissions.TheatreView] = ("Theatre", "View theatre lists"),
        [Permissions.TheatreBook] = ("Theatre", "Book patients"),
        [Permissions.TheatreManage] = ("Theatre", "Create lists and list templates"),
        [Permissions.FormsSend] = ("Pre-admission forms", "Send questionnaires"),
        [Permissions.FormsTriage] = ("Pre-admission forms", "Triage questionnaires"),
        [Permissions.BillingView] = ("Billing", "View estimates and invoices"),
        [Permissions.BillingManage] = ("Billing", "Create estimates, invoices and payments"),
        [Permissions.BillingFees] = ("Billing", "Fee schedule and billing settings"),
        [Permissions.RemindersView] = ("SMS & email", "Outbox and replies"),
        [Permissions.RemindersManage] = ("SMS & email", "Wording and vendor settings"),
        [Permissions.ReferralsView] = ("Referrals & waitlist", "View referrals and the waitlist"),
        [Permissions.ReferralsManage] = ("Referrals & waitlist", "Record referrals and manage the waitlist"),
        [Permissions.ReferralsTriage] = ("Referrals & waitlist", "Triage referrals (accept/decline)"),
        [Permissions.LettersView] = ("Letters", "View letters"),
        [Permissions.LettersWrite] = ("Letters", "Write, finalise, print and send letters"),
        [Permissions.LettersTemplates] = ("Letters", "Edit letter templates and GP practices"),
        [Permissions.RosterView] = ("Rostering", "See the staff roster, request own leave and shift swaps"),
        [Permissions.RosterManage] = ("Rostering", "Write the roster, staff list, approve leave and swaps, export payroll"),
        [Permissions.CredentialingView] = ("Credentialing", "See clinician credentials and expiry alerts"),
        [Permissions.CredentialingManage] = ("Credentialing", "Record credentials, scope of practice and credentialing settings"),
        [Permissions.PortalSpecialist] = ("Portals", "Specialist/PA portal: own patients' lists, appointments, waitlist and documents"),
        [Permissions.PortalManage] = ("Portals", "Send patient portal links and answer patient requests"),
    };

    public async Task<IReadOnlyList<RoleView>> ListAsync()
    {
        var list = new List<RoleView>();
        foreach (var r in await roles.Roles.OrderBy(r => r.Name).ToListAsync())
        {
            var perms = (await roles.GetClaimsAsync(r)).Where(c => c.Type == Permissions.ClaimType).Select(c => c.Value).Order().ToList();
            list.Add(new RoleView(r.Name!, r.Description, DefaultRoles.Definitions.ContainsKey(r.Name!), (await users.GetUsersInRoleAsync(r.Name!)).Count, perms));
        }
        return list;
    }

    public async Task<AdminResult> SetPermissionsAsync(string roleName, IReadOnlyList<string> wanted, string actor)
    {
        var role = await roles.FindByNameAsync(roleName);
        if (role is null) return AdminResult.Fail("Role not found.");
        var unknown = wanted.Where(p => !Permissions.All.Contains(p)).ToList();
        if (unknown.Count > 0) return AdminResult.Fail($"Unknown permission(s): {string.Join(", ", unknown)}.");
        if (roleName == DefaultRoles.SystemAdministrator && AdminMustKeep.Any(p => !wanted.Contains(p)))
            return AdminResult.Fail("The System Administrator role must keep 'Manage user accounts and roles' and 'Licence and module set-up'.");
        var claims = await roles.GetClaimsAsync(role);
        var current = claims.Where(c => c.Type == Permissions.ClaimType).ToList();
        var removed = current.Where(c => !wanted.Contains(c.Value)).ToList();
        var added = wanted.Distinct().Where(p => current.All(c => c.Value != p)).ToList();
        foreach (var c in removed) Check(await roles.RemoveClaimAsync(role, c));
        foreach (var p in added) Check(await roles.AddClaimAsync(role, new Claim(Permissions.ClaimType, p)));
        if (removed.Count + added.Count > 0)
            await audit.WriteAsync(actor, "RolePermissions", "Role", roleName,
                reason: $"added: {(added.Count == 0 ? "-" : string.Join(",", added))}; removed: {(removed.Count == 0 ? "-" : string.Join(",", removed.Select(c => c.Value)))}");
        return AdminResult.Ok;
    }

    public async Task<AdminResult> CreateAsync(string name, string? description, string actor)
    {
        name = name?.Trim() ?? "";
        if (name.Length < 3 || name.Length > 60) return AdminResult.Fail("Role name must be 3 to 60 characters.");
        if (await roles.RoleExistsAsync(name)) return AdminResult.Fail("A role with that name already exists.");
        Check(await roles.CreateAsync(new PasRole { Name = name, Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim() }));
        await audit.WriteAsync(actor, "Create", "Role", name);
        return AdminResult.Ok;
    }

    private static void Check(IdentityResult r)
    {
        if (!r.Succeeded) throw new InvalidOperationException(string.Join("; ", r.Errors.Select(e => e.Description)));
    }
}
