# ADR 0035 – Triage and day-surgery suitability

Status: Accepted (0.22.0)

## Context
TDS decides before booking, and again whenever something changes, whether a patient is suitable for day surgery in a stand-alone unit. Until now BrightPAS held the
pre-assessment questionnaire (Forms, PAQ) with simple red/amber flags, lab results (Labs) and bookings (Theatre), but no single risk profile, no versioned suitability
criteria and nothing that stopped a booking going ahead on an old or unsigned assessment. Spec 7.25 asks for an initial and post-booking triage built from age, BMI,
comorbidities, history, medicines and results, giving a recommendation that a clinician confirms.

## Decision
- New licensed module **Triage & suitability** (schema `triage`), depending on Patients and Forms; reads Labs and Theatre.
- **Facts → scores → rules → suggestion.** Facts come from the patient record, the latest submitted questionnaire (PAQ-2 adds STOP-BANG, functional capacity,
  recent cardiac events, home oxygen, GLP-1 agonists), labs from the last 6 months and **staff findings** (which override patient answers, with a reason).
  `TriageScoring` is pure and tested: BMI, STOP-BANG (with known/missing items), Clinical Frailty Scale, a Caprini-subset VTE score, RCRI and a suggested ASA with reasons.
- **Suitability rules and medicine prompts** are data, in **versioned rule sets**: draft → approved (by someone with *Approve triage rules*, with an approval record) →
  retired. Only one approved set is used. The shipped set is an **example, not clinically approved**; without an approved set BrightPAS shows the scores but suggests
  "More information needed" and a clinician must write notes to sign off as suitable.
- **Outcomes:** Suitable, Suitable with precautions, More information needed, Anaesthetist review, Not suitable for day surgery at TDS. The suggestion is the most
  serious finding. **BrightPAS suggests; a clinician signs off.** A different outcome needs a reason; only *Review* holders (anaesthetists) can sign off outcomes that
  needed anaesthetist review or confirm ASA. The sign-off sets the questionnaire's triage colour.
- **Re-triage** runs automatically (worker every 5 minutes; also on demand): new answers, new results, booking changed, sign-off older than the set number of days on
  the day of surgery. Each run supersedes the last, keeps the history and shows what changed and whether it got worse.
- **Booking gate:** Theatre asks every `IBookingConfirmGate` before a booking becomes Confirmed. With the triage licence on Full and the setting on, a booking cannot be
  confirmed until triage is signed off as suitable (optionally only for listed procedures). Theatre has no reference to Triage.
- Worklists (to sign off, anaesthetist review, more information, got worse, overdue for surgery date, signed off), patient panel, theatre-list badge with precautions,
  a 3-month report. Every view of an assessment is audited.

## Consequences
- Clinical criteria must be agreed by TDS's anaesthetists and approved in BrightPAS before go-live; until then the hold setting should stay off or every booking
  will need a clinician's notes.
- Scores are decision support only. The Caprini subset and the ASA suggestion are deliberately simple and say so on screen.
- New permissions: View triage (Nurse, Surgeon, Anaesthetist), Assess (Nurse, Anaesthetist), Review, Edit rules, Approve rules (Anaesthetist).
