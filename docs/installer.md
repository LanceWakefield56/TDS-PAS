# Installer guide (sandbox testing)

Run `Setup-<version>.exe` on the application server VM (e.g. PAS-APP) as administrator. Windows may warn about an unknown publisher (not yet code-signed): in the sandbox choose *More info → Run anyway*.

The menu offers:
1. **Check this server** – read-only prerequisite checks with plain-English fixes; saves `setup-report-<date>.txt`.
2. **Install or upgrade the PAS platform** – guided installation (below).
3. **Preview installation** – shows every step that would run, changes nothing.

## What "Install" does (each step is safe to repeat)
1. Re-checks the server (IIS not yet installed is fine).
2. Installs IIS with only the features the PAS needs; removes WebDAV, FTP, CGI, classic ASP and directory browsing.
3. Secures network encryption: only TLS 1.2/1.3 (restart needed afterwards).
4. Hardens IIS: removes the default site, hides server details, blocks unsafe requests, custom errors only.
5. Creates the PAS database on SQL Server if missing.
6. Creates or upgrades the database tables (core, identity, patients).
7. Creates a **one-time code** for setting up the first administrator – shown once at the end. Write it down. If lost, run Setup again for a new code (until the first administrator has been set up).

A log `setup-install-<date>.log` is saved next to Setup.exe. Restart the server when asked.

## Sandbox tips
- SQL Server 2022 Developer edition on PAS-SQL; allow TCP 1433 from PAS-APP.
- If PAS-SQL uses a self-signed certificate, answer **yes** to the self-signed question (sandbox only).
- Windows authentication works when Setup runs as a domain account with rights on PAS-SQL; otherwise choose SQL login (e.g. `sa` in the sandbox only).
- Small test VMs: `Setup.exe install --ignore-prerequisites` (test labs only).

## Command line
```
Setup.exe check   [--sql-server PAS-SQL[:1433]] [--drive D:\] [--quiet]
Setup.exe install --sql-server PAS-SQL [--database TdsPas] [--sql-user USER] [--trust-server-certificate]
                  [--skip-iis] [--what-if] [--ignore-prerequisites] [--quiet]
```
In quiet mode the SQL password comes from the environment variable `TDS_PAS_SQL_PASSWORD` (never put passwords on the command line).
Exit codes: 0 success/ready · 1 ready with warnings · 2 failed/not ready.

## Coming next
Deploying the PAS web application to IIS (site, app pool, certificate) and the browser-based first-administrator screen.
