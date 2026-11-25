# ADR 0041 – Bookings from specialists' rooms

Status: Accepted (0.28.0)

## Context
Surgeons' rooms book TDS cases by phone, email and fax. Competitor systems (Gentu/Genie eBookings and eRequests) let the rooms send the booking
electronically. Lance chose this as the first new feature after 0.27 (25 Nov 2026). TDS already has a read-only specialist/PA portal (practice
assistants linked to their specialists), referrals, the waitlist and theatre booking from the waitlist. Some practice systems can export a
referral as HL7 v2 (REF^I12); others cannot, so a file route and the portal are both needed (every connection has a file fallback).

## Decision
Module `BrightPAS.Modules.EBookings` (schema `ebookings`, licence `ebookings`, depends on `referrals` and `portal`).

**Request.** Patient as sent (NHI, names, date of birth, sex, contact, address, interpreter), specialist (clinician register), procedure (TDS
procedure list item or free text), side, anaesthetic, minutes, service, urgency, funding (insurer + approval number, ACC claim, Health NZ,
self-funded), earliest/latest dates and days to avoid, clinical notes, pre-operative details (weight, height, ASA, diabetes, sleep apnoea,
blood thinners and stopping plan, allergies, other conditions, responsible adult, consent signed), files (referral letter, consent, results –
PDF/JPEG/PNG/TIFF by content, encrypted at rest like patient documents) and the rooms contact. Numbered EB000001…

**Who.** Rooms users (`ebookings.send`: Surgeon, PA, Specialist portal roles) send only for the specialists they are (clinician user name) or work
for (portal delegation) and see only those requests – never other patients' records. The booking office (`ebookings.manage`: Reception / Booking)
sees all requests, can enter one received by phone or fax, and imports files.

**Statuses.** Submitted → Query (TDS asked a question; the rooms' reply puts it back to Submitted) → Accepted / Declined (reason required) /
Withdrawn by the rooms (reason required). A possible duplicate (open request for the same patient and specialist) must be confirmed.

**Points to check** (advice, never blocking): no NHI, ASA ≥ 4, BMI ≥ 40, sleep apnoea, blood thinners, diabetes, allergies, no responsible adult
for a general/sedation/regional anaesthetic, consent not signed, insurer approval or ACC claim number missing, interpreter.

**Patient.** The booking office matches the request to a registered patient (same NHI, or same date of birth and similar name; a different NHI is
refused) or registers the patient from the request through the normal registration and duplicate checks.

**Accept.** Creates a referral (source Specialist, rooms as referrer, specialist's CPN) and accepts it onto the waitlist with the office's choice of
procedure, side, anaesthetic, minutes and urgency; the rooms' dates and pre-op details are written into the waitlist notes; files are filed in the
patient record (source Portal). Booking a date stays on the waitlist screen (theatre's checks apply). The rooms see the waitlist status and
booked date in the portal.

**Files from practice systems.** Upload, or a watched folder checked every two minutes (imported files move to `imported`, failures to `problems`
with a note). BrightPAS booking JSON (one, an array or `{"bookings":[…]}`; example file downloadable) or HL7 v2 REF^I12: PID (NHI from the
identifier with authority NHI), PRD RT/RP (specialist by HPI CPN or name; rooms contact), RF1 (priority, referral id), PR1 (procedure; code
matched to the procedure list), DG1/NTE (notes), AL1 (allergies), OBX (weight 29463-7, height 8302-2, ASA; ED base64 attachments), IN1
(insurer/ACC and approval). The same rooms reference for the same specialist is never imported twice.

**Email.** Optional: the rooms contact is emailed when TDS asks, accepts or declines – the email has only the request number (no patient details)
and uses the Reminders email settings.

## Consequences
- Bookings arrive complete and in one place; the office's decisions are audited and visible to the rooms.
- Patient identity is only ever confirmed by TDS staff; the rooms cannot browse TDS's patient register.
- Booking a theatre date is unchanged (waitlist → list), so theatre and credentialing checks still apply.
- Not yet: the rooms choosing a theatre slot themselves; HL7 acknowledgement messages back to the practice system (files only for now).
