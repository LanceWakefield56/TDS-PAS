# Specification traceability – BrightPAS 0.18.0 against specification v1.9

This file links every section of the specification to the code that builds it, the decision record (ADR), and the automated tests and test robot steps that prove it. Update it in every release, alongside spec Appendix C (module status) and `CHANGELOG.md`.

Key: **Built** = in the test build, with automated tests and robot coverage, on demonstration data. **Foundations** = the workflow is built, but device or supplier message layouts are BrightPAS profiles until the supplier confirms them. **Partial** = some of the section is built (the gaps are listed). **Planned** = not started. **Operational** = met by installation, process or hardware, not by code.

Nothing is live. Every row still needs sandbox testing, security review and clinical sign-off (spec 11.1, 12) before real patient data.

## 4–5 Architecture, security and privacy

| Spec | Requirement | Status | Where it is built | Proof |
|---|---|---|---|---|
| 4.1–4.3 | Modular monolith, .NET LTS, SQL Server, React | Built | `src/Platform/*`, one project and one schema per module (`src/Modules/*`) | ModuleCatalogueTests, DatabaseDesignTests |
| 4.4–4.6 | Network zones, hardware, standby, immutable backups | Operational | Install guide H-sections; Setup's database step sets the full recovery model | Sandbox install (Lance) |
| 4.7 | Peripheral devices: scanners, Zebra/DYMO, microphones, capture cards, **bedside monitors** | Built / Foundations | Printing (Zpl/Dymo), barcode find, Scribe microphone, endoscopy capture, Devices module (monitors) | Robot: virtual Zebra, DYMO, scanner, barcode, microphone, capture card, GE gateway |
| 5 | MFA, roles and permissions, break-glass, tamper-evident audit | Built | `BrightPAS.Identity`, `BrightPAS.Core/Audit` (ADR 0003, 0005) | IdentityTests, AuditChainTests, EndpointSecurityTests, robot permission checks |
| 5 | Encrypted exports, least data | Built | `SecureExportService` (AES-256 ZIP/PDF) (ADR 0018) | SecureExportTests, robot exports |
| 5.1 | IIS/host/SQL hardening, TLS 1.2+ | Built | Setup steps HardenTls, HardenIis; security self-test (ADR 0006, 0007) | InstallerTests, StandardsTests, `Setup.exe security-test` |
| 5 | Secure coding | Built | .NET analysers (all security and reliability rules) fail the build; dependency audit fails the release; CSP without inline scripts or styles; CSRF header; rate limits | Build, `scripts/build-release.sh`, WebApiTests |

## 6 Interoperability

| Spec | Requirement | Status | Where it is built | Proof |
|---|---|---|---|---|
| 6 table – labs | HL7 ORU results | Built | Labs module, messaging engine handler LabResults (ADR 0010) | MessagingTests, robot virtual laboratory |
| 6 table – SaferSleep | ADT out; anaesthetic record (ORU + PDF) back | Foundations | Devices module: `DevicesWorker.SendAdtAsync`, `AnaesthesiaService` (ADR 0029) | DevicesTests, robot virtual SaferSleep |
| 6 table – patient monitors | Vital signs from the GE CARESCAPE Gateway (HL7 ORU), nurse validation before charting | Foundations | Devices module: `DeviceHl7.ParseMonitor`, `DeviceService` (ADR 0029) | DevicesTests, robot virtual GE gateway |
| 6 table – HealthLink | REF^I12 letters with PDF | Built | Letters module, `HealthLinkService` (ADR 0016) | AdminAndLettersTests, robot virtual HealthLink |
| 6 table – Modica, SMTP | SMS and email | Built | Reminders module gateways (ADR 0012) | RemindersTests, robot virtual phones and inbox |
| 6 table – Xero | Bills and payroll exports | Partial | CSV exports built; API push planned | Robot exports |
| 6 table – NZePS, ACC, insurers, EFTPOS, NHI/HPI live | – | Planned | – | – |
| 6 table – Medow, cloud AI | Optional scribe and drafting | Foundations | Scribe module (ADR 0028) | ScribeTests, robot Medow and cloud drafting |
| 6.1 | DICOM C-ECHO, MWL, MPPS, C-STORE | Built | Endoscopy DICOM node (ADR 0027); engine DICOM listener | EndoscopyDicomTests, robot virtual Olympus tower |
| 6.1 | Storage commitment, query/retrieve | Planned | – | – |
| 6.2.1 | Message store, queues, retry, dashboard, allowed senders | Built | Messaging module and `MessagingHost` (ADR 0021). From 0.17.0, listeners that file clinical data refuse all connections until their allowed list is set | MessagingTests, MessagingWebTests |
| 6.2.1 | Offline fallback: import from file / watched share for every inbound connection | Built | `MessageFile`, `FileImportService`, ImportEndpoints, EndoDicomImport (ADR 0030) | OfflineImportTests, robot offline import |
| 6.2.2 | Workflow engine with tasks and escalation | Planned | – | – |
| 6.2.3 | Visual flow and mapping designers, second-person approval | Built | Messaging UI and service | MessagingTests |
| 6.2.4 | Integrated or standalone engine | Partial | Integrated only | – |
| 6.3 | Regional and GP messaging profiles | Partial | HealthLink REF^I12 | – |

## 7 Modules

| Spec | Module | Status | Where it is built | Proof |
|---|---|---|---|---|
| 7.1 | Patient administration | Built | `BrightPAS.Modules.Patients`; merge participants | PatientServiceTests, PatientsSqlServerTests, robot |
| 7.2 | Referrals, waitlist, booking | Built | Referrals, Theatre (ADR 0017) | ReferralsTests, CalendarAndRosterTests |
| 7.3 | Questionnaires and triage | Built | Forms (ADR 0011) | ModuleTests, robot portal |
| 7.4 | Specialist, PA and patient portals | Built | Portal (ADR 0018, 0020) | PatientRecordsTests, robot portal on a phone |
| 7.5 | Theatre and sessions | Built | Theatre | ModuleTests, CalendarAndRosterTests |
| 7.6 | Clinical record (eMR) | Partial | Emr (ADR 0026); monitor feed and SaferSleep filing (ADR 0029) | EmrTests, DevicesTests. Gaps: NZePS, barcode medicine administration, decision support |
| 7.7 | Billing | Partial | Billing | ModuleTests. Gaps: ACC/insurer claims, Xero sync, EFTPOS |
| 7.8 | Rostering | Built | Rostering (ADR 0017) | CalendarAndRosterTests |
| 7.9 | Assets, CSSD, scopes, inventory | Built | Assets (ADR 0025) | AssetsTests, robot virtual washer |
| 7.10 | Communications and reminders | Built | Reminders (ADR 0012) | RemindersTests, robot |
| 7.11, 7.15 | Coding and statutory reporting | Built (0.20.0; ACC/insurer claims in "claims", AR-DRG/WIES, NBSP extract and NNPAC not yet) | Modules/BrightPAS.Modules.Coding, Web/Api/CodingEndpoints.cs, pages/Coding.tsx; ADR 0033 | CodingTests, robot Coding & statutory reporting |
| 7.12, 7.14 | Audit, certification, QMS; incidents, complaints, risk | Built (0.19.0; surveys, SSI, M&M, PDSA, audit forms with the form designer) | Modules/BrightPAS.Modules.Quality, Web/Api/QualityEndpoints.cs, pages/Quality.tsx; ADR 0031 | QualityTests, robot Quality |
| 7.13 | Credentialing | Built | Credentialing | ModuleTests |
| 7.16 | Letters and transfer pack | Built | Letters (ADR 0016) | AdminAndLettersTests |
| 7.17 | Optional external scribe (Medow) | Foundations | Scribe inbound API (ADR 0028) | ScribeTests, robot |
| 7.18 | Endoscopy (Endobase replacement) | Built | Endoscopy (ADR 0027) | EndoscopyTests, EndoscopyDicomTests, robot |
| 7.19–7.21 | Delayed send, right recipient, de-identified reporting | Partial (0.21.0: estimate emails only – hold period, stop, right-recipient checks) | Estimates (ADR 0034) | EstimatesTests |
| 7.22 | BrightPAS Scribe | Foundations | Scribe (ADR 0028) | ScribeTests, robot virtual speech server |
| 7.23 | Form designer and import | Planned | – | – |
| 7.25 | Triage & suitability | Built (0.22.0; rules need clinical approval) | Modules/BrightPAS.Modules.Triage, Web/Api/TriageEndpoints.cs, Web/Integration/TriageIntegration.cs, pages/Triage.tsx; Theatre IBookingConfirmGate; ADR 0035 | TriageTests, robot Triage & suitability |
| 6.x | Usability: patient journey, My list, day board | Built (0.23.0) | Web/Journey/JourneyService.cs, Web/Api/JourneyEndpoints.cs, components/Journey.tsx, components/TodayList.tsx, pages/DayBoard.tsx; ADR 0036 | DemoDataTests (journey over all demo patients), robot Patient journey |
| 7.26 | Post-op follow-up & SSI | Planned (0.24.0) | – | – |
| 7.24 | Procedure estimates | Built (0.21.0; follow-up statuses, estimate → invoice and reports not yet) | Modules/BrightPAS.Modules.Estimates, Web/Api/EstimatesEndpoints.cs, Web/Pdf/EstimatePdf.cs, pages/Estimates.tsx, pages/EstimatesAdmin.tsx; ADR 0034 | EstimatesTests, EstimatePdfTests, robot Procedure estimates |

## 8–13 Compliance, usability, delivery

| Spec | Requirement | Status | Proof |
|---|---|---|---|
| 8 | Standards matrix (HISF, Privacy Act, HQSC, ANZCA) | Partial | The standards self-test checks the controls the code can check (StandardsTests). Policy items stay with TDS |
| 9 | Usability: plain language, accessibility | Built | Robot accessibility checks (axe-core), phone and tablet sizes, plain-language checks (`Usability.cs`) |
| 10 | Non-functional (performance, availability, logging) | Partial | Serilog logging (ADR 0022), health checks. Load testing is planned |
| 11.5 | Testing strategy | Built | 330 automated tests; test robot (ADR 0024); upgrade test recipe |
| 12 | Governance and clinical safety | Operational | Clinical sign-off items are listed in README "Before production" |

## Where the build differs from the specification

| Item | Difference | Action |
|---|---|---|
| Bedside monitors (GE CARESCAPE) | Built at TDS's request in 0.17.0; checked against the Gateway Technical Manual in 0.19.1 (GE mnemonics, back-collection, DST). Spec v1.9 did not list them | Added to spec 4.7, the 6 interfaces table and 7.6 (spec changelog, 2026-11-15) |
| Monitor waveforms and alarms | Not stored or shown, by design: BrightPAS is a record, and the monitor and central station stay the alarm system | Recorded in ADR 0029 and spec 7.6 |
| DICOM MWL and MPPS | Built in the endoscopy node, not as a general engine service | Appendix C notes this |
| HL7 layouts (GE, SaferSleep) | BrightPAS interface profiles built from the standards | Confirm with real captures before go-live; mapping changes only |
| Import from file (0.18.0) | Added at TDS's request; spec v1.9 does not describe a file fallback | Add to spec 6.2.1 / 4.7 (spec changelog 2026-11-16) |
