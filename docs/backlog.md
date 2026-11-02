# Backlog

Status: ☐ to do · ◐ in progress · ☑ done

## Phase 0 – Foundations (started 2026-11-02, specification v1.6 approved)
- ☑ Enable NuGet package access for the build environment (api.nuget.org, globalcdn.nuget.org)
- ☑ Test projects (xUnit) – 41 tests
- ☑ SQL Server 2022 test container + opt-in integration tests (`PAS_TEST_SQLSERVER`)
- ◐ Module framework: manifest, dependency resolution, safe disable ☑ · module loading, per-module schema/migrations ☐
- ☐ Identity: accounts, passkeys/FIDO2, TOTP, roles and permissions, break-glass
- ☑ Audit: hash chain + SQL Server append-only ledger table + writer (ADR 0003)
- ☑ Organisation model + TDS default setup (Theatre 1 active, Theatre 2 inactive) + initial migration
- ◐ Patient core: NHI validation (old + new formats) ☑ · patient register, alerts ☐
- ☐ Messaging and workflow engine core: message store, queues, retry, dashboard
- ☐ UI shell (React + TypeScript): role-based "My work today" home, navigation, accessibility baseline
- ☐ IIS hardening script and security headers
- ◐ Guided platform installer: Setup.exe step 1 prerequisite checks ☑ (ADR 0004) · IIS setup, database creation, first admin + MFA, health check ☐
- ☐ Setup Centre (browser-based module installer)
- ☐ Developer guide and data dictionary generation

## Later phases
See the specification, section 11 (Delivery roadmap).
