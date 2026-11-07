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

## Next
- ☐ HealthLink: confirm message profile + EDI accounts with HealthLink; live test via the messaging client
- ☐ Patient identity corrections (name/DOB/NHI) with second-person check; patient merge
- ☐ Wristbands and labels (Zebra); document scanning with OCR; specialist/PA portal; patient portal; credentialing; basic emergency transfer pack
- ☐ NBRS extract from the waitlist (Health NZ file specification) for publicly funded patients
- ☐ Sandbox: verify reminders UI end-to-end on Windows; live Modica test (needs Modica account + DMZ callback path)
- ☐ Modica REST v2 gateway; SMS delivery dashboards; te reo Māori templates (TDS to write/review)
- ☐ Security: dependency vulnerability scan + SBOM in the build; DAST (OWASP ZAP baseline) against the sandbox; TDE + backups (spec 4.6)
- ☐ Sandbox verification on Windows (IIS, PowerShell, MLLP) from Lance's logs
- ☐ MLLP over TLS; HL7 ORM orders out; lab inbox per requesting clinician (HPI CPN on user accounts)
- ☐ Xero export of invoices; ACC/insurer claim formats; refunds/credit notes
- ☐ Drag-and-drop theatre list ordering; theatre utilisation report; surgeon/anaesthetist as user links
- ☐ Production vendor key pair (Lance) + code signing of Setup.exe
- ☐ Passkeys/FIDO2, Setup Centre, messaging engine core

## Later phases
See the specification, section 11 (Delivery roadmap).
