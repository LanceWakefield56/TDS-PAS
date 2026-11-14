# BrightPAS Test Robot

Tests a **demonstration** BrightPAS by using it like staff would, without a person clicking. Full steps: Installation Guide, Part E11.

## Run it

On the BrightPAS **server**, in an **administrator** Command Prompt:

```
cd C:\BrightPASTestBot
BrightPAS-TestBot.exe --url https://localhost
```

It takes 25–40 minutes. When it finishes it prints the report folder (`TestBot-results-<date>`, open `report.html` in Edge) and a zip file of the same name. **Send the zip file to Claude.**

It refuses to run against a database that is not a demonstration database (Setup `--demo-data`).

## What it checks

| Area | What |
|---|---|
| devices (endoscopy) | Virtual Olympus tower over DICOM (echo, worklist, picture, MPPS, unmatched picture); virtual capture card (browser fake camera: pictures and a recorded video); clinical and patient endoscopy reports with pictures |
| devices (monitors, SaferSleep) | Virtual GE CARESCAPE gateway (HL7 vital signs to port 12580): unknown monitor and impossible values refused, monitor connected on screen, nothing charted before validation, validated reading in the chart, wrong-NHI monitor refused. Virtual SaferSleep (listens on 12582, sends records to 12581): ADT received, record with PDF filed, correction, duplicate, wrong patient, unmatched record filed by hand |
| devices (scribe) | Virtual speech server + language model: ambient scribe session with the browser's virtual microphone, de-identified cloud drafting, dictation, Medow draft by token |
| signin | Every demo account signs in with password + authenticator code; wrong password refused; not signed in = sign-in page only, patient data refused |
| pages | Every page in each account's menu, plus one of each record page linked from them (patient, letter, invoice, lab result…) opens without errors, refused server calls or script crashes |
| buttons | Each tab and harmless button is pressed (buttons that delete, send for real, sign out, change security or set-up are left alone and listed as skipped) |
| forms | Each form a button opens is filled with sensible test values and saved: it must save, or explain what is wrong – never crash or refuse silently |
| permissions | Pages hidden from an account's menu, typed into the address bar, must not show data |
| screens | Accessibility (axe-core, WCAG 2.2 AA) and house rules: no computer error text, record ids or code words on screen, NZ dates, one obvious main button, labelled fields, readable text, tap targets ≥ 24 px, heading matches the menu, page fits the window, loads in < 3 s |
| sizes | Tablet (1024×768) and phone (390×844): menu button works, pages fit |
| devices | Virtual devices and outside systems, start to finish (below) |

## Virtual devices and outside systems

| Device | Test |
|---|---|
| SMS company (Modica-compatible, https://localhost:9443) + virtual phones | Test SMS received; delivery receipt marks it Delivered; the patient replies and the reply appears; every reminder due now is sent and read for leftover {placeholders}, length and gaps |
| Email server (localhost:2525) + inboxes | Test email received |
| Patient portal on a phone | Portal link sent by SMS → opened on a phone-size screen → date of birth → question sent → appears in Portal requests |
| Zebra printers (localhost:9150) | Wristband and labels printed from the patient record; ZPL checked (identifiers, barcode, text inside the label) and drawn as a picture |
| DYMO printer | Labels page opens the print window; label shows the identifiers inside its edges |
| Document scanner | A scanned referral letter is attached; text recognition must find a word in it |
| Barcode scanner | NHI typed at scanner speed + Enter finds the patient |
| HealthLink | GP letter sent; outbox file checked as HL7 v2.4 REF^I12; acknowledgement returned; letter must show Acknowledged |
| Laboratory (MLLP, port 12575) | ORU^R01 result sent; ACK must be AA; result matched by NHI and shown with value and comment |
| Exports | Payroll export is AES-encrypted ZIP; unprotected export follows the policy; Xero timesheet shape checked when readable; letter as password-protected PDF |

Settings changed for these tests (SMS & email, HealthLink, default printers) are put back afterwards unless `--keep-settings` is used. The virtual SMS company's certificate is trusted only while the robot runs.

## Options

`--depth quick|full`, `--only <areas>`, `--skip <areas>`, `--headed`, `--slow <ms>`, `--keep-settings`, `--no-simulators`, `--no-trust`, `--app-pool <name>`, `--browser msedge|chrome|chromium`, `--out <folder>`. Run with `--help`.

## For developers

- .NET 10 console app using Microsoft.Playwright 1.63 (Apache-2.0) with the installed Microsoft Edge (`--browser msedge`), so no browser download is needed on the server. axe-core 4.14 (MPL-2.0, unmodified) is embedded for the accessibility rules.
- Linux (development container): `--browser chromium` uses the Playwright Chromium; run as root so the simulator certificate can be trusted (`update-ca-certificates`), and restart the website once after the first trust.
- Files: `Crawler.cs` (accounts, pages, buttons, forms, typed addresses, sizes), `Usability.cs` (screen checks), `Sims.cs` (virtual devices), `Scenarios.cs` (device tests and settings restore), `Report.cs` (report.html, results.json, zip).
- **Every new module:** make sure its pages are in the menu for the right roles (the robot follows the menu), add its pages to `Crawler.AllPages`, add any button that must never be pressed by the explorer to `Crawler.Dangerous`, and add a scenario in `Scenarios.cs` for any new device or outside system.
