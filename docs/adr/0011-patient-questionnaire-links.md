# ADR 0011 – Patient questionnaire links and nurse triage

- **Status:** Accepted · **Date:** 2026-11-07

## Decision
- Patients complete the pre-admission questionnaire without an account, using a **secret link** (256-bit random token; only its SHA-256 hash is stored) **plus their date of birth** as a second factor. Five wrong dates lock the link for 30 minutes; links expire after 14 days, close on submission, and a new link cancels older ones. The public endpoints are rate-limited per IP and reveal nothing for unknown tokens.
- The questionnaire (version PAQ-1) is defined in code and served to the page, so the same definition drives the patient form, validation and the nurse's view.
- Automatic flags (BMI, pacemaker, family anaesthetic history, cardiac, OSA, insulin, anticoagulants, allergies, airway, pregnancy, infection risk…) **suggest** Green / Amber / Red. A nurse always reviews and records the final category, outcome and notes; reported allergies can be added as a patient alert in one step.

## Consequences
- Sending links by SMS/email comes with the reminders module; for now staff copy the link.
- The phone must reach the server (internal network/VPN) until the patient portal is published through the DMZ design in the specification.
