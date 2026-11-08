# ADR 0018 – Secure exports, patient record tools, portals and credentialing (0.10.0)

Status: accepted (2026-11-08)

## Context
Lance asked for the next batch (swaps/payroll, attachments/scanning, wristbands, specialist/PA and patient portals, credentialing, transfer pack, NBRS, identity correction/merge) and that (a) administrators control settings in the app rather than at install, and (b) every export can be encrypted and password-protected.

## Decisions
1. **Exports:** one `SecureExportService`; default policy **Required** = AES-256 encrypted ZIP (SharpZipLib, MIT). Password sent in the POST body, never stored or logged; minimum length set by the administrator. Optional mode allows unprotected files but audits them as such. ZIP chosen over password-PDF because it covers CSV and HTML alike; password-PDF letters are backlog.
2. **Settings:** `core.Settings` key/value table changed by administrators in the app (audited). Applies to export policy, upload limit, portal link lifetime. HealthLink, printers, NBRS codes, credentialing rules and PA links are likewise in-app.
3. **Documents** live in the Patients module (base system) and are encrypted with ASP.NET Data Protection (`IDocumentCipher`); type detected from file signature; SHA-256 stored for tamper detection; served under a sandbox CSP.
4. **Identity changes** require a second person (`patient.merge`, Privacy Officer by default). Merges call an `IPatientMergeParticipant` per module; updates are idempotent so a failed merge is finished by approving again (no distributed transaction).
5. **Wristbands/labels** use ZPL sent to network printers on ports 9100–9199/6101 only (limits SSRF); command characters are stripped from patient text.
6. **New licensed modules:** `credentialing` (schema `cred`) and `portal` (schema `portal`). Credential checks are warnings at booking (clinical judgement stays with staff), recorded when accepted.
7. **Patient portal** reuses the questionnaire pattern: hashed one-time token + date of birth on every call, lock-out, own rate-limit policy (`patient-portal`), no browser storage.
8. **Specialist portal** grants nothing beyond the clinician's own patients (linked through bookings, appointments or waitlist); PA access via explicit delegation.
