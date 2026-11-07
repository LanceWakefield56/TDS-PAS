using BrightPAS.Licensing;
using BrightPAS.Setup.Security;

namespace BrightPAS.Setup.Tests;

/// <summary>Security testing is mandatory for every module, and every finding must map to the standards it evidences.</summary>
public class StandardsTests
{
    [Fact]
    public void Every_available_module_has_a_security_suite()
    {
        foreach (var m in ProductModules.All.Where(m => m.Available))
        {
            var suite = ModuleSuites.All.SingleOrDefault(s => s.ModuleId == m.Id);
            Assert.True(suite is not null, $"Module '{m.Id}' is available but has no security test suite (Security/Standards.cs → ModuleSuites).");
            Assert.NotEmpty(suite!.AnonymousReads);
            Assert.NotEmpty(suite.AnonymousWrites);
        }
    }

    [Theory]
    [InlineData("Website (penetration probes)", "SQL injection (login form)", Standards.AsvsInput)]
    [InlineData("Website (penetration probes)", "TLS 1.0 refused", Standards.AsvsComms)]
    [InlineData("Module security – SMS & email reminders", "SMS vendor callback key guessing", Standards.AsvsLogic)]
    [InlineData("Module security – Lab results", "Login required (anonymous reads)", Standards.HisfAccess)]
    [InlineData("Module security – Theatre bookings", "Cross-site request forgery (CSRF) protection", Standards.AsvsSession)]
    [InlineData("Audit trail", "Audit table is tamper-evident (ledger)", Standards.HisfLogging)]
    [InlineData("Database (SQL Server)", "Database encryption at rest (TDE)", Standards.Ipp5)]
    [InlineData("Server (Windows)", "Windows Firewall on", Standards.HisfNetwork)]
    [InlineData("Module security – SMS & email reminders", "Vendor passwords encrypted at rest", Standards.AsvsCrypto)]
    public void Findings_map_to_standards(string area, string test, string control) => Assert.Contains(control, Standards.ControlsFor(area, test));

    [Theory]
    [InlineData("Module security – SMS & email reminders", "x", "SMS & email reminders")]
    [InlineData("Module security – Pre-admission forms", "x", "Pre-admission forms")]
    [InlineData("Module security – Estimates & invoices", "x", "Estimates & invoices")]
    [InlineData("Server (Windows)", "Windows Firewall on", "Server")]
    public void Findings_are_attributed_to_modules(string area, string test, string module) => Assert.Equal(module, Standards.ModuleFor(area, test));

    [Fact]
    public void Report_has_module_and_standards_sections()
    {
        var r = new SecurityReport();
        r.Pass("Module security – Lab results", "Login required (anonymous reads)", "ok");
        r.Fail("Website (penetration probes)", "SQL injection (login form)", "bad", "fix");
        var html = r.ToHtml();
        Assert.Contains("Security by module", html);
        Assert.Contains("Standards coverage", html);
        Assert.Contains("HISO 10029", html);
        var text = r.ToText();
        Assert.Contains("STANDARDS COVERAGE", text);
        Assert.Contains("Lab results", text);
        Assert.Contains("Not covered by this automated test", text);
    }
}
