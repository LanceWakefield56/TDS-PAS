# Backlog

Status: ☐ to do · ◐ in progress · ☑ done

## Phase 0 – Foundations (started 2026-11-02, specification v1.6 approved)
- ☑ Enable NuGet package access for the build environment (api.nuget.org, globalcdn.nuget.org)
- ☑ Test projects (xUnit) – 41 tests
- ☑ SQL Server 2022 test container + opt-in integration tests (`PAS_TEST_SQLSERVER`)
- ☑ Module framework: manifest, dependency resolution, safe disable, module loading, per-module schemas and migrations
- ◐ Identity: accounts, TOTP MFA + recovery codes, lockout, password policy, roles/permissions, break-glass, first-admin bootstrap, cookie sign-in, user administration ☑ · passkeys/FIDO2, force password change at first sign-in, break-glass screen ☐
- ☑ Audit: hash chain + SQL Server append-only ledger table + writer (ADR 0003)
- ☑ Organisation model + TDS default setup (Theatre 1 active, Theatre 2 inactive) + initial migration
- ☑ Patients module: register, NHI validation, MRN, duplicate checks (macron-insensitive), search, alerts, audit
- ☐ Messaging and workflow engine core: message store, queues, retry, dashboard
- ☑ UI shell (React + TypeScript): set-up wizard, sign-in + MFA, My work today, patients, user accounts, audit log
- ☑ Security, penetration and audit self-test (ADR 0007)
- ☐ Setup Centre (browser-based module installer) · ☐ messaging engine core · ☐ TDE + backups (spec 4.6)
- ◐ IIS/TLS hardening scripts in Setup ☑ · app security headers ☑ · verification in sandbox ☐
- ☑ Guided platform installer: checks, IIS install + hardening, database + schema, first-admin code, web deployment to IIS, single-server mode, verbose logging (ADR 0006, 0008)
- ☐ Developer guide and data dictionary generation

## Later phases
See the specification, section 11 (Delivery roadmap).
