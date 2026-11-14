using System.Security.Cryptography;
using BrightPAS.Licensing;

namespace BrightPAS.Licensing.Tests;

public class LicensingTests
{
    private static readonly ECDsa Vendor = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static readonly ECDsa Pirate = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static readonly IReadOnlyList<TrustedKey> Keys = [new("test", Vendor, false)];
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 10, 0, 0, TimeSpan.FromHours(13));
    private const string Code = "ABCDE-FGHJK-LMNPQ-RSTUV";

    private static LicenceDocument Doc(Func<LicenceDocument, LicenceDocument>? f = null)
    {
        var d = new LicenceDocument { LicenceId = "BP-1", Customer = "Test Clinic", IssuedAt = Now, ExpiresOn = new DateOnly(2027, 6, 30), InstallationCode = Code, Modules = ["labs", "theatre"] };
        return f is null ? d : f(d);
    }

    private static LicenceStatus Eval(string? text, DateTimeOffset? now = null, string code = Code, DateTimeOffset? lastSeen = null, string? activation = null, string[]? lastKnown = null) =>
        LicenceEvaluator.Evaluate(new(text, code, now ?? Now, lastSeen, activation, Now, lastKnown), Keys);

    [Fact]
    public void Valid_licence_enables_only_its_modules()
    {
        var s = Eval(LicenceCodec.Sign(Doc(), Vendor));
        Assert.Equal(LicenceHealth.Valid, s.Health);
        Assert.Equal(ModuleAccess.Full, s.AccessTo("labs"));
        Assert.Equal(ModuleAccess.Full, s.AccessTo("theatre"));
        Assert.Equal(ModuleAccess.None, s.AccessTo("billing"));
    }

    [Fact]
    public void Development_key_is_trusted_only_in_builds_without_a_production_key_unless_set_explicitly()
    {
        Assert.DoesNotContain(VendorKeys.BuiltIn(allowDevelopmentKey: false), k => k.IsDevelopment);
        Assert.Contains(VendorKeys.BuiltIn(allowDevelopmentKey: true), k => k.IsDevelopment);
        // Default: the development key's private half is in the source, so a build with the production key must not trust it.
        Assert.Equal(!VendorKeys.HasProductionKey, VendorKeys.BuiltIn().Any(k => k.IsDevelopment));
    }

    [Fact]
    public void Edited_licence_is_rejected()
    {
        var text = LicenceCodec.Sign(Doc(), Vendor);
        var body = text.Split('\n').First(l => l.StartsWith("eyJ", StringComparison.Ordinal));
        var tampered = text.Replace(body, body[..10] + (body[10] == 'A' ? 'B' : 'A') + body[11..]);
        var s = Eval(tampered);
        Assert.Equal(LicenceHealth.Invalid, s.Health);
        Assert.Equal(ModuleAccess.None, s.AccessTo("labs"));
    }

    [Fact]
    public void Adding_a_module_by_editing_the_json_is_detected()
    {
        // Re-encode the payload with an extra module but keep the original signature.
        var text = LicenceCodec.Sign(Doc(), Vendor);
        var forged = LicenceCodec.Sign(Doc(d => d with { Modules = ["*"] }), Pirate);
        var origSig = string.Concat(text.Split('\n').Where(l => !l.StartsWith('#') && !l.StartsWith("-----", StringComparison.Ordinal))).Split('.')[1];
        var forgedPayload = string.Concat(forged.Split('\n').Where(l => !l.StartsWith('#') && !l.StartsWith("-----", StringComparison.Ordinal))).Split('.')[0];
        var franken = $"-----BEGIN BRIGHTPAS LICENCE-----\n{forgedPayload}.{origSig}\n-----END BRIGHTPAS LICENCE-----\n";
        Assert.Equal(LicenceHealth.Invalid, Eval(franken).Health);
    }

    [Fact]
    public void Licence_signed_with_another_key_is_rejected() =>
        Assert.Equal(LicenceHealth.Invalid, Eval(LicenceCodec.Sign(Doc(), Pirate)).Health);

    [Fact]
    public void Wrong_installation_makes_modules_read_only_not_hidden()
    {
        var s = Eval(LicenceCodec.Sign(Doc(), Vendor), code: "ZZZZZ-ZZZZZ-ZZZZZ-ZZZZZ");
        Assert.Equal(LicenceHealth.WrongInstallation, s.Health);
        Assert.Equal(ModuleAccess.ReadOnly, s.AccessTo("labs"));
    }

    [Fact]
    public void Installation_code_match_ignores_dashes_and_case() =>
        Assert.Equal(LicenceHealth.Valid, Eval(LicenceCodec.Sign(Doc(), Vendor), code: "abcdefghjklmnpqrstuv").Health);

    [Fact]
    public void Expiry_warning_grace_then_read_only()
    {
        var text = LicenceCodec.Sign(Doc(), Vendor);
        Assert.Equal(LicenceHealth.ExpiringSoon, Eval(text, new DateTimeOffset(2027, 6, 15, 9, 0, 0, TimeSpan.FromHours(12))).Health);
        var grace = Eval(text, new DateTimeOffset(2027, 7, 5, 9, 0, 0, TimeSpan.FromHours(12)));
        Assert.Equal(LicenceHealth.InGrace, grace.Health);
        Assert.Equal(ModuleAccess.Full, grace.AccessTo("labs"));
        var expired = Eval(text, new DateTimeOffset(2027, 7, 20, 9, 0, 0, TimeSpan.FromHours(12)));
        Assert.Equal(LicenceHealth.Expired, expired.Health);
        Assert.Equal(ModuleAccess.ReadOnly, expired.AccessTo("labs"));
        Assert.Equal(ModuleAccess.None, expired.AccessTo("billing"));
    }

    [Fact]
    public void Winding_the_clock_back_is_detected()
    {
        var s = Eval(LicenceCodec.Sign(Doc(), Vendor), lastSeen: Now.AddDays(5));
        Assert.Equal(LicenceHealth.ClockProblem, s.Health);
        Assert.Equal(ModuleAccess.ReadOnly, s.AccessTo("labs"));
    }

    [Fact]
    public void Missing_or_invalid_licence_keeps_last_known_modules_viewable()
    {
        Assert.Equal(ModuleAccess.ReadOnly, Eval(null, lastKnown: ["labs"]).AccessTo("labs"));
        Assert.Equal(ModuleAccess.None, Eval(null).AccessTo("labs"));
        Assert.Equal(ModuleAccess.ReadOnly, Eval("garbage", lastKnown: ["labs"]).AccessTo("labs"));
    }

    [Fact]
    public void All_modules_wildcard()
    {
        var s = Eval(LicenceCodec.Sign(Doc(d => d with { Modules = ["*"] }), Vendor));
        Assert.All(ProductModules.All, m => Assert.Equal(ModuleAccess.Full, s.AccessTo(m.Id)));
    }

    [Fact]
    public void Online_activation_grace_then_read_only_and_token_restores()
    {
        var doc = Doc(d => d with { Activation = new ActivationSettings { Url = "https://lic.example", OfflineGraceDays = 30 } });
        var text = LicenceCodec.Sign(doc, Vendor);
        Assert.Equal(ModuleAccess.Full, Eval(text, Now.AddDays(10)).AccessTo("labs"));
        Assert.Equal(LicenceHealth.ActivationOverdue, Eval(text, Now.AddDays(31)).Health);
        var token = LicenceCodec.Sign(new ActivationToken { LicenceId = "BP-1", InstallationCode = Code, IssuedAt = Now.AddDays(30), ValidUntil = Now.AddDays(44) }, Vendor);
        var s = Eval(text, Now.AddDays(31), activation: token);
        Assert.Equal(LicenceHealth.Valid, s.Health);
        var otherInstall = LicenceCodec.Sign(new ActivationToken { LicenceId = "BP-1", InstallationCode = "ZZZZZ-ZZZZZ-ZZZZZ-ZZZZZ", IssuedAt = Now.AddDays(30), ValidUntil = Now.AddDays(44) }, Vendor);
        Assert.Equal(LicenceHealth.ActivationOverdue, Eval(text, Now.AddDays(31), activation: otherInstall).Health);
    }

    [Fact]
    public void Installation_code_is_stable_and_differs_per_machine()
    {
        var id = Guid.NewGuid();
        var a = InstallationIdentity.Code(id, "machine-1");
        Assert.Equal(a, InstallationIdentity.Code(id, "MACHINE-1"));
        Assert.NotEqual(a, InstallationIdentity.Code(id, "machine-2"));
        Assert.NotEqual(a, InstallationIdentity.Code(Guid.NewGuid(), "machine-1"));
        Assert.Matches("^[A-Z2-9]{5}-[A-Z2-9]{5}-[A-Z2-9]{5}-[A-Z2-9]{5}$", a);
    }

    [Fact]
    public void Shipped_sandbox_licence_verifies_with_the_built_in_development_key()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "../../../../../licensing/sandbox/BrightPAS-Sandbox-Licence.lic");
        var r = LicenceCodec.VerifyLicence(File.ReadAllText(path), VendorKeys.BuiltIn());
        Assert.True(r.Ok, r.Error);
        Assert.True(r.Key!.IsDevelopment);
        Assert.Null(r.Value!.InstallationCode);
        Assert.DoesNotContain(VendorKeys.BuiltIn(allowDevelopmentKey: false), k => k.IsDevelopment);
    }
}
