# ADR 0022 – Website logging and error trapping (0.12.2)

Status: accepted (2026-11-10)

## Context
A 500.30 start-up failure in the sandbox (OCR native crash) could only be diagnosed from the Windows Event Log: the website had no log files of its own, no error references and Setup diagnostics did not collect website logs. Lance asked for extensive error trapping and logging so issues can be resolved.

## Decisions
1. **Serilog** (Apache-2.0) file sink alongside the default providers (Windows Event Log on the server): `<data>\logs\BrightPAS-Web-YYYYMMDD.log`, daily, 30 days, 20 MB roll-over, shared/flushed every 2 s. Folder from `Logging:File:Path` (Setup writes it), else next to the Data Protection keys. Level `Logging:File:Level` (default Information; Microsoft categories Warning, EF commands Warning).
2. **Error trapping:** `UsePasErrorHandling` is first in the pipeline: each request gets an 8-character reference (log context property and `X-BrightPAS-Ref` header); unhandled exceptions return a friendly JSON/HTML message with the reference and are logged with method, safe path, user and full exception. Slow (>5 s) and 5xx requests are logged as warnings.
3. **Crash capture:** AppDomain unhandled exceptions (Fatal + flush), unobserved task exceptions, start-up failure to `BrightPAS-Web-startup-error-*.log`; `BackgroundServiceExceptionBehavior.Ignore` so one failing worker is logged without stopping the site. OCR runs out of process (0.12.2) so native crashes cannot stop the site.
4. **Privacy:** no `EnableSensitiveDataLogging` (SQL parameter values never logged); `SafePath` masks `/api/q/{token}`, `/api/my/{token}`, `/api/hooks/sms/{key}`; query strings and bodies are never logged. Logs hold user names, record ids and technical detail only.
5. **Access:** System Administrators see recent entries in the app (Security & printers › Website log); Setup option 5 includes the last 7 log files.
