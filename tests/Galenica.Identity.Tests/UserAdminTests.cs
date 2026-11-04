using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Galenica.Identity.Tests;

public class UserAdminTests
{
    private const string Pw = "correct horse battery staple";

    [Fact]
    public async Task Admin_creates_user_who_must_enrol_mfa_and_bad_input_is_refused()
    {
        await using var host = new TestHost();
        await using var scope = host.Scope();
        var admin = scope.ServiceProvider.GetRequiredService<UserAdminService>();
        var (ok, user) = await admin.CreateAsync("nurse.k", "Kiri Nurse", [DefaultRoles.Nurse], Pw, "admin");
        Assert.True(ok.Succeeded);
        Assert.False(user!.MfaEnabled);
        Assert.Equal(SignInStep.NeedsMfaEnrolment, await scope.ServiceProvider.GetRequiredService<SignInService>().CheckPasswordAsync("nurse.k", Pw));

        Assert.False((await admin.CreateAsync("nurse.k", "Dup", [DefaultRoles.Nurse], Pw, "admin")).Result.Succeeded);
        Assert.False((await admin.CreateAsync("x.y", "X", ["Not A Role"], Pw, "admin")).Result.Succeeded);
        Assert.False((await admin.CreateAsync("x.z", "X", [], Pw, "admin")).Result.Succeeded);
        Assert.False((await admin.CreateAsync("x.w", "X", [DefaultRoles.Nurse], "password1234", "admin")).Result.Succeeded);
    }

    [Fact]
    public async Task Last_administrator_is_protected_and_you_cannot_deactivate_yourself()
    {
        await using var host = new TestHost();
        await using var scope = host.Scope();
        var admin = scope.ServiceProvider.GetRequiredService<UserAdminService>();
        var (_, a) = await admin.CreateAsync("admin", "Admin", [DefaultRoles.SystemAdministrator], Pw, "setup");
        Assert.False((await admin.SetActiveAsync(a!.Id, false, "admin")).Succeeded);
        Assert.False((await admin.SetActiveAsync(a.Id, false, "someone.else")).Succeeded);
        Assert.False((await admin.SetRolesAsync(a.Id, [DefaultRoles.Nurse], "someone.else")).Succeeded);

        var (_, b) = await admin.CreateAsync("admin2", "Admin Two", [DefaultRoles.SystemAdministrator], Pw, "admin");
        Assert.True((await admin.SetActiveAsync(a.Id, false, "admin2")).Succeeded);
        Assert.Equal(SignInStep.Inactive, await scope.ServiceProvider.GetRequiredService<SignInService>().CheckPasswordAsync("admin", Pw));
        Assert.True((await admin.SetActiveAsync(a.Id, true, "admin2")).Succeeded);
        Assert.NotNull(b);
    }

    [Fact]
    public async Task Reset_mfa_forces_re_enrolment_and_unlock_clears_lockout()
    {
        await using var host = new TestHost();
        await using var scope = host.Scope();
        var admin = scope.ServiceProvider.GetRequiredService<UserAdminService>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<PasUser>>();
        var mfa = scope.ServiceProvider.GetRequiredService<MfaService>();
        var signIn = scope.ServiceProvider.GetRequiredService<SignInService>();
        var (_, s) = await admin.CreateAsync("rec.a", "Reception A", [DefaultRoles.Reception], Pw, "admin");
        var u = (await users.FindByIdAsync(s!.Id.ToString()))!;
        var e = await mfa.BeginTotpEnrolmentAsync(u);
        await mfa.CompleteTotpEnrolmentAsync(u, TestAuthenticator.Code(e.SharedKey));
        Assert.Equal(SignInStep.NeedsMfaCode, await signIn.CheckPasswordAsync("rec.a", Pw));

        Assert.True((await admin.ResetMfaAsync(s.Id, "admin")).Succeeded);
        Assert.Equal(SignInStep.NeedsMfaEnrolment, await signIn.CheckPasswordAsync("rec.a", Pw));

        for (var i = 0; i < 5; i++) await signIn.CheckPasswordAsync("rec.a", "wrong wrong wrong");
        Assert.Equal(SignInStep.LockedOut, await signIn.CheckPasswordAsync("rec.a", Pw));
        Assert.True((await admin.UnlockAsync(s.Id, "admin")).Succeeded);
        Assert.Equal(SignInStep.NeedsMfaEnrolment, await signIn.CheckPasswordAsync("rec.a", Pw));

        Assert.True((await admin.ResetPasswordAsync(s.Id, "a brand new long passphrase", "admin")).Succeeded);
        Assert.Equal(SignInStep.Failed, await signIn.CheckPasswordAsync("rec.a", Pw));
    }
}
