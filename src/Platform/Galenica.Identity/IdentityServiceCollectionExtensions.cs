using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Galenica.Identity;

public static class IdentityServiceCollectionExtensions
{
    /// <summary>Registers PAS identity: accounts, password/lockout policy, MFA, break-glass and first-admin set-up.</summary>
    public static IServiceCollection AddPasIdentity(this IServiceCollection services, Action<DbContextOptionsBuilder> configureDb)
    {
        services.AddDbContext<PasIdentityDbContext>(configureDb);
        services.AddDataProtection();
        services.AddIdentityCore<PasUser>(o =>
            {
                // NIST SP 800-63B: length over complexity.
                o.Password.RequiredLength = 12;
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequiredUniqueChars = 1;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.Lockout.AllowedForNewUsers = true;
                o.User.RequireUniqueEmail = false;
                o.SignIn.RequireConfirmedAccount = false;
            })
            .AddRoles<PasRole>()
            .AddEntityFrameworkStores<PasIdentityDbContext>()
            .AddDefaultTokenProviders()
            .AddPasswordValidator<PasPasswordValidator>();

        services.AddScoped<RoleSeeder>();
        services.AddScoped<PasClaimsFactory>();
        services.AddScoped<MfaService>();
        services.AddScoped<SignInService>();
        services.AddScoped<BreakGlassService>();
        services.AddScoped<FirstAdminService>();
        services.AddScoped<UserAdminService>();
        services.AddSingleton<IBreakGlassNotifier, NoOpBreakGlassNotifier>();
        return services;
    }
}
