# ADR 0016 – Letters & documents module and HealthLink

- **Status:** Accepted · **Date:** 2026-11-08

## Decision
- New paid module **letters** (schema `letters`): templates with whitelisted placeholders (unknown placeholders are refused), drafts filled from the patient, booking, referral/waitlist and clinic details, editable while Draft, **locked when Final**, printed with the clinic letterhead, voided with a reason (not after sending – write a correction letter instead). Letters may include the procedure (unlike SMS).
- Five default templates: appointment, admission instructions, GP booking notification, referral received, referral declined.
- **GP practices** (HPI facility number, HealthLink EDI, preferred channel). Patients are linked by the HPI facility number on their record.
- **HealthLink:** final letters to a practice with an EDI account are sent as **HL7 v2.4 REF^I12** (NZ referral/status/discharge structure, HISO 10011.2) with the letter in OBX FT, HL7-escaped. Modes: *Test* (record only, default), *Folder* (write `<control>.hl7` to the outbox folder for the HealthLink messaging client; read ACK files from the inbox folder each minute; AA/CA = acknowledged, others = failed), *Off*. The exact HealthLink profile must be confirmed with HealthLink before live use.
- Permissions: letters.view, letters.write, letters.templates (templates, practices, HealthLink settings).

## Consequences
- PDF generation is by the browser's print; a server-side PDF and CDA/FHIR document formats can be added later. Letters to specialists' own practices and discharge summaries come with the eMR phase.
