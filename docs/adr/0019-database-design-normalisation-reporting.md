# ADR 0019 – Database design: no heaps, indexes, integrity, normalisation, reporting objects (0.10.1)

Status: accepted (2026-11-08)

## Context
Lance asked that the database avoids heaps, includes indexes, views and stored procedures, and follows third normal form.

## Decisions
1. **Clustered primary key on every table** (EF default on SQL Server). Installer test `DatabaseDesignTests` builds a database from the embedded scripts (twice, idempotency) and fails on any heap, missing PK, untrusted/disabled FK, or FK column without a leading index.
2. **Foreign keys:** within a module via EF; across modules in hand-written `14-reporting.sql` using `maintenance.usp_EnsureForeignKey` (creates WITH CHECK; WITH NOCHECK + warning only if existing sandbox rows are orphaned). Modules keep their own schemas/DbContexts.
3. **Normalisation:** list/JSON columns replaced by child tables (BookingChecks, forms Answers/Flags, cred RequiredTypes). Deliberate snapshots kept and documented: invoice surgeon/anaesthetist and line descriptions (legal document), identity-correction "Before" and merge "DuplicateSummary" (audit), letter Author. Known remaining 3NF gap: clinicians referenced by name (theatre lists/templates, appointments, waitlist) and `theatre.Sessions.LocationName` – to be replaced by ClinicianId/LocationId-only in the next increment.
4. **Reporting** through views and procedures in schema `reporting`, granted to role `brightpas_reporting` only (ownership chaining; no table access). Maintenance procedures in schema `maintenance`.
5. GUID keys are random (Guid.NewGuid), so clustered indexes fragment: `maintenance.usp_IndexMaintenance` reorganises/rebuilds weekly. (SQL Server Standard: offline rebuilds – schedule out of hours.)
