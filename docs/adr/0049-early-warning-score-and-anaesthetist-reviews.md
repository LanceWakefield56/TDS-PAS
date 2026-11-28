# ADR 0049 – Early warning score on the day and anaesthetist reviews in triage

Status: Accepted (0.36.0)

## Context
Triage (0.22.0, ADR 0035) suggests day-surgery suitability from the questionnaire, labs and staff findings. Two gaps remained: a patient who is
unwell on the day (abnormal observations on admission) is not re-triaged, and when triage suggests an anaesthetist review there is no way to book
one. The EMR already records observations with an NZ early warning score (EWS, HQSC adult vital signs chart bands). Lance approved 0.36 on 28 Nov 2026.

## Decision
Extends the Triage module (now depends on the EMR module's data); no new licence or permissions.

**Early warning score on the day.** Off until switched on in Triage settings. When on, the latest complete observations in the last 24 hours on the
day of surgery become facts (`ews`, `ews_three`, `ews_at`). EWS at or above the setting (default 5) or any single parameter scoring 3 gives the
finding "anaesthetist to review before surgery" (outcome Anaesthetist review, precaution "repeat observations"); a lower non-zero score adds a
precaution only. The triage worker (every 5 minutes) re-runs triage with the new trigger **Observations on the day** when newer observations exist.
Rule sets can also use the `ews` fact. Bands and thresholds must be confirmed by TDS's anaesthetists.

**Anaesthetist reviews** (`ReviewService`, table triage.Reviews). Staff with Triage assess book a review from an open assessment: anaesthetist
(core.Clinicians of type Anaesthetist), video, phone or in person, NZ date and time, length (default 15 minutes). Rules: in the future, before the
surgery date, no overlap for the anaesthetist, one booked review per assessment. Video reviews get a join link from the meeting link template in
settings (`https://…{code}`, random code); phone reviews keep the number to ring (default the patient's mobile). Only an anaesthetist (Triage review)
records a review as done, with what it found; staff can record did-not-attend or cancel with a reason. Signing off triage stays a separate step.
Worklist tab "Reviews booked" lists the next two weeks.

## Consequences
- The video service is not integrated: any service with per-meeting links works (Teams/Zoom/Doxy-style); patient invitations are sent by staff for now.
- Re-triage on observations only happens on the day of surgery and only when switched on, so upgrades change nothing.
