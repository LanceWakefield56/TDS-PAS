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
9. Specification: update `docs/spec-traceability.md` and spec Appendix C (and the spec section itself if the build adds or changes a requirement, with a line in the spec CHANGELOG).


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

## Assets & CSSD (0.13.0, ADR 0025)
- `src/Modules/BrightPAS.Modules.Assets`: `AssetsModel.cs` (entities, schema `assets`), `EquipmentService` (register, maintenance, lockout, suppliers, settings), `CssdService` (trays, cycles, `ParseMachineLog`, release, issue, recall, scopes, loans, nonconformance, tray ZPL), `StockService` (stock, lots, implants, orders, receipt, invoice matching, Xero bills CSV).
- Web: `Api/AssetsEndpoints.cs` (`/api/assets/*`), merge participant `AssetsMerge`, set-up checks `AssetsChecksAsync`, demo data `AssetsAsync` in DemoData.cs. UI: `pages/Cssd.tsx`, `pages/Assets.tsx`, `pages/Stock.tsx`, patient panel in `components/PatientModules.tsx`.
- Machine-log reader: extend `CssdService.ParseMachineLog` (and its test) when a real Belimed/steriliser export is available; keep it tolerant and always store the raw log.
- Devices that connect (monitors, endoscopy towers) are `Equipment` rows with `NetworkAddress`. Bedside monitors and SaferSleep (0.17.0, ADR 0029) come in through the messaging engine: add an `InboundHandler` value, handle it in `MessagingHost`, and keep HL7 parsing in a static class with unit tests (`DeviceHl7`). Rules that protect patients from a race (one patient per monitor) belong in the database as unique indexes as well as in code.

## Post-op follow-up & SSI (0.24.0, ADR 0037)
- `src/Modules/BrightPAS.Modules.FollowUp`: `FollowUpModel.cs` (schema `followup`: Plans, PlanSteps, Contacts, ContactAnswers, SsiCases, SsiRiskFactors, Settings), `FollowUpScript.cs` (pure: questions, warning rules, wound → suspected SSI, clinic days incl. Nelson Anniversary, due dates, example plans – change here only, with a test), `FollowUpService` (plans, contacts at discharge, scan, links, record, escalations, check-in, report), `SsiService` (open, classify, incident, CSV).
- Web: `Integration/FollowUpIntegration.cs` (`OperationFactsProvider` : `IOperationFacts` from Stock/eMR/Triage/Theatre; `FollowUpWorker` every 5 min incl. GP letters), `Api/FollowUpEndpoints.cs` (`/api/followup/*`, public `/api/c/{token}`), indicators `ssi` and `fucomplete` in `QualityIndicators.cs`. Reminders: `MessageKind.FollowUpCheckIn`, `QueueFollowUpLinkAsync`, `QueueStaffAlertAsync`. Letters: template `GPSSI`. UI: `pages/FollowUp.tsx` (worklists, calendar, contact/script, SSI list and record, plans/settings, report, patient panel, public `/c/:token`).
- Tests: `FollowUpTests` (rules, wound, clinic days, plan choice, discharge → contacts → attempts → escalation → SSI → incident → CSV → report, check-in link security and concern call back).

## Patient journey (0.23.0, ADR 0036)
- `Web/Journey/JourneyService.cs` reads every module (read only): `ForPatientAsync` (episodes and stages), `TodayAsync` (My list, filtered by permission), `DayAsync` (day board). `Api/JourneyEndpoints.cs` (`/api/journey/*`). UI: `components/Journey.tsx` (stage bar, next step, `ActionLink`), `components/TodayList.tsx`, `pages/DayBoard.tsx`; menu groups in `components/Shell.tsx`.
- **New module checklist addition:** add the module's stage to the journey (state, detail, action → an existing screen and the permission it needs) and its to-dos to `TodayAsync`; the robot's "Patient journey" steps then check every action opens for the roles that see it. Never change data from the journey.

## Triage & suitability (0.22.0, ADR 0035)
- `src/Modules/BrightPAS.Modules.Triage`: `TriageModel.cs` (entities, schema `triage`), `TriageScoring.cs` (pure: facts → scores, rules, medicine prompts, suggested outcome, change summary; change scoring here only, with a test), `TriageService` (facts from Patients/Forms/Labs, run, sign-off, worklists, booking gate, re-triage scan, report), `TriageRulesService` (versioned rule sets: example, copy, draft, approve, delete).
- Booking gate: `Core/Modules/IBookingConfirmGate` – Theatre calls every registered gate before Confirmed; `Web/Integration/TriageIntegration.cs` has `TriageBookingGate` (only when the licence is Full) and `TriageWorker` (scan every 5 minutes). Merge participant `TriageMerge`; set-up checks `TriageChecksAsync`; demo `TriageAsync`.
- Web: `Api/TriageEndpoints.cs` (`/api/triage/*`). UI: `pages/Triage.tsx` (worklists, assessment, rules/settings, report, patient panel, theatre badge).
- Tests: `TriageTests` (scores, rules scope and medicines, no approved rules, full flow with the gate, stale/hold settings).

## Procedure estimates (0.21.0, ADR 0034)
- `src/Modules/BrightPAS.Modules.Estimates`: `EstimatesModel.cs` (entities, schema `est`), `EstimateCalculator.cs` (the pricing rules – pure; change rules here only, with a test), `EstimateService` (save/issue, references, archive, templates, Incisive, scheduled prices), `EstimateCatalogue` (price list admin, safe delete, settings, price impact), `EstimateImport` (ClosedXML pricing/stock spreadsheets and downloads), `EstimateMail` (password rules, right-recipient checks, Outlook draft, outbox).
- Web: `Api/EstimatesEndpoints.cs` (`/api/estimates/*`), `Pdf/EstimatePdf.cs` (printed layout; logo/footer embedded from `Pdf/Estimates`), `Integration/EstimatesIntegration.cs` (`EstimateMailer` on the Reminders SMTP settings, `EstimatesWorker` for held emails and scheduled prices), merge participant `EstimatesMerge`, set-up checks `EstimatesChecksAsync`, demo data `EstimatesAsync` (spreadsheets in `Demo/Estimates`). UI: `pages/Estimates.tsx` (list, builder, outbox, patient panel), `pages/EstimatesAdmin.tsx`.
- Tests: `EstimatesTests` imports `Fixtures/estimates-pricing.xlsx` (TDS's real price list) and runs the TDS Estimates v1.5 self-test – every procedure total against the old 2026 estimates and the add-on/special/extended-time/email/archive/template/safe-delete scenarios. Keep it passing when prices or rules change; update the `Known` totals only from a signed-off estimate.
