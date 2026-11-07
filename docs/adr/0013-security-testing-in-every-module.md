# ADR 0013 – Security testing is mandatory for every module, mapped to standards

- **Status:** Accepted · **Date:** 2026-11-07

## Decision
1. **Secure by default.** The web app has an authorization *fallback policy*: any endpoint without explicit rules requires a signed-in user. Public endpoints must opt out with `AllowAnonymous` and appear on a reviewed allow-list. The CSRF check runs before authentication so forged requests are refused uniformly.
2. **Endpoint security inventory (every build).** `EndpointSecurityTests` enumerates every route at runtime and fails the build if any endpoint: is outside a known area; belongs to a module but is not licence-gated (`RequireModule` adds `RequiresModuleAttribute`); is public but not on the allow-list or (where required) not rate-limited; or lacks a specific permission. It then **attacks every endpoint**: anonymously (must be 401 or, for public ones, reveal nothing), as a signed-in browser without the CSRF header (must be 400), and as a low-privilege user (must be 403 wherever a permission is required). Modules added later are covered automatically.
3. **Every module has a black-box suite** in the on-server self-test (`Setup.exe security-test`, `ModuleSuites`); a build test fails if an available module has none. Module-specific checks are added there (e.g. reminders: callback key guessing and throttling, encrypted vendor secrets, links removed after sending).
4. **Standards mapping.** Every finding is mapped to OWASP ASVS 4.0 chapters, HISO 10029:2022 HISF areas and Privacy Act IPP 5 / HIPC Rule 5. The report has *Security by module* and *Standards coverage* tables, and lists standards areas not covered automatically, so it can be filed as evidence for DAA certification, the HISF self-assessment and the PIA.

## Consequences
- References are at chapter/area level; the self-test is evidence towards controls, not certification. The independent penetration test and human code review before live data remain mandatory (spec 11.1).
- Adding a module now requires: a route area, licence gating, permissions, an entry in `ModuleSuites`, and any module-specific probes.
