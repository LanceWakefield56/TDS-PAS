# ADR 0038 – Send safety, de-identified reporting and the form designer

Status: Accepted (0.25.0)

## Context
Spec 7.19–7.21 and 7.23: stop information going to the wrong person and let staff undo a send; produce reports for the Board, benchmarking and research
without exposing patients; and one no-code designer for every form, with import from existing forms.

## Decision
**Send safety (7.19, 7.20)**
- Messages a person sends wait in the **outbox** (default 2 minutes; bulk reminders 10 minutes; automatic replies and system messages go at once) –
  `ReminderService.HoldFor`. **Undo send** cancels while held (reason kept, secret link removed). Message types can be set to need a **second person's
  release** (not the writer). **Send now** for clinically urgent messages needs *Manage SMS & email*, a reason, and is audited.
- **Sent to the wrong person**: held → recalled; sent → a correction notice to the same address with no patient details; letters → marked and a
  retraction letter (template RETRACT) drafted to the same recipient. Either way a **privacy incident** (InformationPrivacy) is opened in Quality asking
  the Privacy Officer for the Privacy Act notifiable-breach assessment.
- **Right recipient**: HealthLink letters to a practice that is not the patient's GP of record are stopped unless a reason is given; an attachment naming
  another NHI is stopped. A plain-words confirmation ("Send Jane Smith's letter … to … ?") before sending. Blocked sends, overridden warnings and
  wrong-recipient reports are audited and counted by the Quality indicator **"Misdirection near misses"**.
- Email and portal content were already link-based (Section 6) and portal access can be revoked; recall therefore disables links rather than content.

**De-identified reporting (7.21)** – module `BrightPAS.Modules.Research` (schema `research`), licensed with Coding & statutory reporting.
- Datasets (operations, SSIs, follow-up) are supplied by the web app (`IDatasetSource`) with each column classified: direct identifier (never released),
  birth date (age / 5-year band), date (exact / month), place (region), clinician (coded A, B…), free text (removed unless pseudonymised), ethnicity
  (prioritised level 1; released only with a Māori data governance note), category, number.
- Levels: **pseudonymised** (study IDs; linking key stored separately, Privacy Officer only), **de-identified** (k-anonymity check on age band, sex,
  procedure, region, month; rows in groups under k flagged or removed), **aggregate** (counts with small-cell and secondary suppression).
- Patient-level extracts need **Privacy Officer approval** (not the requester); research needs an ethics reference. Files are watermarked, encrypted
  (secure export) and logged. `DeidEngine` is pure and tested.

**Form designer (7.23)** – in the Forms module (schema `forms`: Designs, DesignVersions, Fields, FieldOptions, Entries, EntryValues – one row per value).
- Field types: heading, short/long text, number (range), date, yes/no, pick one/any (with scores), signature (typed), calculated (bmi, sum, count), NHI
  (checksum). Show-if logic, required, sections, print header/footer.
- **Bound to the record**: fields can map to data elements; on completion allergies become patient alerts and measurements (weight, height, neck, BP,
  frailty) become triage findings (`IFormDataSink`, web app).
- Versions: draft → testing → published (one) → retired; entries finish on the version they started. Clinical, consent and patient forms are published
  only with *Clinical Governance approval* and a recorded note; licensed/third-party forms need the licence recorded.
- Import: CSV question list, FHIR R4 Questionnaire (import and export), fillable PDF (AcroForm fields via PDFsharp 6.2). Imports open as drafts.

## Consequences / not yet done
- Scanned/flat PDF and Word forms with AI field detection, handwriting extraction from completed paper forms, SharePoint bulk import, body maps, photo
  fields, drawn signatures, the patient-portal/kiosk channel for designer forms, scheduled saved reports and running reports from a replica are in the backlog.
- Outbox holds apply to messages sent through BrightPAS SMS & email; letters already need an explicit send.
