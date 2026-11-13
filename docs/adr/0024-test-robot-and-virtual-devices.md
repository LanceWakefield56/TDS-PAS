# ADR 0024 – Test robot with virtual devices and outside systems (0.12.4)

Status: accepted (2026-11-13)

## Context
Lance wants each installed build tested end to end without a human tester: every page as every role, every form, permissions, whether screens make sense, and the parts that normally need real hardware or outside companies (label printers, scanners, phones receiving SMS, email, HealthLink, the laboratory, Xero files).

## Decisions
- A separate tool, **BrightPAS-TestBot** (`tools/BrightPAS.TestBot`), shipped in each release as `BrightPAS-TestBot-<version>.zip`. It runs on the BrightPAS server and drives the real website through **Microsoft Edge** with Microsoft.Playwright (Apache-2.0), so nothing is installed into BrightPAS and no browser is downloaded.
- **Demonstration systems only:** it reads `/api/setup/status` and stops unless the demo sign-in details are present (ADR 0023). It signs in through the screens with the shared demo password and authenticator key.
- **Generic explorer, not hand-written page scripts:** for each demo account it follows the menu, one of each linked record page, tabs and buttons, and fills and saves each form it opens with sensible values. Buttons that delete, send for real, sign out, change security, licensing or module set-up are never pressed (`Crawler.Dangerous`). New pages are covered automatically once they are in the menu.
- **Screen sense** is checked by rules, not opinion: axe-core 4.14 (MPL-2.0, embedded unmodified, run through the browser's developer channel so the site's CSP is not loosened) for WCAG 2.2 AA, plus house rules (no error text/record ids/code words on staff screens, NZ dates, one obvious main action, labels, text ≥ 12 px, targets ≥ 24 px, heading matches menu, fits the window, < 3 s). Screens for technical staff (audit, system log, messaging) are exempt from the id/code/date rules. Every page is screenshotted into the report so a person (or Claude) can review layout.
- **Virtual devices and outside systems** run inside the robot and are reached through BrightPAS's normal settings: a Modica-compatible HTTPS SMS API (its self-signed certificate is trusted in the machine store only while the robot runs), an SMTP server, Zebra raw-9100 printers (ZPL validated and drawn), the DYMO browser print path (window.print intercepted), a scanned page image, a barcode wedge, a HealthLink folder client returning HL7 ACKs, an MLLP laboratory, and export checks (AES ZIP, encrypted PDF, Xero timesheet shape). Settings changed for this are put back at the end.
- Output: `report.html` (problems, warnings, device outputs with label pictures, screen gallery), `results.json`, and a zip for Claude.

## Consequences
- The robot leaves test entries in the demo database; a fresh demo database (new name) resets it.
- It is not a substitute for clinical safety sign-off or for the real HealthLink/Modica/Xero conformance tests before go-live; formats that still need vendor confirmation are labelled as such in the report.
- First runs found and fixed: **Send test** (SMS & email) answered 500 although the message was sent; pages typed into the address bar crashed on refused calls (now a "Not available to you" page for every guarded route); back links and buttons offered to roles that cannot use them; wide tables widening the page; link contrast, unlabelled fields and progress bars (accessibility).
- Every new module: menu entry for the right roles, `Crawler.AllPages`, dangerous buttons, demo data, and a scenario for any new device or outside system (developer guide checklist).
