# ADR 0032 – New Zealand time, daylight saving and leap years

Status: Accepted (0.19.0)

## Context
TDS asked that everything accounts for daylight saving and leap years. Wall-clock times come from people, machines (steriliser records, HL7 without an offset) and schedules (shifts, theatre lists); a server set to the wrong time zone, or a sum done on wall-clock times, gives wrong dates, durations and pay.

## Decision
- **Instants are stored in UTC** (`DateTimeOffset`); dates are `DateOnly`, times of day `TimeOnly`.
- **One helper, `BrightPAS.Core.NzTime`**, turns NZ wall-clock times into instants and back, using the operating system's time-zone data (Pacific/Auckland / "New Zealand Standard Time", updated by Windows Update):
  - missing hour (last Sunday of September, 02:00–03:00) → moved forward by the gap;
  - repeated hour (first Sunday of April, 02:00–03:00) → first (NZDT) occurrence, or the later one when an end must follow a start (`EndAfter`);
  - day boundaries (`StartOfDay`) for every date filter, so a "day" is 23 or 25 hours on change days.
- **The application clock's local zone is NZ on any server** (`TimeProvider.LocalTimeZone`), so every `GetLocalNow()` is NZ time; no code uses the server's own zone (`DateTime.Now`, `ToLocalTime`) for data.
- **Durations always come from instants:** steriliser plateau and cycle times, overnight shifts (pay for 8 h or 10 h on change nights, Holidays Act), monitor intervals.
- **Leap years:** calendar maths uses `DateOnly` (29 February only in leap years; a record dated 29/02 in a common year is refused, not guessed). **Age** = whole years, with a 29 February birthday counted from **1 March** in a common year – the same rule in C# (`NzTime.Age`), the reporting views and the browser.
- **Reporting views** use `AT TIME ZONE 'New Zealand Standard Time'` for today's date and for the day an event happened.
- **Browser:** dates and times are shown in Pacific/Auckland even if a PC is set to another zone.
- Tests cover both changes, the missing/repeated hour, cycles and shifts across them, 29 February ages, and working days across 29 February.

## Consequences
- Statutory extracts (NBRS, NMDS, NBSP) and HL7 out carry NZ dates and offsets that are right on change days.
- A future change to NZ daylight-saving law needs only the OS time-zone update; the Matariki table in `WorkingDays` still needs its yearly check.
