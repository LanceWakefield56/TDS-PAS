using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Tds.Pas.Core.Data;

/// <summary>Design-time factory used only to generate migrations (no database connection is made).</summary>
public sealed class PasDbContextFactory : IDesignTimeDbContextFactory<PasDbContext>
{
    public PasDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PasDbContext>()
            .UseSqlServer("Server=localhost;Database=TdsPas;Integrated Security=true;TrustServerCertificate=true",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PasDbContext.Schema))
            .Options);
}
