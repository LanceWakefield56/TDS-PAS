# ADR 0036 – Patient journey, My list and the theatre day board

Status: Accepted (0.23.0)

## Context
By 0.22.0 BrightPAS had 21 modules and about 45 menu items. Each module worked, but staff had to know which module held the next job for a patient
(referrals → waitlist → estimate → questionnaire → triage → booking → reminders → admission → theatre → recovery → discharge → follow-up → invoice → coding).
TDS asked for a system "anyone can use", following the patient's journey rather than the software's structure.

## Decision
- **Journey engine** (`Web/Journey/JourneyService.cs`) reads every licensed module (read only) and builds, for each episode of care, the stages in order,
  each Done / Now / Waiting (on the patient or someone outside TDS) / Blocked / Later / Skipped, with a plain-English detail and the screen that does the job.
  The **next step** is the first Blocked or Now stage (otherwise the first Waiting). Estimates are optional and never the next step once the case has moved on.
  Episodes are anchored on the theatre booking; waitlist entries without a booking, referrals not yet on the waitlist and stand-alone estimates are episodes too.
  Current episode first (soonest upcoming or still in the building), then finished ones.
- **The journey never changes data.** Every action opens the existing screen (or a panel on the patient page) that already enforces the rules, permissions and
  audit, so business rules stay in one place. Unlicensed modules are skipped, not shown as gaps.
- **My list** (`/api/journey/today`, home page): everything this person can act on, across modules, grouped Theatre today / Before surgery / After surgery,
  most urgent first, with "Start here". Items the person has no permission for are not shown.
- **Theatre day** (`/day`): today's patients in five columns (Coming in, Admitted, In theatre, Recovery, Gone home), refreshed every 30 s; one tap moves a
  patient on through the existing theatre status rules; discharge goes through the clinical record when the eMR is licensed (it has its own checks).
- **Menu:** three primary items (My list, Patients, Theatre day) plus collapsible groups Before surgery / Day of surgery / After surgery / Running TDS /
  Administration that open on the page you're on. Every page remains reachable.
- Viewing a patient's journey is audited (PatientJourney View).

## Consequences
- A new module must add its stage(s) to the journey and its work to My list (developer guide checklist).
- The robot checks, for each role, that every kind of action on My list opens a screen that person can use – the "no menu needed" test.
- Follow-up (0.24.0) replaces the simple follow-up-call stage with plans, check-ins and SSI surveillance.
