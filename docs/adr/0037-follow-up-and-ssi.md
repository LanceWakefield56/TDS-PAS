# ADR 0037 – Post-operative follow-up and SSI surveillance

Status: Accepted (0.24.0)

## Context
Spec 7.26 asks for every patient to be followed up after discharge (calls, wound checks, reviews), patient check-ins by text with alerts, escalations
tracked to closure, and surgical site infection (SSI) surveillance to HQSC/CDC definitions with GP letters, rates, SPC and an HQSC export.
Until now the clinical record only held a single "follow-up call on" date.

## Decision
- New licensed module **Post-op follow-up and SSI surveillance** (schema `followup`), depending on Patients and Theatre; uses Quality (closed days,
  incidents) and Reminders (texts). Operation facts (implant, wound class, skin prep, duration, prophylactic antibiotic and timing, recovery temperature,
  ASA/BMI/diabetes/precautions, surgeon) come through `IOperationFacts`, implemented in the web app from Stock, the clinical record and Triage – the module
  has no reference to them.
- **Plans** (example plans shipped, to be agreed): steps with type (call, wound check, review, SSI check), method (phone, clinic, surgeon, patient check-in),
  days after the operation (or NZ clinic days), days with an implant (SSI check 90), "high risk only". The first plan whose words match the procedure is used,
  else the default. **Clinic days** = weekdays that are not national public holidays, not Nelson Anniversary Day and not the extra closed days set in Quality
  (the 25 Dec–15 Jan rule is for complaints only). Dates are NZ calendar dates, so daylight saving cannot move them.
- **Contacts** are created by a worker within 5 minutes of discharge (once per booking). Phone contacts: script answers, done / no answer (next clinic day,
  up to the plan's attempts, then unreachable), escalation (advice, GP review, return to TDS, surgeon contacted, ED) with a note, closed with an outcome,
  optional Quality incident.
- **Patient check-in**: a secret link (hash stored, link only in the message until sent) + date of birth, 5 wrong attempts lock for 30 minutes, links sent
  only to the configured https public address (never taken from a request – prevents host-header link poisoning). Warning answers create a call back today,
  show on Concerns and My list, and text the duty nurse with no patient details. Unanswered links become phone calls; patients with no mobile/email get a call.
- **SSI**: wound answers suggest an SSI (CDC: purulent drainage; antibiotics for the wound; wound opened with signs; redness + swelling + fever). The record keeps
  onset, signs, source, treatment and the risk factors at the time (one row per factor – 3NF), numbered SSI-yyyy-nnn. Only the infection prevention lead
  (*Confirm SSI classification*) classifies superficial / deep / organ-space / not an SSI, with the evidence for deep, organ/space and not-SSI. A confirmed
  SSI can raise a Quality incident (category Infection; harm from class and treatment). Surveillance window 30 days, 90 with an implant.
- **GP letter**: the worker drafts the "Wound infection surveillance (GP)" letter (template GPSSI) for operations under surveillance when Letters is licensed.
- **Reporting**: completion, concerns, escalations, returns, satisfaction, SSI by procedure (denominator = operations with an SSI check) and surgeon;
  Quality indicators "Surgical site infections" and "Follow-up contacts completed" (SPC); CSV export for HQSC/private hospital SSI reporting (no names)
  through the encrypted export.
- Journey: the follow-up stage shows the next contact, concerns and open escalations; My list shows calls due, concerns (most urgent), escalations to close
  and SSIs to classify.

## Consequences
- The script wording, warning rules and plans are examples and need clinical approval before go-live.
- The HQSC SSI Improvement Programme covers hip/knee arthroplasty and cardiac surgery, which TDS does not do; the export is TDS's own surveillance file and
  must be checked against any programme's current data specification before submission.
- New permissions: View, Record, Plans, Confirm SSI, Report (Nurse: view/record; Surgeon: view/record; Anaesthetist: view; Quality Manager: view/plans/SSI/report).
