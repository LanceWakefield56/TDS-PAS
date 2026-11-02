# ADR 0005 – Identity, MFA and break-glass

- **Status:** Accepted · **Date:** 2026-11-02

## Decision
- Use **ASP.NET Core Identity** (EF Core stores, schema `identity`) for accounts, password hashing, lockout and authenticator (TOTP) support, rather than writing our own.
- **MFA is mandatory**: sign-in is two steps (password, then authenticator code or one-time recovery code). An account without MFA can only enrol. Failed passwords and failed MFA codes both count towards lockout (5 attempts, 15 minutes).
- **Passwords** follow NIST SP 800-63B: minimum 12 characters, no forced character mixes, but common, organisation-based, username-containing and repetitive passwords are refused.
- **Permissions** are fine-grained claims (`pas:perm`) attached to roles; ten default roles are created at start-up (idempotent).
- **Break-glass** grants need a reason (≥ 10 characters), last 30 minutes, are audited, notify the Privacy Officer (hook) and must be reviewed.
- **First administrator** is created with a one-time code from Setup (PBKDF2-hashed in `core.SetupState`, valid 7 days), and the code is only consumed after MFA enrolment succeeds.
- Passkeys / FIDO2 security keys are added in a later increment as a preferred factor.

## Consequences
No passwords or codes are stored in plain text. Every sign-in outcome is in the tamper-evident audit log.
