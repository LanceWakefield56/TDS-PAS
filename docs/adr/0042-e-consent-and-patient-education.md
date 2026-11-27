# ADR 0042 – E-consent and patient education

Status: Accepted (0.29.0)

## Context
TDS's consent is on paper, signed in the surgeon's rooms or on admission, and the theatre checklist only records "consent form signed". Patients get
mixed written information. Competitor systems offer electronic consent with procedure information. It was suggested as 0.29 after 0.28 and Lance
asked for it on 25 Nov 2026. NZ Code of Rights (rights 6 and 7) requires the information a reasonable patient would expect – the procedure,
benefits, risks, alternatives and what happens without it – and a chance to ask questions; written consent is required for surgery and
anaesthesia. Consent is a process with the surgeon: BrightPAS records it, it does not replace the discussion.

## Decision
Module `BrightPAS.Modules.Consent` (schema `consent`, licence `consent`, depends on `theatre` and `reminders`).

**Information sheets.** Versioned per code (e.g. LAPCHOLE v2): what it is, benefits, alternatives, without the procedure, anaesthetic, getting
ready, recovery, risks (text, frequency Common/Uncommon/Rare/Very rare with the numbers, serious flag – serious risks are always shown first) and
videos/web pages (https only). Draft → Published (approved by a clinician with `consent.approve` who is not the last editor; the previous
published version is retired) → Retired. Published sheets never change. Procedure words pick the suggested sheet for a booking. Three example
sheets are provided as drafts, marked as example wording.

**Consent per booking.** Prepared from a published sheet (the sheet is copied into the consent as JSON, so later edits never change what the
patient read), with risks for this patient and a note. Numbered CN000001… Statuses Draft → Sent → Signed → Confirmed, or Withdrawn.

**Patient link.** Like ADR 0011: 256-bit token, only the SHA-256 hash stored, date of birth as a second factor, five wrong dates lock for 30
minutes, expiry (14 days by default), a new link cancels the old one; sent by text and email (Reminders message kind ConsentLink) and shown to
staff to copy. Public endpoints `/api/e/{token}` are rate-limited per IP. The patient must mark every section read, cannot sign while a question
is unanswered, and ticks three statements (understood; chance to ask; agree). Signer: the patient, or a parent/guardian, welfare guardian, EPOA
attorney or other (relationship required); interpreter recorded. Typed name required; drawn signature optional (PNG, encrypted at rest).
After signing the patient can still withdraw through the link.

**Paper.** A scanned paper form completes the consent (Method Paper) and is filed in the patient record.

**Records.** A MigraDoc PDF (sheet as read, patient-specific risks, questions and answers, signer, signature, interpreter, confirmation) is filed
in the patient record (category Consent) when signed, and replaced by the confirmed copy on the day.

**On the day.** A clinician with `consent.confirm` confirms with the patient; this ticks the theatre booking check "consent". Withdrawal clears it.

**Advice, not rules.** Points to check: question waiting, not signed within 2 days, link expired, under 16 and signed for themselves (competence),
signed by a representative (authority), interpreter. Optional booking gate (`IBookingConfirmGate`, off by default): no confirmation until signed.

**Journey and My list.** A Consent stage between Triage and Booked; to-dos for questions to answer and consents to confirm today.

## Consequences
- Patients read consistent, clinician-approved information at home, can ask questions, and sign before the day; the record shows exactly what
  they read.
- TDS's surgeons must write and approve their own sheets with their own figures before use (the examples are not clinically reviewed).
- Not yet: translated sheets (te reo Māori, other languages), video hosting inside BrightPAS, consent for anaesthesia as a separate sheet,
  surgeon e-signature on the form, consent from the specialist portal.
