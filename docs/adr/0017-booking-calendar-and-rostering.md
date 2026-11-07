# ADR 0017 – Booking calendar, clinic appointments and rostering (TeamUp replacement)

- **Status:** Accepted · **Date:** 2026-11-08 · Input: Lance's TeamUp set-up (booking fields, booking calendar, staff roster)

## Decision
- **Calendar** (week/day, 06:00–21:00, 15-minute rows) shared by bookings and the roster: all-day row on top, side-by-side lanes for overlaps, colour per clinician/staff member (12-colour palette, chosen per person), **striped = tentative** (as in TeamUp). Blocks are positioned with CSS classes because the Content-Security-Policy forbids inline styles.
- **Booking calendar** shows theatre lists (all-day row) with their cases at planned times, and **clinic appointments** (consultation, follow-up, phone call, pre-admission) with a NO CHARGE flag. Clicking an empty time books an appointment; clashes with the clinician's other appointments or theatre lists are warnings.
- **TeamUp booking fields** added to theatre bookings: tentative, payment type, deposit required, pre-booking check list, office-only "entered in Incisive and Xero", actual theatre time in/out, trays needed/used, loan instruments and consumables, scope ID. Name, DOB, NHI and phone come from the patient record; procedure, anaesthetist and comments already existed. Attachments arrive with document scanning.
- **Rostering module** (schema `roster`, paid module): staff list (role, colour, contracted hours, optional link to a BrightPAS account), shifts and all-day entries with area, break, note and tentative flag; overlapping shifts refused; warnings for approved leave and > contracted + 8 hours a week; regular shifts generating up to 13 weeks (skipping leave and existing shifts); leave requests (staff for themselves via the account link, managers for anyone) and approval with a count of shifts needing cover; fortnightly hours vs contracted. New default role **Roster Manager** (no patient access); roster.view added to clinical/admin staff roles.

## Consequences
- Shift swaps and a Xero payroll export are later increments. Overnight shifts are split at midnight.
