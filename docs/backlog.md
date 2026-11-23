# Backlog

Status: ☐ to do · ◐ in progress · ☑ done

## Phase 0 – Foundations (started 2026-11-02, specification v1.6 approved)
- ☑ Enable NuGet package access for the build environment (api.nuget.org, globalcdn.nuget.org)
- ☑ Test projects (xUnit) – 41 tests
- ☑ SQL Server 2022 test container + opt-in integration tests (`PAS_TEST_SQLSERVER`)
- ☑ Module framework: manifest, dependency resolution, safe disable, module loading, per-module schemas and migrations
- ◐ Identity: accounts, TOTP MFA + recovery codes, lockout, password policy, roles/permissions, break-glass, first-admin bootstrap, cookie sign-in, user administration ☑ · passkeys/FIDO2, force password change at first sign-in, break-glass screen ☐
- ☑ Audit: hash chain + SQL Server append-only ledger table + writer (ADR 0003)
- ☑ Organisation model + TDS default setup (Theatre 1 active, Theatre 2 inactive) + initial migration
- ☑ Patients module: register, NHI validation, MRN, duplicate checks (macron-insensitive), search, alerts, audit
- ☐ Messaging and workflow engine core: message store, queues, retry, dashboard
- ☑ UI shell (React + TypeScript): set-up wizard, sign-in + MFA, My work today, patients, user accounts, audit log
- ☑ Security, penetration and audit self-test (ADR 0007)
- ☐ Setup Centre (browser-based module installer) · ☐ messaging engine core · ☐ TDE + backups (spec 4.6)
- ◐ IIS/TLS hardening scripts in Setup ☑ · app security headers ☑ · verification in sandbox ☐
- ☑ Guided platform installer: checks, IIS install + hardening, database + schema, first-admin code, web deployment to IIS, single-server mode, verbose logging (ADR 0006, 0008)
- ☐ Developer guide and data dictionary generation

## Increment 0.5.0 (done)
- ☑ Renamed to **BrightPAS**
- ☑ Licensing: signed licence files, installation code, online activation + licence server, read-only safety rule, Licence Manager (ADR 0009)
- ☑ Lab results module: HL7 ORU via MLLP/import, safe matching, unmatched queue, inbox + acknowledgement (ADR 0010)
- ☑ Theatre bookings module: lists, bookings, planned times, overrun/double-booking warnings, status flow, print
- ☑ Pre-admission forms module: secure patient link + DOB, PAQ-1 questionnaire, auto-flags, nurse triage, allergy → alert (ADR 0011)
- ☑ Estimates & invoices module: fee schedule, 40/20/remainder split, GST, numbering, payments, print
- ☑ Phone-friendly layout; Interfaces page; self-test covers new APIs and licence

## Increment 0.6.0 (done)
- ☑ SMS & email reminders module, Modica default + vendor choice in Setup (ADR 0012)
- ☑ Security testing built into every module, mapped to standards (ADR 0013)

## Increment 0.7.0 (done)
- ☑ Licence & modules redesign: one-click licence load with visible result; module states; guided set-up per module with live checks (ADR 0014)
- ☑ Referrals & waitlist module: intake, triage accept/decline, waitlist with priority targets and overdue flags, hold/resume/remove, book theatre from the waitlist, sync with theatre status, CSV export, patient panel
- ☑ Self-test fixes from the 0.6.0 sandbox log: separate rate limits for patient links and SMS callbacks; callback key checked before the licence; probe order and reporting

## Increment 0.8.0 (done)
- ☑ Organisation set-up: clinic details, rooms, clinician register, procedure catalogue; regular list templates; role editing; patient contact/GP editing (ADR 0015)
- ☑ Letters & documents with HealthLink REF^I12 (ADR 0016)

## Increment 0.9.0 (done)
- ☑ Booking calendar, clinic appointments, TeamUp booking fields; Rostering module (ADR 0017)

## Increment 0.10.0 (done)
- ☑ Secure exports (AES-256 ZIP, admin policy), security settings in-app (ADR 0018)
- ☑ Documents & scanning; identity corrections + merge (second-person check); wristbands & labels (Zebra); emergency transfer pack
- ☑ Roster swaps + Xero payroll export; NBRS extract; staff offboarding
- ☑ Credentialing module; Specialist/PA & patient portals module

## Increment 0.10.1–0.10.2 (done)
- ☑ No heaps, indexes, cross-module FKs, reporting views/procedures (ADR 0019); DYMO labels; 7-Zip link; clinicians/rooms by id

## Increment 0.11.0 (done)
- ☑ Password-protected PDF letters/reports (AES-256) alongside encrypted ZIP; OCR + per-patient document search (ADR 0020)
- ☑ Portal link by SMS (manual + automatic on booking); questionnaire and estimate acceptance in the patient portal

## Increment 0.12.0 (done)
- ☑ Messaging & integration engine (ADR 0021): message store, queues/retry/dead letter, dashboard, alerts, replay; MLLP, DICOM node, REST in/out, folders, SQL connector; HL7/FHIR/JSON/DICOM/CDA-XML/X12
- ☑ Visual flow designer + test harness; mapping designer with versions, second-person approval, rollback; translation tables
- ☑ MCP server (read-only, no content); AI troubleshooting assistant (opt-in, de-identified) + built-in explainer

## Increment 0.12.4 (done)
- ☑ Test robot with virtual devices and outside systems (ADR 0024); fixes it found (send test 500, anaesthetist forms, typed-address guards, accessibility)

## Next
- ☐ Test robot: calendar day columns with many overlapping bookings are unreadable (text cut to "0 T…") – needs a denser layout or a list view for busy days
- ☐ Test robot: DICOM C-ECHO/C-STORE virtual modality; REST interface (key) and folder interfaces; questionnaire completed end to end by the virtual patient; reset-demo option
- ☑ Assets & CSSD (spec 7.9) – 0.13.0 (ADR 0025)
- ☐ Assets & CSSD follow-ups: confirm Belimed WD 290 IQ log layout with a real file; scan trays at the theatre case screen; Xero API push for bills; ultrasonic cleaner cycles; label stock layout check on the real Zebra
- ☑ Clinical record (eMR) – 0.14.0 (ADR 0026)
- ☑ Endoscopy – 0.15.0 (ADR 0027)
- ☑ Endoscopy follow-ups – 0.15.1: NBSP extract, report to GP by HealthLink, drawing on pictures, 4K/MP4, TIFF
- ☐ Confirm CV-1500 DICOM options with Olympus NZ; confirm NBSP extract layout with the National Screening Unit
- ☑ BrightPAS Scribe foundations – 0.16.0 (ADR 0028)
- ☐ Scribe next: PAS-SPEECH server build and accuracy testing, NZMT dose checking, speaker diarisation, confirm Medow API
- ☑ Devices & SaferSleep foundations – 0.17.0 (ADR 0029)
- ☑ Monitor feed checked against the CARESCAPE Gateway Technical Manual: GE mnemonics/units, back-collection, DST repeated hour – 0.19.1
- ☑ Clinical coding and statutory reporting (spec 7.11): coder workbench, edit checks, advice library, NMDS load file, NBRS batches – 0.20.0 (ADR 0033)
- ☑ Endoscopy video retention 30 days with AES-256 archive to share/USB – 0.20.0
- ☑ Procedure estimates (TDS Estimates v1.5 moved in): builder, price list, add-ons, templates, PDF, email with hold and right-recipient checks, Incisive, imports – 0.21.0 (ADR 0034)
- ☐ Estimates next: follow-up statuses (accepted, declined, deposit paid, booked, prior approval, expiry list), estimate → Billing invoice, reports, layout builder; standalone app on a shared library; open business items (Incisive naming, SMTP, Veins/skin graft/Dr Butcher/MyoSure fees, costing defaults)
- ☐ Delayed send and right-recipient checks for letters and reminders (7.19–7.20), de-identified reporting (7.21) – moved to 0.22.0
- ☐ Coding next: obtain the Health NZ clinical code table and code tables (purchaser, specialty, facility, domicile, CPAC); NBSP extract layout; AR-DRG/WIES grouping if a contract needs it; NNPAC if TDS reports outpatient events
- ☐ Optional: ADT to the CARESCAPE Gateway so monitors can "Request Admit" the patient by unit and bed
- ☐ Devices next: capture real CARESCAPE Gateway messages and adjust the mapping; agree the anaesthetic record profile with SaferSleep; clinical sign-off of chart intervals and plausibility limits; Olympus tower status on the Monitors page; overdue-validation prompt on the whiteboard
- ☑ Import from file / watched share for every inbound connection – 0.18.0 (ADR 0030)
- ☐ Check offline import against real sample files (Delta lab queue, CARESCAPE export, Olympus USB export, SaferSleep export)
- ☑ CSSD steriliser cycle records from USB / log folder with curve and plateau checks – 0.19.0 (real Sterilizer 1 files)
- ☑ Daylight saving and leap years handled throughout – 0.19.0 (ADR 0032)
- ☑ Quality, incidents and complaints (spec 7.12, 7.14) – 0.19.0 (ADR 0031)
- ☐ Quality next (with the form designer, 0.22.0): patient surveys/PROMs, SSI surveillance, M&M review, PDSA cycles, internal audit forms; HQSC/ACC notification files when formats are confirmed
- ☐ Messaging: workflow engine (human tasks, timers, escalation), MLLP/DICOM over TLS, DICOM MWL/MPPS (with endoscopy), drag lines in mapper, C# extension scripts
- ☐ PDF for remaining reports (NBRS problems list, payroll summary, audit extracts); letters PDF attached to HealthLink/email sends
- ☐ Confirm NBRS file layout with Health NZ; confirm Xero payroll import format (or Xero Payroll API)
- ☐ HealthLink: confirm message profile + EDI accounts with HealthLink; live test via the messaging client
- ☐ Sandbox: verify reminders UI end-to-end on Windows; live Modica test (needs Modica account + DMZ callback path)
- ☐ Modica REST v2 gateway; SMS delivery dashboards; te reo Māori templates (TDS to write/review)
- ☑ Dependency vulnerability scan in the release build (0.17.0)
- ☐ Security: SBOM in the build; DAST (OWASP ZAP baseline) against the sandbox; TDE + backups (spec 4.6)
- ☐ Sandbox verification on Windows (IIS, PowerShell, MLLP) from Lance's logs
- ☐ MLLP over TLS; HL7 ORM orders out; lab inbox per requesting clinician (HPI CPN on user accounts)
- ☐ Xero export of invoices; ACC/insurer claim formats; refunds/credit notes
- ☐ Drag-and-drop theatre list ordering; theatre utilisation report; surgeon/anaesthetist as user links
- ☐ Production vendor key pair (Lance) + code signing of Setup.exe
- ☐ Passkeys/FIDO2, Setup Centre, messaging engine core

## Later phases
See the specification, section 11 (Delivery roadmap).
