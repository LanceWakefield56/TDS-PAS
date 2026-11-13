# Developer Guide (outline)

Filled in as the system is built.

1. Architecture overview (modular monolith, module framework)
2. Local setup and build
3. Coding standards and project conventions
4. Database: schemas, migrations, ledger/audit tables
5. Security: authentication, authorisation, encryption, secrets
6. Messaging and workflow engine: adding an interface or workflow
7. Adding a new module (checklist)
8. Installers: platform installer, module installers, silent mode
9. Testing strategy and synthetic data
10. Release, deployment and rollback

## Running tests (current)
- Unit tests: `dotnet test` (uses SQLite in memory; no setup needed).
- SQL Server integration tests: start SQL Server 2022 (e.g. `docker run -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=... -e MSSQL_PID=Developer -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest`), then set
  `PAS_TEST_SQLSERVER="Server=localhost,1433;User Id=sa;Password=...;TrustServerCertificate=true;Encrypt=true"` and run `dotnet test`. Each run creates and drops a throw-away database.
- Apply migrations to a database: `dotnet ef database update --project src/Platform/BrightPAS.Core --connection "<connection string>"`.

## Adding a new module (checklist, 0.7.0)
1. Project `src/Modules/BrightPAS.Modules.<Name>` with its own schema, DbContext, design-time factory and initial migration; register the module in `Program.cs`, the web test factory and `ModulesHost`.
2. Add the module id to `ProductModules` (Available = true), permissions to `Permissions` and the default roles.
3. Endpoints: `RequireModule(id)` on the group and a specific permission on every endpoint; add an `Areas` entry in `EndpointSecurityTests` (ADR 0013); public endpoints need the allow-list and a rate-limit policy of their own.
4. Self-test: a `ModuleSuites` entry and module rule in `Setup/Security/Standards.cs`.
5. Guided set-up (ADR 0014): add live checks in `ModuleSetupService.ChecksAsync` and the id to `WithWizard` (also `SETUP_MODULES` in the UI).
6. `scripts/generate-db-scripts.sh` (new numbered script) + `InstallerTests` expected list; data dictionary; guide test section; CHANGELOG.
7. Demonstration data (ADR 0023): add the module's data to `Web/Demo/DemoData.cs` through the module's own services so a demo system shows it already set up; `DemoDataTests` checks every module in `WithWizard` passes its set-up wizard.
8. Test robot (ADR 0024): menu entry for the right roles (the robot follows the menu) and a route guard in `App.tsx` (`allowed(...)`) matching it; add the module's pages to `Crawler.AllPages`, any button the explorer must never press to `Crawler.Dangerous`, and a scenario in `Scenarios.cs` for any new device or outside system. Run the robot against a fresh demo database before release.


## Demonstration data (0.12.3, ADR 0023)
- `Setup.exe install --demo-data` (or answer Yes) on a NEW database → `RequestDemoDataStep` writes `core.Settings demo.state = requested` → the website's `DemoDataWorker` runs `DemoDataSeeder` once (inside the app pool, so Data Protection keys and encryption are the real ones) → `WaitForDemoDataStep` waits for `done`/`failed`.
- The seeder only uses the module services (same validation as the screens). `DemoClock` (the registered `TimeProvider`) lets the seeder wind the clock back for its own async flow only, so history (bookings, invoices, audit) has realistic dates; nothing else is affected.
- Refuses when any user account exists. Demo accounts: `DemoAccounts` (password `Demo-Passphrase-2026`, shared TOTP key `BRIGHTPASDEMOKEY`), shown on the sign-in page only when demo data is present.
- Dev loop: `tools/dev-e2e` DevDb → insert `demo.state=requested` → `run-web.sh` → wait; `flow7.js` signs in as each role through the screens.

## Test robot (0.12.4, ADR 0024)
- `tools/BrightPAS.TestBot` – see its README. Dev container: fresh demo DB (above), `run-web.sh`, then
  `dotnet run --project tools/BrightPAS.TestBot -- --url https://localhost:5001 --browser chromium --out /tmp/tb` (as root, so the simulator certificate can be trusted; restart the website once after the first trust). `--only devices` for the device scenarios only (~3 min); a full run is ~30 min.
- The explorer presses buttons on a demo database: start each full run from a fresh demo database, or results are affected by earlier runs.
- Review `report.html` (problems, warnings, device outputs) and the screen gallery screenshots before each release.
