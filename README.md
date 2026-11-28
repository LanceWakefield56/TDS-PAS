# BrightPAS – Patient Administration System

Tasman Day Surgery's locally hosted patient administration system, with eMR, billing, rostering, assets/CSSD, quality management, endoscopy, and a TDS-developed messaging and workflow engine.

- **Specification:** v1.9 (BrightPAS). The master copy is kept as section files in the TDS-PAS OneDrive folder (`Specification\v1.9\`); Appendix C there records what each release has built. Earlier Word versions are in `docs/spec/`.
- **Status:** release 0.35.0 – a test build on demonstration data. **Nothing is live.** Every module still needs sandbox testing on Windows, an independent security review and clinical sign-off before real patient data (see "Before production" below).
- **Owner:** Tasman Day Surgery. Product owner: Lance Wakefield.
- **What is built:** see `CHANGELOG.md` (one entry per release), `docs/adr/` (one decision record per module or design decision) and `docs/spec-traceability.md` (every specification section → code → tests, and where the build differs from the specification).

## Structure

| Folder | Contents |
|---|---|
| `src/Platform/BrightPAS.Core` | Shared core: identity, audit, organisation model, patients, module framework |
| `src/Platform/BrightPAS.Identity` | Accounts, MFA, roles/permissions, break-glass, first-admin set-up |
| `src/Platform/BrightPAS.Shared` | Small shared helpers (also used by Setup) |
| `src/Platform/BrightPAS.Messaging` | Messaging and workflow engine (HL7, FHIR, DICOM, workflows) |
| `src/Platform/BrightPAS.Web` | Web host (IIS / ASP.NET Core) |
| `src/Modules/` | One self-contained project per module |
| `src/Installer/BrightPAS.Setup` | Guided platform and module installers |
| `tools/` | Test robot (virtual devices), Licence Manager, developer end-to-end helpers |
| `tests/` | Automated tests (SQL Server integration tests run when `PAS_TEST_SQLSERVER` is set) |
| `scripts/` | Build helpers (e.g. regenerate installer schema scripts) |
| `docs/` | Specification, developer guide, data dictionary, decision records (ADRs), backlog |

## Technology

C# / .NET 10 (LTS), ASP.NET Core minimal APIs on IIS, EF Core with one database schema per module, SQL Server 2022, React + TypeScript (Vite). HL7 v2 (own parser, MLLP), DICOM (fo-dicom 5.2.6), PDFs (PDFsharp-MigraDoc 6.2.0, pinned). No third-party integration engine.

## Build, test, release

| Step | Command |
|---|---|
| Build | `dotnet build BrightPAS.slnx` (0 warnings; .NET code analysis with all security and reliability rules fails the build on any finding); UI: `npm ci && npm run build` in `src/Platform/BrightPAS.Web/ClientApp` |
| Tests | `dotnet test BrightPAS.slnx` – set `PAS_TEST_SQLSERVER` to also run the SQL Server tests (required before a release) |
| Source style | `dotnet format whitespace BrightPAS.slnx --verify-no-changes` (rules in `.editorconfig`) |
| Database scripts | `scripts/generate-db-scripts.sh` after any EF migration (idempotent scripts the installer runs) |
| Test robot | `tools/BrightPAS.TestBot` against a fresh demonstration database – every page, role, form and virtual device (README there) |
| Release | `scripts/build-release.sh` – stops if any dependency has a known vulnerability or the source style check fails |

New module: follow the checklist in `docs/developer-guide.md` (licence gate, named permissions, set-up wizard, patient merge, demonstration data, standards self-test, tests, test robot).

## Before production

The automated tests and the test robot prove the build behaves as designed on demonstration data. They do not replace:

- Installation and the test robot run on the Windows sandbox, then a restore-tested backup and TDE (spec 4.6).
- An independent penetration test and code signing of the installer (spec Appendix C.2).
- Real-device checks: Olympus CV-1500 DICOM options, GE CARESCAPE Gateway and SaferSleep message layouts, Zebra/DYMO stock, HealthLink, Modica, the laboratory.
- Clinical governance sign-off: early warning score bands, checklists, monitor chart intervals, scribe templates, endoscopy wording, and the medicine rules (dose limits, allergy groups, interactions) checked against current NZ references.
- The NZULM monthly medicines file (free, requested from NZULM) imported with a confirmed column layout; a licensed drug database if TDS wants broader interaction cover.
- Privacy impact assessment for each module that handles health information, and the production licence key pair.

## Interim source control (until Azure DevOps)

See `docs/adr/0002-interim-source-control.md`. In short: the full Git history lives in **`TDS-PAS.bundle`** in the TDS-PAS OneDrive folder, with a plain source snapshot in **`TDS-PAS-source-latest.zip`**. Do not create a `.git` folder inside OneDrive.

## Rules

- Synthetic test data only. Never put real patient, staff or financial data in this repository.
- Every change: tests pass, the data dictionary and docs are updated, and an entry is added to `CHANGELOG.md`.
