using BrightPAS.Core.Data;
using BrightPAS.Core.Setup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

var db = args.Length > 0 ? args[0] : "BrightPASDev";
var cs = $"Server=localhost,1433;User ID=sa;Password=Sandbox!Pass2026;TrustServerCertificate=True;Initial Catalog={db}";
await using (var c = new SqlConnection(new SqlConnectionStringBuilder(cs) { InitialCatalog = "master" }.ConnectionString))
{
    await c.OpenAsync();
    await new SqlCommand($"IF DB_ID('{db}') IS NOT NULL BEGIN ALTER DATABASE [{db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{db}]; END; CREATE DATABASE [{db}]; ALTER DATABASE [{db}] SET ALLOW_SNAPSHOT_ISOLATION ON;", c).ExecuteNonQueryAsync();
}
// Apply the same SQL scripts the installer uses.
foreach (var f in Directory.GetFiles("/home/claude/repo/TDS-PAS/src/Installer/BrightPAS.Setup/Database", "*.sql").OrderBy(x => x))
{
    var text = await File.ReadAllTextAsync(f);
    var batches = System.Text.RegularExpressions.Regex.Split(text, @"^\s*GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    await using var c = new SqlConnection(cs); await c.OpenAsync();
    foreach (var b in batches.Where(b => !string.IsNullOrWhiteSpace(b))) await new SqlCommand(b, c) { CommandTimeout = 120 }.ExecuteNonQueryAsync();
    Console.WriteLine($"applied {Path.GetFileName(f)}");
}
var code = BrightPAS.Shared.BootstrapCode.Generate();
await using (var core = new PasDbContext(new DbContextOptionsBuilder<PasDbContext>().UseSqlServer(cs).Options))
{
    var s = await core.SetupState.FirstOrDefaultAsync();
    if (s is null) core.SetupState.Add(new SetupState { BootstrapCodeHash = BrightPAS.Shared.BootstrapCode.Hash(code), BootstrapCodeCreatedAt = DateTimeOffset.UtcNow });
    else { s.BootstrapCodeHash = BrightPAS.Shared.BootstrapCode.Hash(code); s.BootstrapCodeCreatedAt = DateTimeOffset.UtcNow; }
    await core.SaveChangesAsync();
}
Console.WriteLine("BOOTSTRAP=" + code);
