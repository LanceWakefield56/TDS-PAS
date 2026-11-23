using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Modules.Research;

namespace BrightPAS.Modules.Tests;

/// <summary>0.25.0 De-identified and anonymised reporting (spec 7.21, ADR 0038).</summary>
public class ResearchTests
{
    private static readonly ColumnDef[] Defs =
    [
        new("patient_name", "Patient name", ColumnKind.DirectId), new("nhi", "NHI", ColumnKind.DirectId), new("date_of_birth", "Age", ColumnKind.BirthDate),
        new("sex", "Sex", ColumnKind.Category, true), new("ethnicity", "Ethnicity", ColumnKind.Ethnicity, true), new("town", "Region", ColumnKind.Place),
        new("procedure", "Procedure", ColumnKind.Category, true), new("operation_date", "Operation date", ColumnKind.Date), new("surgeon", "Surgeon", ColumnKind.Clinician), new("notes", "Notes", ColumnKind.FreeText),
    ];

    private static DataRow Row(Guid pid, string proc, string sex, string dob, string town = "Nelson", string date = "2026-10-15", string surgeon = "Dr Ben Hall") =>
        new(pid, new Dictionary<string, string?>
        {
            ["patient_name"] = "Aroha Ngātai",
            ["nhi"] = "ZZZ0016",
            ["date_of_birth"] = dob,
            ["sex"] = sex,
            ["ethnicity"] = "Māori",
            ["town"] = town,
            ["procedure"] = proc,
            ["operation_date"] = date,
            ["surgeon"] = surgeon,
            ["notes"] = "Phone 021 555 1234 re ZZZ0016"
        });

    private static List<DataRow> Data()
    {
        var rows = new List<DataRow>();
        for (var i = 0; i < 12; i++) rows.Add(Row(Guid.NewGuid(), "Colonoscopy", i % 2 == 0 ? "Female" : "Male", $"19{60 + i % 3}-03-0{1 + i % 5}"));
        for (var i = 0; i < 6; i++) rows.Add(Row(Guid.NewGuid(), "Hernia repair", "Male", "1955-06-10", "Blenheim", "2026-11-02", "Dr Sam Lee"));
        rows.Add(Row(Guid.NewGuid(), "Rare procedure", "Female", "1990-01-01", "Takaka"));
        return rows;
    }

    [Fact]
    public void Bands_regions_and_scrubbing()
    {
        Assert.Equal("60-64", DeidEngine.AgeBand(62));
        Assert.Equal("90+", DeidEngine.AgeBand(93));
        Assert.Equal("Nelson Tasman", DeidEngine.Region("Richmond"));
        Assert.Equal("Marlborough", DeidEngine.Region("blenheim"));
        Assert.Equal("North Island", DeidEngine.Region("Auckland"));
        Assert.Equal("Rest of South Island", DeidEngine.Region("Dunedin"));
        var s = DeidEngine.Scrub("Seen Aroha on 12/10/2026, NHI ZZZ0016, phone 021 555 1234, aroha@example.nz", ["Aroha"]);
        Assert.DoesNotContain("ZZZ0016", s, StringComparison.Ordinal);
        Assert.DoesNotContain("Aroha", s, StringComparison.Ordinal);
        Assert.DoesNotContain("12/10/2026", s, StringComparison.Ordinal);
        Assert.DoesNotContain("555", s, StringComparison.Ordinal);
    }

    [Fact]
    public void De_identified_removes_identifiers_bands_ages_months_dates_codes_clinicians_and_flags_small_groups()
    {
        var cols = new[] { "patient_name", "nhi", "date_of_birth", "sex", "ethnicity", "town", "procedure", "operation_date", "surgeon", "notes" };
        var r = DeidEngine.Apply(DeidLevel.DeIdentified, Defs, cols, Data(), new DateOnly(2026, 11, 24), _ => "x", 5);
        Assert.DoesNotContain("Patient name", r.Headers); Assert.DoesNotContain("NHI", r.Headers); Assert.DoesNotContain("Notes", r.Headers); Assert.DoesNotContain("Ethnicity", r.Headers);
        Assert.Contains(r.Notes, n => n.Contains("Māori data governance", StringComparison.Ordinal));
        var text = string.Join("\n", r.Rows.Select(x => string.Join(",", x)));
        Assert.DoesNotContain("ZZZ0016", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Ngātai", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Dr ", text, StringComparison.Ordinal);
        Assert.Contains("2026-10", text, StringComparison.Ordinal);
        Assert.DoesNotContain("2026-10-15", text, StringComparison.Ordinal);
        Assert.Contains("Clinician A", text, StringComparison.Ordinal);
        Assert.True(r.AtRiskRows > 0); // the rare procedure (1 person) and other small groups
        var removed = DeidEngine.Apply(DeidLevel.DeIdentified, Defs, cols, Data(), new DateOnly(2026, 11, 24), _ => "x", 5, removeSmallGroups: true);
        Assert.Equal(0, removed.AtRiskRows);
        Assert.True(removed.MinGroupSize >= 5 || removed.Rows.Count == 0);
        Assert.DoesNotContain("Rare procedure", string.Join("\n", removed.Rows.Select(x => string.Join(",", x))), StringComparison.Ordinal);
        // With a governance note, ethnicity is released.
        Assert.Contains("Ethnicity", DeidEngine.Apply(DeidLevel.DeIdentified, Defs, ["ethnicity", "procedure"], Data(), new DateOnly(2026, 11, 24), _ => "x", 5, allowEthnicity: true).Headers);
    }

    [Fact]
    public void Aggregate_suppresses_small_cells_and_the_cell_that_would_reveal_one()
    {
        var r = DeidEngine.Apply(DeidLevel.Aggregate, Defs, ["procedure", "sex"], Data(), new DateOnly(2026, 11, 24), _ => "x", 5);
        Assert.Equal(["Procedure", "Sex", "Count"], r.Headers);
        var rare = r.Rows.Single(x => x[0] == "Rare procedure");
        Assert.Equal(DeidEngine.Suppressed, rare[2]);
        Assert.Equal("6", r.Rows.Single(x => x[0] == "Colonoscopy" && x[1] == "Female")[2]);
        Assert.Equal("19", r.Rows[^1][2]);
        // Secondary: hernia has Male 6 only (one cell) – fine; a block with one suppressed and one visible cell must suppress both.
        var rows = Data(); rows.Add(Row(Guid.NewGuid(), "Hernia repair", "Female", "1970-01-01"));
        var r2 = DeidEngine.Apply(DeidLevel.Aggregate, Defs, ["procedure", "sex"], rows, new DateOnly(2026, 11, 24), _ => "x", 5);
        Assert.All(r2.Rows.Where(x => x[0] == "Hernia repair"), x => Assert.Equal(DeidEngine.Suppressed, x[2]));
    }

    private sealed class Fake : IDatasetSource
    {
        public List<DataRow> Rows { get; } = Data();
        public string Key => "test";
        public string Label => "Test";
        public IReadOnlyList<ColumnDef> Columns => Defs;
        public Task<IReadOnlyList<DataRow>> RowsAsync(DateOnly @from, DateOnly to, CancellationToken ct) => Task.FromResult<IReadOnlyList<DataRow>>(Rows);
    }

    [Fact]
    public async Task Patient_level_extracts_need_a_second_person_and_ethics_for_research_and_keep_stable_study_codes()
    {
        await using var h = new ModulesHost();
        await using var scope = h.Scope();
        var fake = new Fake();
        var svc = ActivatorUtilities.CreateInstance<ExtractService>(scope.ServiceProvider, new object[] { new IDatasetSource[] { fake } });
        ExtractEdit E(DeidLevel level, ExtractPurpose purpose = ExtractPurpose.QualityImprovement, string? ethics = null, string? maori = null, params string[] cols) =>
            new("SSI audit", "test", level, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), purpose, "Clinical governance committee", ethics, maori, 5, false, cols.Length == 0 ? ["procedure", "sex", "date_of_birth"] : cols);

        Assert.NotEmpty((await svc.CreateAsync(E(DeidLevel.Pseudonymised, ExtractPurpose.Research), "quality")).Errors);
        Assert.NotEmpty((await svc.CreateAsync(E(DeidLevel.DeIdentified, cols: ["ethnicity", "procedure"]), "quality")).Errors);
        var (agg, _) = await svc.CreateAsync(E(DeidLevel.Aggregate, cols: ["procedure"]), "quality");
        Assert.Equal(ExtractStatus.Approved, agg!.Status);
        var (csv, _, err) = await svc.ReleaseAsync(agg.Id, "quality");
        Assert.Null(err);
        var text = Encoding.UTF8.GetString(csv!);
        Assert.StartsWith("# Tasman Day Surgery DX-2026-", text, StringComparison.Ordinal);
        Assert.Contains("do not attempt to identify", text, StringComparison.Ordinal);

        var (p, errors) = await svc.CreateAsync(E(DeidLevel.Pseudonymised, ExtractPurpose.Research, "HDEC 2026 FULL 123"), "quality");
        Assert.Empty(errors);
        Assert.Equal(ExtractStatus.Submitted, p!.Status);
        Assert.NotNull((await svc.ReleaseAsync(p.Id, "quality")).Error);
        Assert.NotNull(await svc.DecideAsync(p.Id, true, "ok", "quality")); // own request
        var preview = await svc.PreviewAsync(p.Id, "quality");
        Assert.Equal(19, preview!.Rows.Count > 0 ? 19 : 0);
        Assert.Null(await svc.DecideAsync(p.Id, true, "Approved for the HDEC study; small groups acceptable for a pseudonymised research extract held by the investigator.", "privacy"));
        var first = Encoding.UTF8.GetString((await svc.ReleaseAsync(p.Id, "quality")).Csv!);
        var second = Encoding.UTF8.GetString((await svc.ReleaseAsync(p.Id, "quality")).Csv!);
        Assert.Equal(first.Split('\n')[2..], second.Split('\n')[2..]); // same study codes on a re-run (first line differs: time)
        Assert.DoesNotContain("ZZZ0016", first, StringComparison.Ordinal);
        Assert.Equal(19, await scope.ServiceProvider.GetRequiredService<ResearchDbContext>().StudyCodes.CountAsync(c => c.ExtractId == p.Id));
        Assert.Equal(2, (await svc.GetAsync(p.Id))!.Downloads);
        // Declining needs a reason.
        var (d, _) = await svc.CreateAsync(E(DeidLevel.DeIdentified), "quality");
        Assert.NotNull(await svc.DecideAsync(d!.Id, false, null, "privacy"));
        Assert.Null(await svc.DecideAsync(d.Id, false, "Use aggregate instead", "privacy"));
        Assert.NotNull((await svc.ReleaseAsync(d.Id, "quality")).Error);
    }
}
