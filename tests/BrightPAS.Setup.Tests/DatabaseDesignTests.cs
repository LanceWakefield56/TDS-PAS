using Microsoft.Data.SqlClient;
using BrightPAS.Setup.Install;

namespace BrightPAS.Setup.Tests;

/// <summary>
/// Runs every embedded installer script against a throw-away SQL Server database (twice – scripts must be idempotent) and checks the
/// database design rules: no heaps, every foreign key trusted and indexed, reporting views and procedures compile and run.
/// Needs PAS_TEST_SQLSERVER (skipped otherwise, like the other SQL Server tests).
/// </summary>
public class DatabaseDesignTests
{
    private static readonly string? Server = Environment.GetEnvironmentVariable("PAS_TEST_SQLSERVER");

    private static async Task<List<string>> Rows(SqlConnection c, string sql)
    {
        await using var cmd = new SqlCommand(sql, c);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<string>();
        while (await r.ReadAsync()) list.Add(r.GetString(0));
        return list;
    }

    [Fact]
    public async Task Installer_scripts_build_a_normalised_indexed_database_with_reporting_objects()
    {
        if (string.IsNullOrEmpty(Server)) return;
        var db = "BrightPASDesign_" + Guid.NewGuid().ToString("N")[..8];
        await using (var master = new SqlConnection(new SqlConnectionStringBuilder(Server) { InitialCatalog = "master" }.ConnectionString))
        {
            await master.OpenAsync();
            await new SqlCommand($"CREATE DATABASE [{db}]", master).ExecuteNonQueryAsync();
        }
        try
        {
            await using var c = new SqlConnection(new SqlConnectionStringBuilder(Server) { InitialCatalog = db }.ConnectionString);
            await c.OpenAsync();
            for (var pass = 0; pass < 2; pass++)
                foreach (var (name, sql) in SqlScripts.Embedded())
                    foreach (var batch in SqlScripts.SplitBatches(sql))
                    {
                        try { await new SqlCommand(batch, c) { CommandTimeout = 120 }.ExecuteNonQueryAsync(); }
                        catch (SqlException ex) { Assert.Fail($"{name} (pass {pass + 1}) failed: {ex.Message}"); }
                    }

            Assert.Empty(await Rows(c, "SELECT s.name+'.'+t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id JOIN sys.indexes i ON i.object_id=t.object_id AND i.index_id=0"));
            Assert.Empty(await Rows(c, "SELECT s.name+'.'+t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE NOT EXISTS (SELECT 1 FROM sys.key_constraints k WHERE k.parent_object_id=t.object_id AND k.type='PK')"));
            Assert.Empty(await Rows(c, "SELECT name FROM sys.foreign_keys WHERE is_not_trusted = 1 OR is_disabled = 1"));
            // Every foreign key column is the leading column of some index (fast joins, no table scans on delete checks).
            Assert.Empty(await Rows(c, @"SELECT OBJECT_SCHEMA_NAME(fk.parent_object_id)+'.'+OBJECT_NAME(fk.parent_object_id)+'.'+COL_NAME(fkc.parent_object_id, fkc.parent_column_id)
                FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id AND fkc.constraint_column_id = 1
                WHERE NOT EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = fkc.parent_object_id AND ic.column_id = fkc.parent_column_id AND ic.key_ordinal = 1)"));
            var patientFks = await Rows(c, "SELECT name FROM sys.foreign_keys WHERE referenced_object_id = OBJECT_ID('patients.Patients')");
            Assert.True(patientFks.Count >= 15, $"Patient references protected by foreign keys: {patientFks.Count}");
            // No comma-separated / JSON list columns left (1NF).
            Assert.Empty(await Rows(c, "SELECT OBJECT_SCHEMA_NAME(object_id)+'.'+OBJECT_NAME(object_id)+'.'+name FROM sys.columns WHERE name IN ('Checklist','AnswersJson','FlagsJson','RequiredTypes') AND OBJECTPROPERTY(object_id,'IsUserTable')=1"));

            foreach (var v in new[] { "vPatient", "vTheatreCase", "vAppointment", "vWaitlist", "vInvoiceSummary", "vCredentialStatus", "vRosterShift", "vAuditDaily" })
                await new SqlCommand($"SELECT TOP (0) * FROM [reporting].[{v}]", c).ExecuteNonQueryAsync();
            foreach (var p in new[] { "EXEC reporting.usp_TheatreList '2026-11-09'", "EXEC reporting.usp_WaitlistOverdue", "EXEC reporting.usp_CredentialExpiries", "EXEC reporting.usp_RosterHours '2026-11-01','2026-11-30'",
                         "EXEC reporting.usp_InvoicesOutstanding", $"EXEC reporting.usp_PatientAccessReport '{Guid.Empty}'", "EXEC maintenance.usp_IndexMaintenance @WhatIf = 1" })
                await new SqlCommand(p, c).ExecuteNonQueryAsync();
            Assert.Single(await Rows(c, "SELECT name FROM sys.database_principals WHERE name = 'brightpas_reporting' AND type = 'R'"));
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await using var master = new SqlConnection(new SqlConnectionStringBuilder(Server) { InitialCatalog = "master" }.ConnectionString);
            await master.OpenAsync();
            await new SqlCommand($"ALTER DATABASE [{db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{db}]", master).ExecuteNonQueryAsync();
        }
    }
}
