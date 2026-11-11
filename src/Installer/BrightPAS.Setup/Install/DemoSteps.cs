using Microsoft.Data.SqlClient;

namespace BrightPAS.Setup.Install;

/// <summary>
/// Demonstration / training systems (0.12.3, --demo-data): asks the website to fill a NEW, empty database with an invented,
/// fully set-up practice (staff accounts, patients, lists, bookings, invoices, referrals, results, roster, interfaces).
/// Refused when the database already has user accounts, so it can never touch a real installation.
/// </summary>
public sealed class RequestDemoDataStep : IInstallStep
{
    public const string StateKey = "demo.state";
    public string Name => "Request demonstration data";
    public string Description => "Marks this new database to be filled with invented demonstration data when the website starts (test and training systems only).";

    public async Task<StepResult> RunAsync(InstallContext c, CancellationToken ct)
    {
        if (!c.Options.DemoData) return StepResult.Skip("Not a demonstration system (no --demo-data).");
        if (c.Options.WhatIf) return StepResult.Planned($"Would mark database [{c.Options.Database}] for demonstration data (only if it has no user accounts).");
        await using var conn = new SqlConnection(c.Options.ConnectionString());
        await conn.OpenAsync(ct);
        await using (var state = new SqlCommand("SELECT Value FROM core.Settings WHERE [Key] = @k", conn))
        {
            state.Parameters.AddWithValue("@k", StateKey);
            var existing = await state.ExecuteScalarAsync(ct) as string;
            if (existing is "done") return StepResult.Already("Demonstration data is already loaded in this database.");
            if (existing is "requested" or "running") return StepResult.Already("Demonstration data has already been requested.");
            if (existing is "failed")
            {
                await using var users0 = new SqlCommand("SELECT COUNT(*) FROM [identity].[AspNetUsers]", conn);
                if ((int)(await users0.ExecuteScalarAsync(ct) ?? 0) > 0)
                    return StepResult.Fail("Loading demonstration data failed part-way last time. Delete this database (it only holds demonstration data) and run Setup again.");
            }
        }
        await using (var users = new SqlCommand("SELECT COUNT(*) FROM [identity].[AspNetUsers]", conn))
        {
            var count = (int)(await users.ExecuteScalarAsync(ct) ?? 0);
            if (count > 0)
                return StepResult.Fail($"Database [{c.Options.Database}] already has {count} user account(s). Demonstration data can only go into a NEW database – " +
                                       "run Setup again with a different database name (for example BrightPAS_Demo).");
        }
        const string upsert = """
            MERGE core.Settings AS t USING (SELECT @k AS [Key]) AS s ON t.[Key] = s.[Key]
            WHEN MATCHED THEN UPDATE SET Value = N'requested', UpdatedAt = SYSDATETIMEOFFSET(), UpdatedBy = N'setup'
            WHEN NOT MATCHED THEN INSERT ([Key], Value, UpdatedAt, UpdatedBy) VALUES (@k, N'requested', SYSDATETIMEOFFSET(), N'setup');
            """;
        await using (var cmd = new SqlCommand(upsert, conn))
        {
            cmd.Parameters.AddWithValue("@k", StateKey);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        c.DemoDataRequested = true;
        return StepResult.Done("Demonstration data will be loaded when the website starts.");
    }
}

/// <summary>Waits (up to 10 minutes) for the website to finish loading the demonstration data and reports the result.</summary>
public sealed class WaitForDemoDataStep(TimeSpan? poll = null, TimeSpan? limit = null) : IInstallStep
{
    private readonly TimeSpan _poll = poll ?? TimeSpan.FromSeconds(5);
    private readonly TimeSpan _limit = limit ?? TimeSpan.FromMinutes(10);
    public string Name => "Load demonstration data";
    public string Description => "The website creates the demonstration practice: staff accounts, about 90 patients, three months of lists, bookings and invoices, referrals, results, roster and interfaces. This takes one to three minutes.";

    public async Task<StepResult> RunAsync(InstallContext c, CancellationToken ct)
    {
        if (!c.Options.DemoData) return StepResult.Skip("Not a demonstration system.");
        if (c.Options.WhatIf) return StepResult.Planned("Would wait for the website to load the demonstration data.");
        if (c.Options.SkipWeb || !c.Probe.IsWindows) return StepResult.Skip("The website was not installed on this run – the data loads the first time the website starts.");
        await using var conn = new SqlConnection(c.Options.ConnectionString());
        await conn.OpenAsync(ct);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        string? state = null;
        var lastShown = TimeSpan.Zero;
        while (watch.Elapsed < _limit)
        {
            state = await ValueAsync(conn, RequestDemoDataStep.StateKey, ct);
            if (state is "done")
            {
                var detail = await ValueAsync(conn, "demo.detail", ct);
                c.DemoDataLoaded = true;
                return StepResult.Done($"Demonstration data loaded in {watch.Elapsed.TotalSeconds:0} s. {detail}");
            }
            if (state is "failed")
                return StepResult.Fail($"The website could not load the demonstration data: {await ValueAsync(conn, "demo.detail", ct)} " +
                                       "The website log (BrightPAS › Website log, or C:\\ProgramData\\BrightPAS\\logs) has the details. Delete this demonstration database and run Setup again.");
            if (watch.Elapsed - lastShown >= TimeSpan.FromSeconds(30)) { c.Log.Info($"  … still loading ({watch.Elapsed.TotalSeconds:0} s, status: {state ?? "waiting for the website"})"); lastShown = watch.Elapsed; }
            await Task.Delay(_poll, ct);
        }
        return StepResult.Fail($"The demonstration data was not finished after {_limit.TotalMinutes:0} minutes (status: {state ?? "not started"}). Open the website once (it starts on the first visit) and check the website log.");
    }

    private static async Task<string?> ValueAsync(SqlConnection conn, string key, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("SELECT Value FROM core.Settings WHERE [Key] = @k", conn);
        cmd.Parameters.AddWithValue("@k", key);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }
}

/// <summary>What Setup prints for a demonstration system (the same details the website's sign-in page shows).</summary>
public static class DemoSignIn
{
    public const string Password = "Demo-Passphrase-2026";
    public const string AuthenticatorSecret = "BRIGHTPASDEMOKEY";
    public static readonly (string User, string Who)[] Accounts =
    [
        ("admin", "System Administrator"), ("privacy", "Privacy Officer"), ("reception", "Reception / Booking"), ("nurse", "Nurse"),
        ("hreid", "Surgeon (Dr Hamish Reid)"), ("slindqvist", "Endoscopist (Dr Sarah Lindqvist)"), ("bkowalski", "Anaesthetist (Dr Ben Kowalski)"),
        ("finance", "Finance"), ("cssd", "CSSD Technician"), ("pa", "Practice Assistant"), ("integration", "Integration Administrator"),
        ("integration2", "Second approver for mapping changes"), ("roster", "Roster Manager"), ("loconnor", "Visiting specialist (portal only)"),
    ];
}
