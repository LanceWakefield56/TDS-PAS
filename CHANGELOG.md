# Changelog

## 0.10.0 – 2026-11-08 – **Patient records, portals, credentialing, secure exports**
- **Secure exports (ADR 0018):** every export (waitlist, NBRS, payroll, transfer pack) is an AES-256 encrypted, password-protected ZIP by default. Administrators choose "required" (default) or "optional", the minimum password length, upload size and portal link lifetime – in the app (**Security & printers**), not at install. Every export is audited with whether it was protected.
- **Documents & scanning:** attach PDF/JPEG/PNG/TIFF scans and photos to a patient (or booking). File type checked from the content, contents encrypted at rest (Data Protection), SHA-256 tamper check, every opening audited, removal needs a reason (soft delete). Phones/tablets can scan with the camera.
- **Patient identity correction & merge:** name/DOB/sex/NHI corrections and duplicate merges need a second person (Privacy Officer; requester cannot approve). Merges move bookings, appointments, forms, invoices, lab results, referrals, waitlist, letters, SMS, portal items, documents and alerts to the kept record; re-runnable if interrupted. Merged records disappear from search.
- **Wristbands & labels (Zebra ZPL):** network printers set up by the administrator (port 9100); wristband with name, DOB, NHI, MRN, Code 128 barcode and ALLERGY flag; patient labels; ZPL download for USB printers.
- **Emergency transfer pack:** one page from every module (demographics, alerts, procedure, pre-admission flags, labs, GP, documents) with an SBAR handover; transfer recorded; print or protected export.
- **Roster:** shift swaps and give-aways (colleague accepts, roster manager approves); payroll ID and Xero earnings rate per staff member; **Xero payroll timesheet export**.
- **NBRS extract** for publicly funded waitlist entries, with Health NZ codes set by an administrator and a problem list (e.g. missing NHI). Layout to be confirmed against the current NBRS file specification.
- **Credentialing module (new, licensed):** APC, vocational scope, indemnity, TDS approval and other credentials with expiry; scope of practice per service/procedure; status dashboard; **theatre bookings warn** when a credential has expired by the case date or the procedure is outside scope (accepted warnings are audited).
- **Specialist, PA & patient portals module (now available):** read-only portal for specialists (linked by user name) and their PAs – own theatre cases, clinics, waitlist, patient documents; other patients refused. Patient portal: secret link + date of birth (5 tries then 30-minute lock), upcoming procedure and appointments, requests (change/cancel/question/update details) answered by staff, document upload (5 a day, 10 MB). New role **Specialist (portal only)**; Reception can send links.
- **Staff offboarding:** one step switches off sign-in, removes future shifts (listed for cover), stops regular shifts, closes swaps/leave, deactivates roster and clinician entries, removes PA links – audited.
- HealthLink/EDI stays an in-app administrator setting (Letter templates → HealthLink); Theatre 2 and extra rooms via Organisation set-up.
- Self-test: "HTTPS-only site" accepts the port-80 redirect binding (0.9.2 fix carried forward); new module suites for Credentialing and Portals; patient suite covers documents, merges, printing and exports.
- Installer: 12-credentialing.sql, 13-portal.sql; core, patients, roster and referrals schema updates. Guide: H13–H16.
- Tests: 258 + browser walk-through on SQL Server (documents, correction, merge, transfer export, printers, credentialing, offboarding, specialist portal, patient portal, swaps, payroll export, NBRS).

## 0.9.2 – 2026-11-08 – Self-test fix
- Self-test IIS "HTTPS-only site" accepts the port-80 redirect binding (was a false FAIL). No app changes.

## 0.9.1 – 2026-11-08 – Always open the secure page
- Typing `BRIGHTPAS-SRV` or `http://BRIGHTPAS-SRV` now lands on `https://BRIGHTPAS-SRV` instead of "can't reach this page". Setup binds port 80 to the BrightPAS site, but BrightPAS answers plain HTTP **only** with a permanent redirect to https:// (GET/HEAD) and refuses everything else (HTTP 403 "https_required") – no page, sign-in or patient data ever travels unencrypted. HSTS then keeps the browser on https://. `--no-http-redirect` keeps port 80 closed; if another IIS site already uses port 80, Setup leaves it alone and says so.
- Replaced ASP.NET's UseHttpsRedirection (which silently does nothing under IIS when it cannot find the HTTPS port) with an explicit HTTPS-only rule; Setup writes `Https:Port`.
- Self-test: "Unencrypted HTTP disabled" now checks that http:// redirects to https:// and that a sign-in sent over plain HTTP is refused.
- Tests: 244.

## 0.9.0 – 2026-11-08 – **Booking calendar & rostering** (TeamUp replacement)
- **Calendar (ADR 0017):** week/day view of theatre cases and clinic appointments, coloured by clinician, tentative bookings striped, theatre lists in the all-day row; click an empty time to book.
- **Clinic appointments:** consultations, follow-ups, phone calls and pre-admission visits, NO CHARGE flag, tentative, arrived/completed/DNA/cancel; warnings for clashes with the clinician's appointments or theatre lists.
- **Booking details (TeamUp fields):** tentative, payment type, deposit required, check list, "entered in Incisive and Xero", theatre time in/out, trays needed/used, loan instruments & consumables, scope ID (audited).
- **Rostering module:** staff with colours and contracted hours; shifts and all-day entries (office days), tentative shifts striped; regular shifts → roster weeks ahead; leave requests by staff and approval by the roster manager (shows shifts needing cover); hours vs contracted; print. New **Roster Manager** role. Clinicians get a calendar colour.
- Installer: 11-roster.sql; theatre and core schema updates. Guide: H4d, H12.
- Tests: 243 + browser walk-through (calendar, appointments, booking details, roster, leave).

## 0.8.0 – 2026-11-08 – **Administration & set-up**; **Letters & HealthLink**
- **Organisation set-up (ADR 0015):** clinic details for the letterhead (address, phone, HPI numbers); theatres and rooms (switch Theatre 2 on, add procedure rooms); clinician register with HPI CPN; procedure catalogue with usual time, anaesthetic, side-required and fee code (example list included).
- **Regular theatre lists:** templates per surgeon/day/room (weekly or every 2–4 weeks) and "Create lists" for up to 26 weeks; clashes skipped.
- **Roles & permissions:** tick what each role may do, create roles; System Administrator keeps user/module administration; removed default permissions are no longer re-added at start-up.
- **Pick-lists:** theatre lists, bookings, referrals and the waitlist suggest clinicians and procedures; picking a procedure fills in its time and anaesthetic; side is enforced where required.
- **Patient record:** contact details and GP practice can now be edited (audited).
- **Letters & documents module (ADR 0016):** templates with placeholders, drafts from patient/booking/referral, finalise (locks), print with letterhead, void; GP practices; send to GPs via **HealthLink** (HL7 REF^I12; test mode, folder hand-off to the HealthLink client with acknowledgements, or off); letters panel on the patient record; referral letters from the referral screen; guided set-up.
- Fixed: SQL Server could not sort list templates by weekday (found in the browser walk-through).
- Installer: 10-letters.sql; core and theatre schema updates. Guide: G9, H1c, H4c, H11.
- Tests: 238 + browser walk-through (organisation set-up, roles, templates, catalogue booking, GP letter → HealthLink).

## 0.7.0 – 2026-11-07 – **Licence & modules redesign**; **Referrals & waitlist**; self-test fixes
- **Fixed:** "Install licence" seemed to do nothing – the result was shown at the top of the page, out of sight. New **Load licence file** button installs at once and shows the result right under it; paste option kept.
- **Licence & modules (ADR 0014):** one installer for everything; the licence unlocks modules; each module shows *Not licensed / Needs set-up / Active / Read-only*, with a **guided set-up** whose steps are checked live (users, settings, test message, fee schedule…). Modules appear in staff menus once set up; menu badge and home-page notice for administrators.
- **Referrals & waitlist module:** referral intake (source, referrer + HPI, specialist, funder and approval number), specialist triage (accept → waitlist, decline with reason), waitlist with Urgent/Semi-urgent/Routine targets (30/90/120 days, configurable), overdue and due-soon flags, hold (with automatic end) / resume / remove with reasons, **book from the waitlist** onto a theatre list with surgeon/target-date/overrun warnings, automatic return to the waitlist when a booking is cancelled or not attended, completion on discharge, CSV export (formula-injection safe, audited), patient-record panel, home-page counts. New permissions referrals.view/manage/triage added to roles.
- **Security fixes (from the 0.6.0 sandbox log):** patient questionnaire links and SMS vendor callbacks now have **separate rate limits** (a burst of delivery receipts can no longer lock patients out); the callback **key is checked before the licence**, so a wrong key reveals nothing. Self-test: link probe runs before the throttle probe and accepts 404/429; callback results report the real status codes.
- Installer: schema script 09-referrals.sql and core ModuleSetup table; guide: G7–G8 (licence, module set-up), H1b, H4b, upgrade notes.
- Tests: 226 (+ browser walk-through of licence load, wizards, referral → triage → waitlist → booking).

## 0.6.0 – 2026-11-07 – **SMS & email reminders**; security testing in every module
- **Reminders module (ADR 0012):** booking confirmations, day-before reminders, questionnaire links by SMS/email, two-way replies (YES confirms the booking; NO goes to staff; STOP/START opt-out), quiet hours, re-check before sending, retries, per-patient preferences (texts/email allowed, English/te reo Māori), editable wording with placeholder checks (no clinical detail), outbox and replies screens, patient-record panel, test mode with simulated replies.
- **Vendor choice:** Modica is the default; another HTTP SMS vendor, test mode or off can be chosen in Setup (`--sms-provider`, `--email-provider`, or the new install questions) or later on the settings screen. Email via SMTP (Microsoft 365 relay). Vendor passwords encrypted, write-only.
- **Security (ADR 0013):** secure-by-default fallback authorization; CSRF check before sign-in checks; endpoint security inventory test that attacks every endpoint of every module (anonymous, forged, low-privilege) and fails the build on any unprotected or un-gated endpoint; per-module security suites in the self-test (build fails if a module has none); reminders callback probes and encrypted-secret checks; report now has **Security by module** and **Standards coverage** (OWASP ASVS, HISO 10029 HISF, Privacy Act IPP 5 / HIPC Rule 5).
- Installer: schema script 08-reminders.sql; vendor defaults written to appsettings and applied to existing installs; guide: Setup questions, reminders test checklist, Modica appendix, security section.
- Tests: 217.

## 0.5.6 – 2026-11-07 – Installer fix (folder permissions)
- **Fixed:** step 10 "Access to the path … Microsoft.Identity.Client.dll is denied" was a permissions fault, not a locked file (no worker process was running). The folder lock-down in 0.5.0–0.5.5 applied its rule to every existing file with `/T`, leaving those files without an Administrators entry – Setup (and diagnostics) could not even read web.config or older logs.
- Step 10 now first **repairs** both folders (take ownership for Administrators, reset all files to inherit, clear read-only), then copies files. If a file is still blocked it stops the IIS services (W3SVC, WAS), ends worker processes, retries, and starts IIS again.
- Folder lock-down rewritten: permissions are set once on each top folder and everything below inherits; icacls output is logged and failures stop Setup; a final check proves Administrators can write/read and the app pool account is present.
- Tests: 161.

## 0.5.5 – 2026-11-07 – Installer fix (re-install over a running site)
- **Fixed:** step 10 failed with "Access to the path 'C:\Program Files\BrightPAS\Web\Microsoft.Identity.Client.dll' is denied" when the website was already installed and running – its files were locked. Setup now takes the site offline (`app_offline.htm`), stops the app pool and waits until its worker process has exited (ending it after 90 s if needed), then copies files one by one with retries, clearing read-only flags. The site comes back online in the next step.
- Step 9 now only starts the app pool if Windows needs that to recognise its account.
- Tests: 159.

## 0.5.4 – 2026-11-07 – Installer fix (wrong ASP.NET Core Module location)
- **Fixed:** 0.5.3 looked for the ASP.NET Core Module V2 in `System32\inetsrv`, but it is installed under `%ProgramFiles%\IIS\Asp.Net Core Module\V2`, so Setup reported it missing even after a successful Hosting Bundle repair. Setup (and diagnostics) now read the module's registration in IIS `applicationHost.config` and check that the DLL it points to exists. The 0.5.3 diagnosis of the web failure was based on this wrong check.
- Tests: 156.

## 0.5.3 – 2026-11-07 – Installer fix (ASP.NET Core Module missing from IIS)
- **Fixed:** the website could not run because the ASP.NET Core Module was not in IIS, yet Setup reported the Hosting Bundle as "already installed". Cause: the Hosting Bundle had been installed before IIS, and Setup only checked a registry key. Setup now checks the module DLL and its IIS registration, and **repairs** the Hosting Bundle automatically (`/repair`) when `dotnet-hosting-10.x.x-win.exe` is next to Setup.exe; otherwise it explains how to repair it by hand.
- **Fixed:** diagnostics zip left out the Setup logs. Files are now read even while open, failures are recorded in `collection-notes.txt`, and logs next to Setup.exe are included. Diagnostics also report the module DLL / IIS registration / installed Hosting Bundle and the website's actual response to https://localhost/health.
- Tests: 153.

## 0.5.2 – 2026-11-07 – Installer fix (PowerShell scripts never ran)
- **Fixed (serious):** every Setup PowerShell step (install IIS, TLS hardening, IIS hardening, app pool, site, certificate) reported "Done" without running. Scripts were piped to `powershell -Command -`, which silently skips multi-line blocks and still exits 0. Scripts now run from a temporary `.ps1` file with `-File`, and must print a completion marker – exit code 0 without the marker is treated as a failure. **Anyone who ran 0.5.0/0.5.1 must re-run Setup: TLS and IIS hardening were not applied.**
- **Fixed:** step 9 failed with SQL error 15401 "Windows NT user or group 'IIS APPPOOL\BrightPAS' not found" (consequence of the above). The app pool step now creates and starts the pool and waits until Windows recognises its account before creating the SQL login.
- Tests: real PowerShell (pwsh) runs multi-line and failing scripts in CI; marker logic and app-pool script covered. 149 tests.

## 0.5.1 – 2026-11-07 – Installer fix
- **Fixed:** Setup failed at step 5 "Create the PAS database" with `TypeLoadException` (SqlClient native SNI, Windows only). Cause: Setup.exe was published trimmed, which broke SqlClient's native interop on Windows; Linux tests use the managed SNI and could not catch it. Setup is now published **untrimmed** (larger exe). Regression tests assert the publish settings.
- No database, web or licence changes. Re-running Setup over a part-finished 0.5.0 install is safe (steps 1–4 are repeatable).

## 0.5.0 – 2026-11-07 (Phase 0/1) – **BrightPAS**, licensing and four paid modules
- **Renamed BrightPAS** everywhere (projects, UI, installer, IIS site/app pool, database default, paths, `BRIGHTPAS_SQL_PASSWORD`).
- **Licensing (ADR 0009):** ECDSA-signed licence files locked to an installation code; modules enabled per licence (base/paid split decided commercially); optional online activation with a licence server (binding, revocation, offline grace); clock-rollback detection; active-user limit; **licence problems make modules read-only, never hide clinical data**. Licence & modules admin page. Tools: `BrightPAS-LicenceManager` (keygen, issue, inspect) and `BrightPAS-LicenceServer`. Sandbox licence (all modules, development key) shipped for testing.
- **Lab results module (ADR 0010):** HL7 ORU^R01 over MLLP or import; ACKs; duplicate and corrected-result handling; matching by NHI/MRN with date of birth and family-name checks; unmatched queue with audited manual matching; inbox with abnormal highlighting and acknowledgement; results on the patient record. Interfaces page with sample-message generator.
- **Theatre bookings module:** theatre lists per active theatre/room, bookings with side, duration and anaesthetic, planned times with turnover, overrun and double-booking warnings, status flow (booked → discharged, cancel with reason, DNA), reorder, printable day list.
- **Pre-admission forms module (ADR 0011):** secure patient link + date of birth (lockout, expiry), mobile-friendly PAQ-1 questionnaire with save-and-return, automatic Green/Amber/Red flags, nurse triage with outcome and one-step allergy alert.
- **Estimates & invoices module:** fee schedule (example items for sandbox), default 40/20/remainder split with per-item overrides, GST, estimate → invoice, numbering, payments (part paid/paid), void rules, printable tax invoice (internal split hidden on print).
- **UI:** phone layout (☰ menu, tables as cards), licence banner, home page work counts and today's theatre summary, module panels on the patient record.
- **Installer:** schema scripts for labs/theatre/forms/billing; IIS Application Initialization + preload so background services keep running; self-test checks the licence and probes all new APIs and patient-link guessing.
- **Install guide** updated: licence step, test checklists for every module, MLLP appendix, licensing appendix.
- Tests: 141 automated (licensing 13, modules 19, web licence enforcement), plus browser end-to-end across all roles and modules.

## 0.3.0 – 2026-11-05 (Phase 0, increment 3) – product renamed **Galenica**
- **Renamed** to Galenica: projects, namespaces, UI, installer, IIS site/app pool, packages (Galenica-Setup, Galenica-Web), database default `Galenica`.
- **GUI (React + TypeScript):** first-time set-up wizard, sign-in with MFA and phone-app enrolment, recovery codes, "My work today", find/register/view patients, alerts, duplicate check, **user accounts** administration, **audit log** with integrity check.
- **Web sign-in:** cookie authentication (HttpOnly, Secure, SameSite=Strict, __Host- prefix, 15-minute idle timeout, account re-check every 2 minutes), permission-based API authorisation, CSRF header protection, rate limiting.
- **User administration:** create users with roles, change roles, deactivate/reactivate (last-admin and self protection), unlock, reset phone app (MFA), reset password – all audited.
- **Installer:** deploys the website to IIS (Hosting Bundle install, least-privilege database login, locked-down folders, HTTPS-only site with your or a self-signed certificate, health check); **single-server mode** (app connects as IIS APPPOOL\Galenica, no password); **verbose logging** with secret masking.
- **Security, penetration and audit self-test** built into Setup (ADR 0007), with HTML report.
- **Diagnostics collector** (Setup option 5 / `Setup.exe diagnostics`): one zip with Setup logs, security reports, Hosting Bundle log, filtered Windows event logs, IIS/certificate/firewall/permission settings and deployed config (passwords masked). Offered automatically when a run fails.
- **Installation guide** for a single server + Windows 11 workstation, written for junior IT (generated by `scripts/docs/install-guide.js`, shipped with each release).
- **Fixed (found by tests):** audit timestamps stored at millisecond precision so integrity checks re-verify after reload; HSTS header now also sent for https://localhost.
- Tests: 108 automated tests, plus browser end-to-end test and a self-test run against the app.

## 0.2.0 – 2026-11-02 (Phase 0, increment 2)
- **Identity:** accounts (ASP.NET Core Identity, schema `identity`), mandatory authenticator-app MFA with recovery codes, two-step sign-in, lockout after 5 failures, NIST-style password rules, 10 default roles with fine-grained permissions, break-glass access (reason, 30-minute limit, audit, review), first-administrator set-up with a one-time installer code.
- **Patients module:** patient register with NHI validation, MRN allocation (TDS0000001…), NHI and name/date-of-birth duplicate checks, macron-insensitive search (NHI, MRN, name, date of birth), alerts; every create/view audited.
- **Module loading** and per-module database schemas/migrations (`core`, `identity`, `patients`).
- **Web host:** wired to SQL Server; security headers; rate-limited first-administrator endpoints; start-up seeding of roles and the TDS organisation structure.
- **Setup.exe step 2 (install):** IIS install, TLS and IIS hardening (PowerShell), database creation, idempotent schema scripts, one-time first-admin code, preview mode, logs. ~16 MB.
- **Fixed:** audit writer now works with SQL Server's retry-on-failure strategy (found in end-to-end testing).
- **Verified end-to-end on SQL Server 2022:** Setup creates the database → web app starts on it → first administrator created with code + MFA → code cannot be reused.
- Tests: 80 (all passing, including SQL Server integration tests).

## 0.1.1 – 2026-11-02
- Verified against a real SQL Server 2022 (Developer edition container): the core migration applies cleanly and `core.AuditEvents` is created as an APPEND_ONLY_LEDGER_TABLE.
- New SQL Server integration tests (run when `PAS_TEST_SQLSERVER` is set): migrations, ledger table type, audit chain on SQL Server, **UPDATE and DELETE on audit records are rejected by the database**, organisation setup round-trip.
- Tests: 44 (30 core unit, 3 SQL Server integration, 11 installer), all passing.

## 0.1.0 – 2026-11-02 (Phase 0, increment 1)
- Module framework: module manifests, dependency-ordered install plan, circular/missing dependency detection, safe-disable check.
- Organisation model (Organisation → Site → Facility → Location) with TDS default setup; Theatre 2 configured but inactive.
- NHI validation for old (AAANNNC) and new (AAANNAX) formats; test NHIs (Z…) flagged.
- Tamper-evident audit: SHA-256 hash chain, audit writer, SQL Server 2022 append-only ledger table.
- Initial EF Core migration for schema `core` (SQL Server).
- Guided installer Setup.exe (self-contained, Windows) – step 1 prerequisite checks with plain-English fixes and a saved report.
- Tests: 41 automated tests (core 30, installer 11), all passing.
- Docs: data dictionary (core), ADR 0003 (audit), ADR 0004 (installer), installer guide.

## 0.0.1 – 2026-11-02
- Repository skeleton and documentation; specification v1.6.
