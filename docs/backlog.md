# Backlog

Status: ☐ to do · ◐ in progress · ☑ done

## Phase 0 – Foundations (starts after specification approval)
- ☐ Enable NuGet package access for the build environment (api.nuget.org, globalcdn.nuget.org)
- ☐ Test project (xUnit) and SQL Server test container
- ☐ Module framework: module manifest, dependencies, enable/disable, per-module database schema and migrations
- ☐ Identity: accounts, passkeys/FIDO2, TOTP, roles and permissions, break-glass
- ☐ Audit: tamper-evident audit log (SQL Server ledger tables)
- ☐ Organisation model: Organisation → Site → Facility → Location (Theatre 1, Theatre 2 inactive)
- ☐ Patient core: register, NHI field validation, alerts
- ☐ Messaging and workflow engine core: message store, queues, retry, dashboard
- ☐ UI shell (React + TypeScript): role-based "My work today" home, navigation, accessibility baseline
- ☐ IIS hardening script and security headers
- ☐ Guided platform installer: prerequisite checks, IIS setup, database creation, first admin + MFA, health check
- ☐ Setup Centre (browser-based module installer)
- ☐ Developer guide and data dictionary generation

## Later phases
See the specification, section 11 (Delivery roadmap).
