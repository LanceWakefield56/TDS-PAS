# ADR 0012 – SMS & email reminders (Modica default, vendor-neutral)

- **Status:** Accepted · **Date:** 2026-11-07

## Decision
- New paid module **reminders** (schema `reminders`): booking confirmations, day-before reminders, questionnaire links, two-way SMS replies, opt-outs and per-patient preferences (allow SMS/email, English or te reo Māori).
- **Vendor-neutral.** SMS goes through `ISmsGateway`; **Modica (REST v1, Basic auth, `POST /messages`) is the default** (spec 2/6). Alternatives without code changes: *Another SMS vendor (HTTP API)* with a JSON template, *Test mode* (record only) and *Off*. Email: SMTP (e.g. Microsoft 365 relay), Test mode, Off. Setup asks for the vendor (`--sms-provider`, `--email-provider`); it can be changed later on the settings screen. Setup never handles vendor passwords.
- Vendor passwords and the callback key are encrypted with ASP.NET Core Data Protection (keys outside the database, DPAPI on Windows) and are write-only in the UI.
- **No clinical detail** in messages: templates accept only `{FirstName} {ClinicName} {ClinicPhone} {Date} {ArrivalTime} {Location} {Surgeon} {Link}` – no procedure names. Email bodies contain no clinical content (spec 6).
- **Safety rules:** automatic messages wait for the end of quiet hours (default 20:00–08:00); booking messages are re-checked just before sending (cancelled or moved bookings are not messaged); each automatic message has a de-duplication key; a message interrupted mid-send is marked Failed for staff rather than retried (no duplicates); retries back off 1/5/15/60/240 min; questionnaire links are stored only until sent.
- **Replies:** YES/Y/Āe confirms the booking automatically; NO/CANCEL is **never** an automatic cancellation – it goes to staff to phone the patient; STOP/START manage the opt-out (UEMA 2007); anything else goes to staff. Replies are matched by the vendor's reply-to id, else the latest message to that number (14 days).
- Vendor callbacks (`/api/hooks/sms/{key}/inbound|status`) are public by necessity: secret 192-bit key in the URL, constant-time comparison, 404 for wrong keys, rate-limited, 16 KB body limit, exempt from the browser CSRF header, publish only this path through the DMZ.
- Confirmations apply only to bookings made after the feature is switched on (no flood when first enabled).

## Consequences
- Modica REST v2 can be added as another gateway class later. Te reo Māori templates are added by TDS (fluent speaker review) and are off until switched on.
- Replies need the server reachable from the vendor over HTTPS with a public certificate (DMZ reverse proxy).
