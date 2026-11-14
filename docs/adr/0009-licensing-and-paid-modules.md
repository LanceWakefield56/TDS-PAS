# ADR 0009 – Licensing and paid modules

- **Status:** Accepted · **Date:** 2026-11-07

## Context
BrightPAS will be offered as a base system plus paid modules. Licences must be hard to forge or share, must work on servers without internet, and must never put patients at risk.

## Decision
- **Signed licence files.** A licence is JSON (customer, edition, modules or `*`, expiry, user limit, grace days, optional installation code and online-activation settings) signed with the vendor's **ECDSA P-256** private key. BrightPAS embeds the vendor **public** key(s) and rejects any change to the file. Development key (repository, sandbox only, flagged everywhere) and production key (`licensing/vendor-public-key.pem`, private key offline) are separate; a build that contains the production key refuses the development key by default (0.17.0); `Licensing:AllowDevelopmentKey=true` switches it back on for a test server, `false` refuses it on any build.
- **Locked to an installation.** Installation code = hash of a random installation id (in `core.LicenceState`) and the server's machine id (Windows MachineGuid). Copying the program or the database to another server changes the code.
- **Online activation (optional).** Licences may name a licence server. BrightPAS checks in every N days; the server binds a licence to one installation, supports revocation, and replies with a signed activation token. Offline grace period (default 30 days) before modules go read-only.
- **Tamper detection.** "Last seen" time is recorded every 15 minutes; a clock wound back more than a day makes modules read-only until fixed.
- **Enforcement.** Each module's API group carries `RequireModule(id)`; the MLLP listener and UI check the same status. `/api/me` returns per-module access so the menu shows only licensed modules. Active-user limit enforced on account creation.
- **Clinical-safety rule.** A licence problem (expired beyond grace, invalid, wrong server, activation overdue, clock problem) makes previously licensed modules **read-only** – GET still works so results, bookings and invoices stay viewable and printable – and never hides clinical data. Only modules never licensed are hidden.
- **Base vs paid split is commercial**, decided per licence in the Licence Manager (no code change). The Patients register, identity, audit and organisation set-up are always available.
- **Tools.** `BrightPAS-LicenceManager` (keygen with encrypted private key, issue, inspect) and `BrightPAS-LicenceServer` (activation, revocation) live in `tools/`.

## Consequences
- Forging a licence needs the private key; protect it (offline, password, backup in a safe).
- On-premises .NET code can be decompiled and patched by a determined attacker; licensing deters casual copying and the commercial contract covers the rest. Code obfuscation/signing can be added later.
