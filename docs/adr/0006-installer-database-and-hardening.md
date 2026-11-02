# ADR 0006 – Installer step 2: database scripts and server hardening

- **Status:** Accepted · **Date:** 2026-11-02

## Decision
- Setup applies the schema from **idempotent SQL scripts generated from the EF Core migrations** (`scripts/generate-db-scripts.sh`), embedded in Setup.exe and executed with Microsoft.Data.SqlClient. Running Setup again applies only missing changes (verified against SQL Server 2022).
- IIS installation and TLS/IIS hardening are **PowerShell scripts embedded in Setup**, each safe to re-run and failing loudly. A `--what-if` preview shows what would happen without changing anything.
- New databases use the FULL recovery model (needed for the log-shipping standby) and read-committed snapshot isolation.
- SQL connections always require encryption; trusting a self-signed SQL certificate is an explicit, sandbox-only choice.
- Setup is published with **partial trimming** (framework only) and single-file compression (~16 MB). SqlClient is not trimmed; its trim warnings are suppressed and the trimmed build is tested end-to-end against SQL Server.
- `--ignore-prerequisites` exists for test labs with small VMs and is clearly logged as unsupported for production.

## Consequences
PowerShell steps cannot be tested in the Linux build environment; they are verified in the TDS sandbox VMs.
