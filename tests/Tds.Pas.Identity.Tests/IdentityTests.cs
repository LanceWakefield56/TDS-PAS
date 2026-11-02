using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tds.Pas.Core.Audit;
using Tds.Pas.Core.Data;
using Tds.Pas.Core.Setup;
using Tds.Pas.Shared;

namespace Tds.Pas.Identity.Tests;

public class IdentityTests
{
    private const string GoodPassword = "correct horse battery staple";

    private static async Task<PasUser> CreateUserAsync(AsyncServiceScope scope, string name, string role = DefaultRoles.Nurse)
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<PasUser>>();
        var user = new PasUser { UserName = name, DisplayName = name };
        var r = await users.CreateAsync(user, GoodPassword);
        Assert.True(r.Succeeded, string.Join("; ", r.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(user, role);
        return user;
    }

    private static async Task EnrolAsync(AsyncServiceScope scope, PasUser user)
    {
        var mfa = scope.ServiceProvider.GetRequiredService<MfaService>();
        var enrol = await mfa.BeginTotpEnrolmentAsync(user);
        Assert.NotNull(await mfa.CompleteTotpEnrolmentAsync(user, TestAuthenticator.Code(enrol.SharedKey)));
    }

    [Fact]
    public async Task Default_roles_are_created_with_permissions()
    {
        await using var host = new TestHost();
        await using var scope = host.Scope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<PasRole>>();
        Assert.Equal(DefaultRoles.Definitions.Count, await roles.Roles.CountAsync());
        var privacy = await roles.FindByNameAsync(DefaultRoles.PrivacyOfficer);
        var perms = (await roles.GetClaimsAsync(privacy!)).Select(c => c.Value).ToList();
        Assert.Contains(Permissions.BreakGlassReview, perms);
        // Running the seeder again changes nothing.
        await scope.ServiceProvider.GetRequiredService<RoleSeeder>().EnsureDefaultRolesAsync();
        Assert.Equal(perms.Count, (await roles.GetClaimsAsync(privacy!)).Count);
    }

    [Theory]
    [InlineData("short")]                         // too short
    [InlineData("password123456")]                // common word + digits
    [InlineData("Password1234")]                  // common
    [InlineData("tasmansurgery1")]                // organisation name
    [InlineData("aaaaaaaaaaaaaaa")]               // repetitive
    public async Task Weak_passwords_are_rejected(string password)
    {
        await using var host = new TestHost();
        await using var scope = host.Scope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<PasUser>>();
        var r = await users.CreateAsync(new PasUser { UserName = "jbloggs", DisplayName = "J Bloggs" }, password);
        Assert.False(r.Succeeded);
    }

    [Fact]
    public async Task Password_containing_username_is_rejected()
    {
        await using var host = new TestHost();
        await using var scope = host.Scope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<PasUser>>();
        var r = await users.CreateAsync(new PasUser { UserName = "rgreen", DisplayName = "R Green" }, "my name is rgreen ok");
        Assert.Contains(r.Errors, e => e.Code == "PasswordContainsUserName");
    }

    [Fact]
    public async Task Account_without_mfa_can_only_enrol()
    {
        await using var host = new TestHost();
        await using var scope = host.Scope();
        await CreateUserAsync(scope, "nurse.a");
        var signIn = scope.ServiceProvider.GetRequiredService<SignInService>();
        Assert.Equal(SignInStep.NeedsMfaEnrolment, await signIn.CheckPasswordAsync("nurse.a", GoodPassword));
        Assert.Equal(SignInStep.Failed, await signIn.CheckMfaAsync("nurse.a", "123456"));
    }

    [Fact]
    public async Task Full_sign_in_with_password_and_authenticator_code_succeeds_and_is_audited()
    {
        await using var host = new TestHost();
        await using var scope = host.Scope();
        var user = await CreateUserAsync(scope, "nurse.b");
        var mfa = scope.ServiceProvider.GetRequiredService<MfaService>();
        var enrol = await mfa.BeginTotpEnrolmentAsync(user);
        Assert.StartsWith("otpauth://totp/TDS%20PAS", enrol.OtpAuthUri);
        var recovery = await mfa.CompleteTotpEnrolmentAsync(user, TestAuthenticator.Code(enrol.SharedKey));
        Assert.Equal(10, recovery!.Count);

        var signIn = scope.ServiceProvider.GetRequiredService<SignInService>();
        Assert.Equal(SignInStep.NeedsMfaCode, await signIn.CheckPasswordAsync("nurse.b", GoodPassword));
        Assert.Equal(SignInStep.Success, await signIn.CheckMfaAsync("nurse.b", TestAuthenticator.Code(enrol.SharedKey), "PACU-01"));

        var core = scope.ServiceProvider.GetRequiredService<PasDbContext>();
        var events = await core.AuditEvents.OrderBy(e => e.Sequence).ToListAsync();
        Assert.Contains(events, e => e.Action == "Login" && e.UserId == "nurse.b" && e.Workstation == "PACU-01");
        Assert.Null(AuditChain.FindFirstBreak(events));
    }

    [Fact]
    public async Task Recovery_code_works_once()
    {
        await using var host = new TestHost();
        await using var scope = host.Scope();
        var user = await CreateUserAsync(scope, "nurse.c");
        var mfa = scope.ServiceProvider.GetRequiredService<MfaService>();
        var enrol = await mfa.BeginTotpEnrolmentAsync(user);
        var codes = await mfa.CompleteTotpEnrolmentAsync(user, TestAuthenticator.Code(enrol.SharedKey));
        var signIn = scope.ServiceProvider.GetRequiredService<SignInService>();
        Assert.Equal(SignInStep.Success, await signIn.CheckMfaAsync("nurse.c", codes![0]));
        Assert.Equal(SignInStep.Failed, await signIn.CheckMfaAsync("nurse.c", codes[0]));
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account()
    {
        await using var host = new TestHost();
        await using var scope = host.Scope();
        var user = await CreateUserAsync(scope, "reception.a", DefaultRoles.Reception);
        await EnrolAsync(scope, user);
        var signIn = scope.ServiceProvider.GetRequiredService<SignInService>();
        for (var i = 0; i < 4; i++)
            Assert.Equal(SignInStep.Failed, await signIn.CheckPasswordAsync("reception.a", "wrong password guess"));
        Assert.Equal(SignInStep.LockedOut, await signIn.CheckPasswordAsync("reception.a", "wrong password guess"));
        Assert.Equal(SignInStep.LockedOut, await signIn.CheckPasswordAsync("reception.a", GoodPassword));
    }

    [Fact]
    public async Task Inactive_account_cannot_sign_in()
    {
        await using var host = new TestHost();
        await using var scope = host.Scope();
        var user = await CreateUserAsync(scope, "leaver");
        user.IsActive = false;
        await scope.ServiceProvider.GetRequiredService<UserManager<PasUser>>().UpdateAsync(user);
        Assert.Equal(SignInStep.Inactive, await scope.ServiceProvider.GetRequiredService<SignInService>().CheckPasswordAsync("leaver", GoodPassword));
    }

    [Fact]
    public async Task Break_glass_needs_a_reason_expires_after_30_minutes_and_is_audited_and_reviewed()
    {
        await using var host = new TestHost();
        await using var scope = host.Scope();
        var user = await CreateUserAsync(scope, "anaes.a", DefaultRoles.Anaesthetist);
        var bg = scope.ServiceProvider.GetRequiredService<BreakGlassService>();

        await Assert.ThrowsAsync<ArgumentException>(() => bg.GrantAsync(user, "P42", "urgent"));
        var grant = await bg.GrantAsync(user, "P42", "Deteriorating patient in PACU, need history");
        Assert.True(await bg.HasActiveGrantAsync(user.Id, "P42"));
        Assert.False(await bg.HasActiveGrantAsync(user.Id, "P43"));

        host.Clock.Advance(TimeSpan.FromMinutes(31));
        Assert.False(await bg.HasActiveGrantAsync(user.Id, "P42"));

        var core = scope.ServiceProvider.GetRequiredService<PasDbContext>();
        Assert.Contains(await core.AuditEvents.ToListAsync(), e => e.Action == "BreakGlass" && e.PatientId == "P42");

        Assert.Single(await bg.PendingReviewsAsync());
        await bg.ReviewAsync(grant.Id, "privacy.officer", BreakGlassReviewStatus.Appropriate, "Clinically justified");
        Assert.Empty(await bg.PendingReviewsAsync());
    }

    [Fact]
    public async Task First_administrator_is_created_with_the_installer_code_and_mfa()
    {
        await using var host = new TestHost();
        await using var scope = host.Scope();
        var code = BootstrapCode.Generate();
        var core = scope.ServiceProvider.GetRequiredService<PasDbContext>();
        core.SetupState.Add(new SetupState { BootstrapCodeHash = BootstrapCode.Hash(code), BootstrapCodeCreatedAt = host.Clock.GetUtcNow() });
        await core.SaveChangesAsync();
        var svc = scope.ServiceProvider.GetRequiredService<FirstAdminService>();

        Assert.Equal(FirstAdminError.InvalidCode, (await svc.StartAsync("AAAA-BBBB-CCCC-DDDD", "admin.lance", "Lance", GoodPassword)).Error);

        var start = await svc.StartAsync(code.ToLowerInvariant(), "admin.lance", "Lance", GoodPassword);
        Assert.True(start.Succeeded);
        Assert.Equal(FirstAdminError.InvalidMfaCode, (await svc.CompleteAsync(code, "admin.lance", "000000")).Error);

        var done = await svc.CompleteAsync(code, "admin.lance", TestAuthenticator.Code(start.Enrolment!.SharedKey));
        Assert.True(done.Succeeded);
        Assert.Equal(10, done.RecoveryCodes!.Count);

        // The code cannot be used again.
        Assert.Equal(FirstAdminError.AlreadyCompleted, (await svc.StartAsync(code, "admin.other", "Other", GoodPassword)).Error);
    }

    [Fact]
    public async Task Installer_code_expires_after_seven_days()
    {
        await using var host = new TestHost();
        await using var scope = host.Scope();
        var code = BootstrapCode.Generate();
        var core = scope.ServiceProvider.GetRequiredService<PasDbContext>();
        core.SetupState.Add(new SetupState { BootstrapCodeHash = BootstrapCode.Hash(code), BootstrapCodeCreatedAt = host.Clock.GetUtcNow() });
        await core.SaveChangesAsync();
        host.Clock.Advance(TimeSpan.FromDays(8));
        var r = await scope.ServiceProvider.GetRequiredService<FirstAdminService>().StartAsync(code, "admin.lance", "Lance", GoodPassword);
        Assert.Equal(FirstAdminError.CodeExpired, r.Error);
    }
}
