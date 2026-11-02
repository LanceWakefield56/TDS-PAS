# Backlog

Status: ☐ to do · ◐ in progress · ☑ done

## Phase 0 – Foundations (started 2026-11-02, specification v1.6 approved)
- ☑ Enable NuGet package access for the build environment (api.nuget.org, globalcdn.nuget.org)
- ☑ Test projects (xUnit) – 41 tests
- ☑ SQL Server 2022 test container + opt-in integration tests (`PAS_TEST_SQLSERVER`)
- ☑ Module framework: manifest, dependency resolution, safe disable, module loading, per-module schemas and migrations
- ◐ Identity: accounts, TOTP MFA + recovery codes, lockout, password policy, roles/permissions, break-glass, first-admin bootstrap ☑ (ADR 0005) · passkeys/FIDO2, cookie sign-in for the UI ☐
- ☑ Audit: hash chain + SQL Server append-only ledger table + writer (ADR 0003)
- ☑ Organisation model + TDS default setup (Theatre 1 active, Theatre 2 inactive) + initial migration
- ☑ Patients module: register, NHI validation, MRN, duplicate checks (macron-insensitive), search, alerts, audit
- ☐ Messaging and workflow engine core: message store, queues, retry, dashboard
- ☐ UI shell (React + TypeScript): sign-in + MFA screens, first-admin screen, role-based "My work today" home, accessibility baseline (next)
- ◐ IIS/TLS hardening scripts in Setup ☑ · app security headers ☑ · verification in sandbox ☐
- ◐ Guided platform installer: checks ☑ · IIS install + hardening ☑ · database + schema ☑ · first-admin code ☑ (ADR 0006) · deploy web app to IIS (site, app pool, certificate) ☐
- ☐ Setup Centre (browser-based module installer)
- ☐ Developer guide and data dictionary generation

## Later phases
See the specification, section 11 (Delivery roadmap).
