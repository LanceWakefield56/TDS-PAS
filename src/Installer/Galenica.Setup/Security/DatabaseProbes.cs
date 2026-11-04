using Microsoft.Data.SqlClient;
using Galenica.Setup.Install;
using Galenica.Shared;

namespace Galenica.Setup.Security;

/// <summary>SQL Server configuration, least privilege, audit-log integrity and application security state.</summary>
public sealed class DatabaseProbes(SecurityTestOptions o, SecurityReport r, IInstallLog log)
{
    private const string Db = "Database (SQL Server)";
    private const string Audit = "Audit log";
    private const string App = "Application accounts and data";

    public async Task RunAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(o.Database.SqlServer)) { r.Add(Db, "Database checks", FindingStatus.Skip, "No SQL Server specified (use --sql-server)."); return; }
        SqlConnection conn;
        try
        {
            conn = new SqlConnection(o.Database.ConnectionString());
            await conn.OpenAsync(ct);
        }
        catch (SqlException ex)
        {
            log.Error($"Cannot connect to the database: {ex.Message}", ex);
            r.Fail(Db, "Connect to database", $"Could not connect: SQL error {ex.Number}: {ex.Message}", "Check the SQL Server name, database name and your permissions.");
            return;
        }
        await using (conn)
        {
            r.Pass(Db, "Connect to database", $"Connected to [{o.Database.Database}] on SQL Server {conn.ServerVersion}.");

            var encrypted = await Scalar<string>(conn, "SELECT encrypt_option FROM sys.dm_exec_connections WHERE session_id = @@SPID", ct);
            if (string.Equals(encrypted, "TRUE", StringComparison.OrdinalIgnoreCase)) r.Pass(Db, "Encrypted database connection", "This connection to SQL Server is encrypted (TLS).");
            else r.Fail(Db, "Encrypted database connection", "The connection to SQL Server is not encrypted.", "Galenica always requests encryption; check SQL Server's certificate configuration.");

            var major = int.TryParse(conn.ServerVersion.Split('.')[0], out var m) ? m : 0;
            if (major >= 16) r.Pass(Db, "SQL Server version", $"SQL Server 2022 or later ({conn.ServerVersion}).");
            else r.Fail(Db, "SQL Server version", $"Version {conn.ServerVersion} is older than SQL Server 2022.", "Galenica needs SQL Server 2022 (ledger tables, TDE in Standard).");

            var saEnabled = await Scalar<int?>(conn, "SELECT CASE WHEN is_disabled = 0 THEN 1 ELSE 0 END FROM sys.sql_logins WHERE principal_id = 1", ct);
            if (saEnabled == 1) r.Warn(Db, "'sa' account disabled", "The built-in 'sa' administrator login is enabled.", "Disable 'sa' (SSMS → Security → Logins → sa → Properties → Status → Disabled) once Windows administrators are set up. Acceptable in the sandbox.");
            else r.Pass(Db, "'sa' account disabled", "The built-in 'sa' login is disabled or SQL logins are not in use.");

            var xp = await Scalar<int?>(conn, "SELECT CONVERT(int, value_in_use) FROM sys.configurations WHERE name = 'xp_cmdshell'", ct);
            if (xp == 1) r.Fail(Db, "xp_cmdshell disabled", "xp_cmdshell is enabled (lets SQL run Windows commands).", "Run: EXEC sp_configure 'xp_cmdshell', 0; RECONFIGURE;");
            else r.Pass(Db, "xp_cmdshell disabled", "SQL Server cannot run operating-system commands.");

            var tde = await Scalar<bool?>(conn, "SELECT is_encrypted FROM sys.databases WHERE name = DB_NAME()", ct);
            if (tde == true) r.Pass(Db, "Encryption at rest (TDE)", "The database files and backups are encrypted.");
            else r.Warn(Db, "Encryption at rest (TDE)", "Transparent Data Encryption is not yet switched on.", "Planned before live use (specification section 5). Fine for the sandbox with test data only.");

            var recovery = await Scalar<string>(conn, "SELECT recovery_model_desc FROM sys.databases WHERE name = DB_NAME()", ct);
            if (recovery == "FULL") r.Pass(Db, "Recovery model", "FULL recovery (supports point-in-time restore and the standby copy).");
            else r.Warn(Db, "Recovery model", $"Recovery model is {recovery}.", "Set to FULL for point-in-time restore.");

            var lastFull = await Scalar<DateTime?>(conn, "SELECT MAX(backup_finish_date) FROM msdb.dbo.backupset WHERE database_name = DB_NAME() AND type = 'D'", ct);
            if (lastFull is null) r.Warn(Db, "Database backups", "No full backup of this database has ever been taken.", "Backups and the isolated backup design (spec 4.6) are set up in a later phase. For the sandbox this is expected.");
            else if (DateTime.Now - lastFull > TimeSpan.FromDays(1)) r.Warn(Db, "Database backups", $"Last full backup {lastFull:dd MMM yyyy HH:mm}.", "Take daily full backups.");
            else r.Pass(Db, "Database backups", $"Last full backup {lastFull:dd MMM yyyy HH:mm}.");

            await CheckPrincipalsAsync(conn, ct);
            await CheckAuditAsync(conn, ct);
            await CheckApplicationAsync(conn, ct);
        }
    }

    private async Task CheckPrincipalsAsync(SqlConnection conn, CancellationToken ct)
    {
        // Database users other than dbo/system: none should be db_owner or a sysadmin except administrators.
        const string sql = """
            SELECT dp.name,
                   IS_ROLEMEMBER('db_owner', dp.name) AS IsOwner,
                   ISNULL(IS_SRVROLEMEMBER('sysadmin', sp.name), 0) AS IsSysadmin,
                   IS_ROLEMEMBER('db_ddladmin', dp.name) AS IsDdl
            FROM sys.database_principals dp
            LEFT JOIN sys.server_principals sp ON sp.sid = dp.sid
            WHERE dp.type IN ('S','U','G','E','X') AND dp.name NOT IN ('dbo','guest','INFORMATION_SCHEMA','sys') AND dp.principal_id > 4
            """;
        var appUsers = new List<string>();
        var risky = new List<string>();
        await using (var cmd = new SqlCommand(sql, conn))
        await using (var rd = await cmd.ExecuteReaderAsync(ct))
            while (await rd.ReadAsync(ct))
            {
                var name = rd.GetString(0);
                var owner = !rd.IsDBNull(1) && rd.GetInt32(1) == 1;
                var sysadmin = !rd.IsDBNull(2) && rd.GetInt32(2) == 1;
                var ddl = !rd.IsDBNull(3) && rd.GetInt32(3) == 1;
                log.Debug($"Database user {name}: db_owner={owner} sysadmin={sysadmin} ddladmin={ddl}");
                var isApp = name.StartsWith("IIS APPPOOL\\", StringComparison.OrdinalIgnoreCase) || name.StartsWith("galenica_app", StringComparison.OrdinalIgnoreCase)
                            || name.Equals(o.Web.AppWindowsLogin, StringComparison.OrdinalIgnoreCase);
                if (isApp) { appUsers.Add(name); if (owner || sysadmin || ddl) risky.Add(name); }
            }
        if (appUsers.Count == 0) r.Warn(Db, "Website database account (least privilege)", "No Galenica website database account was found.", "Run Setup's install (it creates the website's own limited account).");
        else if (risky.Count > 0) r.Fail(Db, "Website database account (least privilege)", $"Too many rights: {string.Join(", ", risky)}.", "The website account must only be in db_datareader and db_datawriter. Remove other roles in SSMS.");
        else r.Pass(Db, "Website database account (least privilege)", $"{string.Join(", ", appUsers)} can read and write data only – it cannot change or drop tables.");
    }

    private async Task CheckAuditAsync(SqlConnection conn, CancellationToken ct)
    {
        var ledger = await Scalar<string>(conn, "SELECT t.ledger_type_desc FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name = 'core' AND t.name = 'AuditEvents'", ct);
        if (ledger == "APPEND_ONLY_LEDGER_TABLE") r.Pass(Audit, "Tamper-proof storage", "The audit table is a SQL Server append-only ledger table: records cannot be changed or deleted, even by administrators.");
        else r.Fail(Audit, "Tamper-proof storage", $"Audit table type is '{ledger ?? "missing"}'.", "Re-run Setup's install to create the audit table correctly.");

        // Re-verify the hash chain over every record.
        long count = 0, expected = 1;
        var prev = AuditHash.Genesis;
        long? broken = null;
        await using (var cmd = new SqlCommand("SELECT Sequence, OccurredAt, UserId, Action, EntityType, EntityId, PatientId, Reason, Workstation, PreviousHash, Hash FROM core.AuditEvents ORDER BY Sequence", conn) { CommandTimeout = 600 })
        await using (var rd = await cmd.ExecuteReaderAsync(ct))
            while (await rd.ReadAsync(ct))
            {
                var seq = rd.GetInt64(0);
                string? S(int i) => rd.IsDBNull(i) ? null : rd.GetString(i);
                var hash = AuditHash.Compute(seq, rd.GetDateTimeOffset(1), rd.GetString(2), rd.GetString(3), rd.GetString(4), rd.GetString(5), S(6), S(7), S(8), rd.GetString(9));
                if (seq != expected || rd.GetString(9) != prev || rd.GetString(10) != hash) { broken = seq; break; }
                prev = rd.GetString(10); expected++; count++;
            }
        if (broken is null) r.Pass(Audit, "Integrity of every record", $"All {count:N0} audit records re-verified: none altered, removed or re-ordered.");
        else r.Fail(Audit, "Integrity of every record", $"The audit chain is broken at record {broken}.", "Treat as a security incident: tell the Privacy Officer and GM, keep the server running, and do not delete anything.");

        var pending = await Scalar<int>(conn, "SELECT COUNT(*) FROM [identity].BreakGlassGrants WHERE ReviewStatus = 'Pending' AND GrantedAt < DATEADD(day, -7, SYSDATETIMEOFFSET())", ct);
        if (pending > 0) r.Warn(Audit, "Break-glass reviews", $"{pending} emergency access event(s) have waited more than 7 days for review.", "The Privacy Officer should review them.");
        else r.Pass(Audit, "Break-glass reviews", "No overdue emergency-access reviews.");
        var failed = await Scalar<int>(conn, "SELECT COUNT(*) FROM core.AuditEvents WHERE Action = 'LoginFailed' AND OccurredAt > DATEADD(day, -1, SYSDATETIMEOFFSET()) AND UserId NOT LIKE 'sectest-%'", ct);
        r.Info(Audit, "Failed sign-ins (last 24 h)", $"{failed} failed sign-in attempt(s), excluding this self-test.");
    }

    private async Task CheckApplicationAsync(SqlConnection conn, CancellationToken ct)
    {
        var noMfa = await Scalar<int>(conn, "SELECT COUNT(*) FROM [identity].AspNetUsers WHERE IsActive = 1 AND TwoFactorEnabled = 0 AND LastSignInAt IS NOT NULL", ct);
        if (noMfa > 0) r.Fail(App, "Two-step sign-in (MFA) for all users", $"{noMfa} active account(s) have signed in without MFA.", "Report this – sign-in requires MFA by design.");
        else r.Pass(App, "Two-step sign-in (MFA) for all users", "Every account that has signed in uses two-step sign-in.");
        var pendingMfa = await Scalar<int>(conn, "SELECT COUNT(*) FROM [identity].AspNetUsers WHERE IsActive = 1 AND TwoFactorEnabled = 0", ct);
        if (pendingMfa > 0) r.Info(App, "Accounts awaiting phone-app set-up", $"{pendingMfa} account(s) will be asked to set up MFA at first sign-in.");

        var admins = await Scalar<int>(conn, """
            SELECT COUNT(*) FROM [identity].AspNetUsers u JOIN [identity].AspNetUserRoles ur ON ur.UserId = u.Id
            JOIN [identity].AspNetRoles r ON r.Id = ur.RoleId WHERE r.Name = 'System Administrator' AND u.IsActive = 1
            """, ct);
        if (admins == 0) r.Warn(App, "System Administrators", "No active System Administrator yet.", "Complete the first-administrator set-up in the browser.");
        else if (admins > 3) r.Warn(App, "System Administrators", $"{admins} active System Administrators.", "Keep administrator accounts to the minimum (2–3).");
        else r.Pass(App, "System Administrators", $"{admins} active System Administrator account(s).");

        var code = await Scalar<DateTimeOffset?>(conn, "SELECT BootstrapCodeCreatedAt FROM core.SetupState WHERE Id = 1 AND BootstrapCodeUsedAt IS NULL AND BootstrapCodeHash IS NOT NULL", ct);
        if (code is not null) r.Info(App, "First-administrator code", $"An unused set-up code exists (created {code:dd MMM yyyy HH:mm}; it expires after 7 days).");

        var test = await Scalar<int>(conn, "SELECT COUNT(*) FROM patients.Patients WHERE IsTestPatient = 0", ct);
        if (test > 0) r.Warn(App, "Real patient data", $"{test} patient record(s) do not use a test NHI (Z…).", "The sandbox must contain test data only (spec 11.1). Use NHIs starting with Z for testing.");
        else r.Pass(App, "Real patient data", "Only test patients (NHI starting with Z, or no NHI marked as test) are present – or none yet.");
    }

    private async Task<T> Scalar<T>(SqlConnection conn, string sql, CancellationToken ct)
    {
        try
        {
            await using var cmd = new SqlCommand(sql, conn);
            var v = await cmd.ExecuteScalarAsync(ct);
            log.Debug($"SQL: {sql.Split('\n')[0].Trim()} => {v ?? "NULL"}");
            if (v is null or DBNull) return default!;
            var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            return (T)(target.IsInstanceOfType(v) ? v : Convert.ChangeType(v, target, System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (SqlException ex)
        {
            log.Warn($"SQL check failed ({ex.Number}): {ex.Message} | {sql.Split('\n')[0].Trim()}");
            return default!;
        }
    }
}
