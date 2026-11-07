# ADR 0015 – Organisation reference data, role editing and patient contact updates

- **Status:** Accepted · **Date:** 2026-11-08

## Decision
- **Clinic details** (address, phone, email, HPI organisation/facility) live on core.Sites and drive the letterhead and placeholders.
- **Rooms** are edited by administrators (core.Locations); only active Theatre/ProcedureRoom locations can have lists. Theatre 2 is switched on here, no code change.
- **Clinician register** (core.Clinicians, HPI CPN) and **procedure catalogue** (core.Procedures, usual minutes/anaesthetic/side required/fee code) are core reference data, readable by any signed-in user (names only – no patient data) and managed with `admin.organisation.manage`. Forms suggest from them with `<datalist>` but still accept typing, so existing free-text records stay valid.
- **Recurring list templates** (theatre.Templates) generate real lists for up to 26 weeks; clashing or existing lists are skipped, never changed.
- **Role editing:** System Administrators tick permissions per role and can add roles. The System Administrator role always keeps user and module administration. Default permissions are seeded **once** per role (`pas:seeded` claim marker), so removed permissions are not re-added at start-up.
- **Patient contact and GP details** can be edited (`patient.edit`), audited with the list of fields changed. Name, date of birth and NHI changes are a separate, stricter process (later).

## Consequences
- Reference endpoints `/api/organisation/clinicians|procedures` are on the endpoint security allow-list for any signed-in user.
