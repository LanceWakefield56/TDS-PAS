using Tds.Pas.Core.Patients;

namespace Tds.Pas.Core.Tests;

public class NhiValidatorTests
{
    [Theory]
    [InlineData("WLD9413", NhiFormat.Old)]   // published example of a valid old-format NHI
    [InlineData("ABC1235", NhiFormat.Old)]
    [InlineData("zsc21tn", NhiFormat.New)]   // published example (lower case accepted)
    [InlineData("ZVB97XQ", NhiFormat.New)]
    [InlineData(" WLD9413 ", NhiFormat.Old)] // surrounding spaces trimmed
    public void Valid_numbers_pass(string nhi, NhiFormat format)
    {
        var r = NhiValidator.Validate(nhi);
        Assert.True(r.IsValid, r.Error);
        Assert.Equal(format, r.Format);
    }

    [Theory]
    [InlineData("ZZZ0044")]  // checksum 0 → invalid
    [InlineData("ZZZ00AA")]  // wrong check letter
    [InlineData("WLD9414")]  // wrong check digit
    [InlineData("IAB1234")]  // I not allowed
    [InlineData("OAB1234")]  // O not allowed
    [InlineData("AB12345")]  // digit in letter position
    [InlineData("ABC12D")]   // too short
    [InlineData("ABC12345")] // too long
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_numbers_fail(string? nhi)
    {
        var r = NhiValidator.Validate(nhi);
        Assert.False(r.IsValid);
        Assert.False(string.IsNullOrEmpty(r.Error));
    }

    [Fact]
    public void Numbers_starting_with_Z_are_flagged_as_test_numbers()
    {
        var r = NhiValidator.Validate("ZSC21TN");
        Assert.True(r.IsValid);
        Assert.True(r.IsTestNumber);
        Assert.False(NhiValidator.Validate("WLD9413").IsTestNumber);
    }
}
