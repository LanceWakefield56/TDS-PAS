using System.Reflection;
using System.Text.RegularExpressions;

namespace Galenica.Setup.Install;

/// <summary>Embedded, idempotent schema scripts generated from the EF Core migrations (scripts/generate-db-scripts.sh).</summary>
public static partial class SqlScripts
{
    public static IReadOnlyList<(string Name, string Sql)> Embedded()
    {
        var asm = Assembly.GetExecutingAssembly();
        return asm.GetManifestResourceNames()
            .Where(n => n.StartsWith("Database.", StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select(n =>
            {
                using var s = asm.GetManifestResourceStream(n)!;
                using var r = new StreamReader(s);
                return (n["Database.".Length..], r.ReadToEnd());
            })
            .ToList();
    }

    /// <summary>Splits a script into batches on lines containing only GO (as sqlcmd does).</summary>
    public static IReadOnlyList<string> SplitBatches(string script) =>
        GoLine().Split(script.TrimStart('﻿'))
            .Select(b => b.Trim())
            .Where(b => b.Length > 0)
            .ToList();

    [GeneratedRegex(@"^\s*GO\s*;?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoLine();
}
