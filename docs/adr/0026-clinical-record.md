# ADR 0026 – Clinical record (eMR)

Status: Accepted (0.14.0)

## Context
TDS runs day-stay surgery and endoscopy on paper perioperative records. Spec 7.7 asks for an electronic record covering admission, the Surgical Safety Checklist, the intraoperative record, observations, medicines and discharge, linked to the theatre booking.

## Decision
- New module `emr` (schema `emr`), licensed separately. One **Episode** per theatre booking, created on first open by a user with emr.write.
- Stages PreAdmission → Admitted → InTheatre → Recovery → ReadyForDischarge → Discharged/Transferred. Closed episodes are read-only except late notes.
- The checklist follows the WHO/HQSC Surgical Safety Checklist; items and roles are server-side constants so every site gets the same safety rules. Phases must be done in order; Sign Out is blocked by unresolved counts.
- Risk scores are computed server-side (`EmrService.Score`) and stored with the answers. The EWS uses NEWS-style bands until TDS confirms the HQSC NZ EWS chart and escalation policy.
- Controlled drugs: a register per drug with a running balance; every movement witnessed by a second named person; administration only from a patient record.
- Observations carry `Source` (Manual/Device) and `DeviceId` so GE CARESCAPE monitors can feed vitals later (0.17.0) without schema change.
- Discharge enforces the escort/overnight carer rule after sedation or anaesthesia, with an audited override.
- Every write is audited against the patient; patient merge moves episodes.

## Consequences
- Clinical content (checklist wording, score thresholds, EWS bands) must be signed off by TDS clinical governance before live use.
- Paper records stay the legal record until TDS approves go-live.
