# Changelog

## 0.2.0 – 2026-11-02 (Phase 0, increment 2)
- **Identity:** accounts (ASP.NET Core Identity, schema `identity`), mandatory authenticator-app MFA with recovery codes, two-step sign-in, lockout after 5 failures, NIST-style password rules, 10 default roles with fine-grained permissions, break-glass access (reason, 30-minute limit, audit, review), first-administrator set-up with a one-time installer code.
- **Patients module:** patient register with NHI validation, MRN allocation (TDS0000001…), NHI and name/date-of-birth duplicate checks, macron-insensitive search (NHI, MRN, name, date of birth), alerts; every create/view audited.
- **Module loading** and per-module database schemas/migrations (`core`, `identity`, `patients`).
- **Web host:** wired to SQL Server; security headers; rate-limited first-administrator endpoints; start-up seeding of roles and the TDS organisation structure.
- **Setup.exe step 2 (install):** IIS install, TLS and IIS hardening (PowerShell), database creation, idempotent schema scripts, one-time first-admin code, preview mode, logs. ~16 MB.
- **Fixed:** audit writer now works with SQL Server's retry-on-failure strategy (found in end-to-end testing).
- **Verified end-to-end on SQL Server 2022:** Setup creates the database → web app starts on it → first administrator created with code + MFA → code cannot be reused.
- Tests: 80 (all passing, including SQL Server integration tests).

## 0.1.1 – 2026-11-02
- Verified against a real SQL Server 2022 (Developer edition container): the core migration applies cleanly and `core.AuditEvents` is created as an APPEND_ONLY_LEDGER_TABLE.
- New SQL Server integration tests (run when `PAS_TEST_SQLSERVER` is set): migrations, ledger table type, audit chain on SQL Server, **UPDATE and DELETE on audit records are rejected by the database**, organisation setup round-trip.
- Tests: 44 (30 core unit, 3 SQL Server integration, 11 installer), all passing.

## 0.1.0 – 2026-11-02 (Phase 0, increment 1)
- Module framework: module manifests, dependency-ordered install plan, circular/missing dependency detection, safe-disable check.
- Organisation model (Organisation → Site → Facility → Location) with TDS default setup; Theatre 2 configured but inactive.
- NHI validation for old (AAANNNC) and new (AAANNAX) formats; test NHIs (Z…) flagged.
- Tamper-evident audit: SHA-256 hash chain, audit writer, SQL Server 2022 append-only ledger table.
- Initial EF Core migration for schema `core` (SQL Server).
- Guided installer Setup.exe (self-contained, Windows) – step 1 prerequisite checks with plain-English fixes and a saved report.
- Tests: 41 automated tests (core 30, installer 11), all passing.
- Docs: data dictionary (core), ADR 0003 (audit), ADR 0004 (installer), installer guide.

## 0.0.1 – 2026-11-02
- Repository skeleton and documentation; specification v1.6.
