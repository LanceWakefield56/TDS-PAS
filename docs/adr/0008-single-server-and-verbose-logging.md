# ADR 0008 – Single-server installation and verbose installer logging

- **Status:** Accepted · **Date:** 2026-11-05

## Decision
- **Single server** (IIS + SQL Server on one machine) is a supported layout for the sandbox and small sites. When SQL Server is local and Windows authentication is used, the website connects as its own virtual account `IIS APPPOOL\Galenica` (created before the SQL login), so no database password exists anywhere. Separate-server installs keep the generated least-privilege SQL login (or `--app-windows-login`).
- **Verbose logging:** every run writes `C:\ProgramData\Galenica\Logs\Galenica-<Install|Check|SecurityTest>-<date>.log` (copied next to Setup.exe): header with OS/user/options, all server facts, each step with timings, every PowerShell script and its full output (scripts are wrapped to report the exact error line), every SQL batch, full exception details. Passwords, connection-string passwords and the one-time code are always masked. `--verbose` shows the detail on screen too.

## Consequences
A junior IT person can send one log file and the exact failure is visible.
