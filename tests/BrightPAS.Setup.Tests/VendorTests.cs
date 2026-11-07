using System.Text.Json;
using BrightPAS.Setup.Install;

namespace BrightPAS.Setup.Tests;

public class VendorTests
{
    [Fact]
    public void Defaults_to_modica_and_keeps_earlier_choice()
    {
        var dir = Directory.CreateTempSubdirectory("bpas-set").FullName;
        WebPackage.WriteProductionSettings(dir, "Server=x", @"C:\keys");
        string Sms() => JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "appsettings.Production.json"))).RootElement.GetProperty("Reminders").GetProperty("SmsProvider").GetString()!;
        Assert.Equal("Modica", Sms());
        WebPackage.WriteProductionSettings(dir, "Server=x", @"C:\keys", "TestOutbox", null);
        Assert.Equal("TestOutbox", Sms());
        WebPackage.WriteProductionSettings(dir, "Server=x", @"C:\keys"); // upgrade without a new choice keeps it
        Assert.Equal("TestOutbox", Sms());
    }

    [Fact]
    public void Vendor_names_are_validated()
    {
        Assert.Equal("GenericHttp", MessagingVendors.Normalise("generichttp", MessagingVendors.Sms, "SMS vendor"));
        Assert.Throws<ArgumentException>(() => MessagingVendors.Normalise("Twilio", MessagingVendors.Sms, "SMS vendor"));
    }
}
