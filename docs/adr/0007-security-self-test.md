# ADR 0007 – Built-in security, penetration and audit self-test

- **Status:** Accepted · **Date:** 2026-11-05

## Decision
Setup includes `security-test` (menu option 4, and offered at the end of every install). It is read-only and safe, and produces an HTML and text report (`C:\ProgramData\Galenica\Reports`). It covers:
- **Server (Windows):** TLS protocol registry, firewall profiles, SMBv1, Defender real-time protection, update recency, Guest account, RDP network-level authentication.
- **IIS:** default site removed, directory browsing off, server header removed, risky features (WebDAV/FTP/CGI/ASP) absent, HTTPS-only bindings, app pool identity.
- **Files:** program folder not writable by ordinary users; settings file and key/log folder not readable by ordinary users.
- **Website (black-box penetration probes):** certificate, TLS 1.0/1.1 refused, port 80 closed/redirect, security headers (CSP, HSTS, X-Frame-Options, nosniff, Referrer-Policy, no-store on API), version headers hidden, login required on every protected API, CSRF protection, SQL injection on the login form, reflected XSS, sensitive-file and path-traversal downloads, TRACE disabled, error-detail leakage, password-guessing and set-up-code throttling.
- **Database:** encrypted connection, SQL Server version, `sa` disabled, `xp_cmdshell` off, TDE, recovery model, backups, website account least privilege.
- **Audit:** ledger table type, full re-verification of the audit hash chain (same formula as the app via `Galenica.Shared.AuditHash`), overdue break-glass reviews, failed sign-ins.
- **Application:** MFA for every signed-in account, number of administrators, unused set-up code, non-test patient data in the sandbox.

The in-app **Audit log** screen (Privacy Officer) also has a "Check audit log integrity" button.

## Consequences
The self-test found a real issue during development (HSTS missing for https://localhost) which was fixed. It does not replace the independent penetration test before live use.
