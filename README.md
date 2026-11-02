# TDS PAS – Patient Administration System

Tasman Day Surgery's locally hosted patient administration system, with eMR, billing, rostering, assets/CSSD, quality management, endoscopy, and a TDS-developed messaging and workflow engine.

- **Specification:** `docs/spec/TDS-PAS-Specification-v1.6.docx` (TDS-PAS-1.0)
- **Status:** Phase 0 in progress (v0.1.1). Specification v1.6 approved.
- **Owner:** Tasman Day Surgery. Product owner: Lance Wakefield.

## Structure

| Folder | Contents |
|---|---|
| `src/Platform/Tds.Pas.Core` | Shared core: identity, audit, organisation model, patients, module framework |
| `src/Platform/Tds.Pas.Messaging` | Messaging and workflow engine (HL7, FHIR, DICOM, workflows) |
| `src/Platform/Tds.Pas.Web` | Web host (IIS / ASP.NET Core) |
| `src/Modules/` | One self-contained project per module |
| `src/Installer/Tds.Pas.Setup` | Guided platform and module installers |
| `tests/` | Automated tests |
| `docs/` | Specification, developer guide, data dictionary, decision records (ADRs), backlog |

## Technology

C# / .NET 10 (LTS), ASP.NET Core on IIS, SQL Server 2022 Standard, React + TypeScript UI, fo-dicom, NHapi, Firely SDK. See the specification, section 4.3.

## Interim source control (until Azure DevOps)

See `docs/adr/0002-interim-source-control.md`. In short: the full Git history lives in **`TDS-PAS.bundle`** in the TDS-PAS OneDrive folder, with a plain source snapshot in **`TDS-PAS-source-latest.zip`**. Do not create a `.git` folder inside OneDrive.

## Rules

- Synthetic test data only. Never put real patient, staff or financial data in this repository.
- Every change: tests pass, the data dictionary and docs are updated, and an entry is added to `CHANGELOG.md`.
