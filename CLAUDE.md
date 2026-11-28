# BrightPAS – notes for Claude Code

BrightPAS is a patient administration system for **Tasman Day Surgery (TDS)**, a private day-surgery and endoscopy unit in Nelson, New Zealand. Owner: **Lance** (not a developer – keep replies short and plain, no jargon; he wants tokens saved). Base system + licensed modules (ADR 0009), one installer, guided set-up per module (ADR 0014). Read `README.md`, `CHANGELOG.md` (latest entry first), `docs/backlog.md` and the ADRs in `docs/adr/` before big changes.

## Machine
- Windows build VM (see `docs/build-vm-setup.md`). Repo at `C:\src\TDS-PAS` – never inside OneDrive. Shell scripts (`scripts/*.sh`) run in **Git Bash**.
- Lance installs BrightPAS on **Windows VMs with Setup.exe** – he does not use Docker. Sandbox test server: BRIGHTPAS-SRV (Windows Server 2025, SQL Server 2025, IIS 10).
- OneDrive `F:\OneDrive\TDS-PAS\`: release files per version in `<version>\`; `TDS-PAS.bundle` backup; `Specification\v1.9\` (spec section files – update changed sections each release, move old copies into `old-<version>\`, don't delete).

## Stack and commands
- .NET 10 (LTS packages only), EF Core on SQL Server (SQLite in some unit tests), React + Vite ClientApp (`src/Platform/BrightPAS.Web/ClientApp`, builds into wwwroot, not in git). Playwright 1.63 and **PDFsharp-MigraDoc 6.2.0 pinned – never upgrade MigraDoc**.
- `dotnet tool restore`; UI: `npm ci && npm run build` in ClientApp (check types with `npx tsc --noEmit -p .`).
- Tests: `dotnet test BrightPAS.slnx` with `PAS_TEST_SQLSERVER` set – always against SQL Server (SQLite misses translation errors, reserved words, cross-schema FKs and the retrying execution strategy). 415 tests at 0.28.0; 420 at 0.29.0; 424 at 0.30.0; 428 at 0.31.0; 431 at 0.32.0; 432 at 0.33.0; 436 at 0.34.0; 439 at 0.35.0.
- Cloud sandbox (no Windows): SQL Server runs in Docker (`docker run -d -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=... -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest`, start `dockerd` first) for `PAS_TEST_SQLSERVER`; `tools/dev-e2e` builds a database from the installer scripts, and the robot runs with `--browser chromium --app-pool none`. Setup.exe/IIS still need Windows.
- Style: `dotnet format whitespace BrightPAS.slnx` before a release. Analyzers treat warnings as errors (`.editorconfig` lists rules off on purpose).
- Release: `scripts/build-release.sh` → `artifacts/<version>/` (Setup.exe, web zip, test robot, install guide .docx, sandbox licence). Stops on style drift, vulnerable NuGet or npm audit findings.
- After any migration: `scripts/generate-db-scripts.sh` (idempotent SQL in `src/Installer/BrightPAS.Setup/Database/`, numbered per module; cross-schema FKs go in `90-reporting.sql`).
- Install test: `BrightPAS-Setup-<v>.exe install --demo-data` on this VM (IIS + local SQL), then the test robot `tools/BrightPAS.TestBot` against it. Always run the full robot from a **fresh demo database**. Demo accounts: password `Demo-Passphrase-2026`, TOTP key `BRIGHTPASDEMOKEY` (each code works once per account). `tools/dev-e2e` and old notes mention `/home/claude/...` Linux paths – those were the cloud sandbox; adapt them.

## Rules that matter
- **NZ time everywhere**: use `BrightPAS.Core.NzTime` (`FromLocal`, `NzDate`, `StartOfDay`, `Age`…). Never `DateTime.Now`/`ToLocalTime()`. Everything must handle daylight saving and leap years. UI: `nzDateTime`/`formatDate` helpers.
- Code: `StringComparison.Ordinal`, `JsonSerializerOptions.Web`, culture en-NZ, no LINQ variable named `from`. User transactions inside `CreateExecutionStrategy().ExecuteAsync`. Migration data SQL wrapped in `EXEC(N'...')`.
- Security: every endpoint has a named permission policy and a licence gate (`RequireModule`); public endpoints need a description and rate limiting (EndpointSecurityTests). New route prefixes need an area entry there. CSP forbids inline styles – CSS classes only. Every export can be encrypted (SecureExportService).
- UI for the robot: form errors in `<div className="error" role="alert">`; tables need headings; scrollable divs need `tabIndex=0 role="region" aria-label`; new action buttons go in the robot explorer's do-not-press list (`tools/BrightPAS.TestBot/Crawler.cs`). Edit/add forms open in a pop-up (`components/Modal.tsx`), not lower down the page.
- **New module checklist** (`docs/developer-guide.md`): project + slnx, Web.csproj, Program.cs (module, merge participant, endpoints, workers), permissions + default roles (+ `api.ts` Perm), LicenceModel, ModuleSetup wizard checks, Standards.cs self-test, DB script + reporting FKs, test hosts (ModulesHost, WebApiTests, InstallerTests, EndpointSecurityTests), **demo data** (must not depend on weekday/date), Shell menu + App.tsx route, robot scenario, docs.
- Every connection needs an import-from-file fallback (USB/share). New gates default off so demo data and upgrades aren't blocked. BrightPAS suggests; clinicians decide.

## Each release
Tests → bump `Directory.Build.props` + ClientApp `package.json`/`package-lock.json` → format → `build-release.sh` → install + full robot on a fresh demo DB → robot report into `artifacts/<v>/` → update CHANGELOG, backlog, data dictionary, ADR, install guide (`scripts/docs/install-guide.js`: H-section + "Upgrading to <v>"), README status, `docs/spec-traceability.md`, spec Appendix C → commit + tag `v<version>` → copy release files to OneDrive `<version>\` and refresh the bundle.

## Where things stand
Release chain on GitHub LanceWakefield56/TDS-PAS, each branch built on the one before: 0.29.0 E-consent (PR #1) → 0.30.0 Online payments (PR #3) → `feature/0.31-medicines` 0.31.0 Prescribing and barcode medicine checks (ADR 0044) → 0.32–0.36 agreed with Lance 28 Nov 2026 (payments extras, estimates follow-up, direct insurer claiming/EFTPOS/Xero, messaging workflows, triage EWS). PACS imaging is 0.37 on its own branch (ADR 0050, licence "imaging"). All pass every test against SQL Server in the cloud sandbox; none installed with Setup.exe on Windows yet. Before production: independent pen test, code signing, TDE + backups, trusted certificate, least-privilege DB account, clinical sign-off of rule sets, NZF terms check, PIAs, production licence keys, integration agreements (Health NZ, Xero, EFTPOS).
