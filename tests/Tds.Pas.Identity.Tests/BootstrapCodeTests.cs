using Tds.Pas.Shared;

namespace Tds.Pas.Identity.Tests;

public class BootstrapCodeTests
{
    [Fact]
    public void Generated_code_has_four_groups_of_unambiguous_characters()
    {
        var code = BootstrapCode.Generate();
        Assert.Matches("^[A-HJKMNP-Z2-9]{4}(-[A-HJKMNP-Z2-9]{4}){3}$", code);
    }

    [Fact]
    public void Hash_verifies_ignoring_case_spaces_and_dashes_and_rejects_others()
    {
        var code = BootstrapCode.Generate();
        var hash = BootstrapCode.Hash(code);
        Assert.DoesNotContain(BootstrapCode.Normalise(code), hash);
        Assert.True(BootstrapCode.Verify(code.ToLowerInvariant().Replace("-", " "), hash));
        Assert.False(BootstrapCode.Verify(BootstrapCode.Generate(), hash));
    }
}
