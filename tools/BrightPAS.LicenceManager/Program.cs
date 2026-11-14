// BrightPAS Licence Manager – vendor tool for creating signing keys and issuing licence files.
//   BrightPAS-LicenceManager                      menu
//   BrightPAS-LicenceManager keygen  --out FOLDER
//   BrightPAS-LicenceManager issue   --key PRIVATE.pem --customer NAME [--site NAME] [--edition Production|Evaluation|Sandbox]
//                                    --modules labs,theatre|all --expires YYYY-MM-DD [--installation-code CODE] [--max-users N]
//                                    [--grace-days 14] [--online-url URL --check-in-days 7 --offline-grace-days 30] --out FILE.lic
//   BrightPAS-LicenceManager inspect FILE.lic [--public-key PUBLIC.pem]
//   BrightPAS-LicenceManager modules
// The private key password is asked for (hidden) or read from BRIGHTPAS_KEY_PASSWORD. Never put it on the command line.
using System.Security.Cryptography;
using BrightPAS.Licensing;

var a = Args.Parse(args);
var interactive = a.Command is null;
Console.WriteLine("BrightPAS Licence Manager\n");
try
{
    var cmd = a.Command ?? Menu();
    return cmd switch
    {
        "keygen" => KeyGen(a),
        "issue" => Issue(a),
        "inspect" => Inspect(a),
        "modules" => Modules(),
        _ => Usage()
    };
}
catch (Exception ex) when (ex is CryptographicException or IOException or ArgumentException or FormatException or UnauthorizedAccessException)
{
    Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine("ERROR: " + ex.Message); Console.ResetColor();
    return 2;
}

string Menu()
{
    Console.WriteLine("1. Create the vendor signing keys (once only)");
    Console.WriteLine("2. Issue a licence");
    Console.WriteLine("3. Check (inspect) a licence file");
    Console.WriteLine("4. List modules");
    Console.Write("\nType 1-4 and press Enter: ");
    return Console.ReadLine()?.Trim() switch { "1" => "keygen", "2" => "issue", "3" => "inspect", "4" => "modules", _ => "help" };
}

int Usage()
{
    Console.WriteLine("Commands: keygen | issue | inspect | modules   (run without arguments for the menu)");
    return 1;
}

int Modules()
{
    foreach (var m in ProductModules.All) Console.WriteLine($"  {m.Id,-10} {m.Name,-30} {(m.Available ? "available" : "coming later")}");
    return 0;
}

int KeyGen(Args a)
{
    var folder = Q(a, "out", "Folder to save the keys in (use a USB drive)", Environment.CurrentDirectory);
    Directory.CreateDirectory(folder);
    var priv = Path.Combine(folder, "vendor-private-key.pem");
    var pub = Path.Combine(folder, "vendor-public-key.pem");
    if (File.Exists(priv)) throw new IOException($"{priv} already exists. Keys are created once; delete it only if you are sure.");
    var pw = Password(confirm: true);
    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    File.WriteAllText(priv, key.ExportEncryptedPkcs8PrivateKeyPem(pw, new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 600_000)));
    File.WriteAllText(pub, key.ExportSubjectPublicKeyInfoPem());
    Console.WriteLine($"\nPrivate key (encrypted): {priv}\nPublic key:              {pub}");
    Console.WriteLine("\nNext: copy ONLY vendor-public-key.pem into the BrightPAS source folder 'licensing' and rebuild.");
    Console.WriteLine("Keep the private key offline (USB + a backup in a safe). Anyone with it and its password can issue licences.");
    return 0;
}

int Issue(Args a)
{
    var keyPath = Q(a, "key", "Private key file", "vendor-private-key.pem");
    using var key = LoadPrivate(keyPath);
    var edition = Q(a, "edition", "Edition (Production / Evaluation / Sandbox)", LicenceEditions.Production);
    edition = new[] { LicenceEditions.Production, LicenceEditions.Evaluation, LicenceEditions.Sandbox }
        .FirstOrDefault(e => e.Equals(edition, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException($"Unknown edition '{edition}'.");
    var customer = Q(a, "customer", "Customer (organisation name)", null);
    var site = Q(a, "site", "Site (optional)", "");
    var code = Q(a, "installation-code", "Installation code (from BrightPAS › Licence; blank = any server)", "");
    if (string.IsNullOrWhiteSpace(code)) code = null;
    if (code is null && edition == LicenceEditions.Production) throw new ArgumentException("A Production licence must be locked to an installation code.");
    if (code is not null && LicenceEvaluator.Normalise(code).Length != 20) throw new ArgumentException("An installation code has 20 letters/numbers (XXXXX-XXXXX-XXXXX-XXXXX).");
    var modulesText = Q(a, "modules", "Modules (comma-separated ids, or 'all'); see 'modules'", "all");
    var modules = modulesText.Equals("all", StringComparison.OrdinalIgnoreCase) ? new[] { "*" }
        : modulesText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(m => m.ToLowerInvariant()).ToArray();
    foreach (var m in modules.Where(m => m != "*"))
        if (ProductModules.Find(m) is null) throw new ArgumentException($"Unknown module '{m}'. Run 'modules' for the list.");
    var expires = DateOnly.Parse(Q(a, "expires", "Valid until (YYYY-MM-DD)", DateTime.Today.AddYears(1).ToString("yyyy-MM-dd")), System.Globalization.CultureInfo.InvariantCulture);
    var maxUsersText = Q(a, "max-users", "Maximum active users (blank = unlimited)", "");
    var url = Q(a, "online-url", "Online activation URL (blank = offline licence)", "");
    var doc = new LicenceDocument
    {
        LicenceId = $"BP-{DateTime.UtcNow:yyyyMMdd}-{RandomNumberGenerator.GetHexString(6)}",
        Customer = customer!,
        Site = string.IsNullOrWhiteSpace(site) ? null : site,
        Edition = edition,
        IssuedAt = DateTimeOffset.UtcNow,
        ExpiresOn = expires,
        InstallationCode = code?.ToUpperInvariant(),
        MaxUsers = int.TryParse(maxUsersText, out var mu) ? mu : null,
        Modules = modules,
        GraceDays = int.TryParse(a.Get("grace-days"), out var g) ? g : 14,
        Activation = string.IsNullOrWhiteSpace(url) ? null : new ActivationSettings
        {
            Url = url,
            CheckInDays = int.TryParse(a.Get("check-in-days"), out var c) ? c : 7,
            OfflineGraceDays = int.TryParse(a.Get("offline-grace-days"), out var o) ? o : 30
        }
    };
    var outPath = Q(a, "out", "Save licence as", $"{doc.LicenceId}.lic");
    File.WriteAllText(outPath, LicenceCodec.Sign(doc, key));
    Console.WriteLine($"\nLicence {doc.LicenceId} written to {outPath}");
    Console.WriteLine("Send this file to the customer. They install it in BrightPAS › Licence.");
    return 0;
}

int Inspect(Args a)
{
    var file = a.Positional ?? Ask("Licence file", null);
    var keys = new List<TrustedKey>(VendorKeys.BuiltIn());
    if (a.Get("public-key") is { } pk) keys.Insert(0, TrustedKey.FromPem("given key", File.ReadAllText(pk), false));
    var r = LicenceCodec.VerifyLicence(File.ReadAllText(file), keys);
    if (!r.Ok) { Console.WriteLine("INVALID: " + r.Error); return 2; }
    var d = r.Value!;
    Console.WriteLine($"VALID signature ({r.Key!.Name})");
    Console.WriteLine($"  Licence:      {d.LicenceId}\n  Customer:     {d.Customer} {d.Site}\n  Edition:      {d.Edition}\n  Valid until:  {d.ExpiresOn:yyyy-MM-dd} (+{d.GraceDays} days grace)");
    Console.WriteLine($"  Installation: {d.InstallationCode ?? "any server"}\n  Max users:    {d.MaxUsers?.ToString() ?? "unlimited"}\n  Modules:      {string.Join(", ", d.Modules)}");
    Console.WriteLine($"  Online:       {(d.Activation is null ? "no (offline licence)" : $"{d.Activation.Url}, every {d.Activation.CheckInDays} days, {d.Activation.OfflineGraceDays} days offline allowed")}");
    return 0;
}

ECDsa LoadPrivate(string path)
{
    var pem = File.ReadAllText(path);
    var key = ECDsa.Create();
    if (pem.Contains("ENCRYPTED PRIVATE KEY", StringComparison.Ordinal)) key.ImportFromEncryptedPem(pem, Password(confirm: false));
    else
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("Warning: this private key is not password-protected (development key?).");
        Console.ResetColor();
        key.ImportFromPem(pem);
    }
    return key;
}

static string Password(bool confirm)
{
    var env = Environment.GetEnvironmentVariable("BRIGHTPAS_KEY_PASSWORD");
    if (!string.IsNullOrEmpty(env)) return env;
    while (true)
    {
        var p = ReadHidden("Private key password (at least 16 characters): ");
        if (p.Length < 16) { Console.WriteLine("Too short."); continue; }
        if (!confirm || ReadHidden("Type it again: ") == p) return p;
        Console.WriteLine("The passwords did not match.");
    }
}

static string ReadHidden(string prompt)
{
    Console.Write(prompt);
    if (Console.IsInputRedirected) return Console.ReadLine() ?? "";
    var s = new System.Text.StringBuilder();
    while (true)
    {
        var k = Console.ReadKey(intercept: true);
        if (k.Key == ConsoleKey.Enter) { Console.WriteLine(); return s.ToString(); }
        if (k.Key == ConsoleKey.Backspace) { if (s.Length > 0) s.Length--; }
        else if (!char.IsControl(k.KeyChar)) s.Append(k.KeyChar);
    }
}

string Q(Args args, string opt, string prompt, string? def) =>
    args.Get(opt) ?? (interactive ? Ask(prompt, def) : def ?? throw new ArgumentException($"--{opt} is required."));

static string Ask(string prompt, string? def)
{
    Console.Write(def is null ? $"{prompt}: " : $"{prompt} [{def}]: ");
    var v = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(v)) return def ?? throw new ArgumentException($"{prompt} is required.");
    return v;
}

internal sealed record Args(string? Command, string? Positional, Dictionary<string, string> Options)
{
    public string? Get(string name) => Options.TryGetValue(name, out var v) ? v : null;
    public static Args Parse(string[] args)
    {
        string? cmd = null, pos = null;
        var o = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal)) o[args[i][2..]] = i + 1 < args.Length ? args[++i] : "";
            else if (cmd is null) cmd = args[i].ToLowerInvariant();
            else pos ??= args[i];
        }
        return new Args(cmd, pos, o);
    }
}
