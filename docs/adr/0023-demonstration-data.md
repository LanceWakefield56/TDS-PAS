# ADR 0023 – Demonstration data installed by Setup (0.12.3)

Status: accepted (2026-11-11)

## Context
Lance wants to test every function of a build without first creating staff, patients, lists and settings by hand. Each module's guided set-up (ADR 0014) also needs data before it can be marked set up.

## Decisions
- One installer option, `--demo-data` (interactive question, default No), for a **new** database (default name BrightPAS_Demo). Setup refuses if the database already has user accounts, so a real installation can never be filled with demo data.
- The data is created **by the website, through the module services** (not SQL inserts): it passes the same validation as data typed in, uses the real Data Protection keys for encrypted fields and documents, and writes normal audit entries. Setup only requests it (`core.Settings demo.state`) and waits for the result.
- The application clock is a `DemoClock` wrapper; the loader winds it back for its own async flow so history carries realistic dates. Everything else sees real time.
- Invented people only: fictional staff and clinicians, test-range Z NHIs (flagged as test patients), `.example.nz`/`demo.brightpas.nz` addresses, 555 numbers, SMS/email/HealthLink in test mode, interfaces on local folders/ports.
- Demo systems are obvious: purple DEMONSTRATION SYSTEM bar on every page; the sign-in page lists the demo accounts, the shared password and the shared authenticator key (only when demo data is present).
- Every module in `WithWizard` is completed through `ModuleSetupService.CompleteAsync`, so the wizards' live checks prove the data is complete. New modules must add their demo data (developer guide checklist step 7).

## Consequences
- A demo database is disposable: to refresh it, install to a new database name. Re-running Setup on it upgrades it like any other.
- Found while building it: test SMS/email messages (no patient) failed against the 0.10 cross-schema foreign key, which also blocked the SMS & email set-up wizard on SQL Server. Fixed in 0.12.3 (`reminders.Messages.PatientId` nullable).
