# ADR 0046 – Estimate follow-up, expiry and invoice from an estimate

Status: Accepted (0.33.0)

## Context
Procedure estimates (0.21.0, ADR 0034) are built, issued and emailed, but nothing records what happened next: whether the patient accepted, whether
the insurer approved, whether a deposit was paid or the operation booked. Finance kept this in their heads or a spreadsheet, estimates quietly
expired, and an invoice had to be typed again in Billing. Lance approved 0.33 on 28 Nov 2026.

## Decision
Extends the Estimates module (`est` schema); no new licence or permissions.

**Follow-up status** on every issued estimate: Waiting (set when issued) → Accepted / Declined (reason needed); for insured patients Approval
requested → Approved (the insurer's number is saved as the prior approval) / Approval declined; Deposit paid (amount and date); Booked; Invoiced
(only by making the invoice); Expired. Each step is kept in `est.EstimateEvents` (who, when, note) and audited. Staff may set a date to follow up again.

**Lists** (Before surgery › Estimate follow-up): To chase (nothing heard for the follow-up days – 7 by default – the insurer not answered, accepted
but not booked, or the follow-up date reached); Running out (valid-until within the warning days – 14 by default); Accepted – to book or invoice;
Expired; All open. The estimates worker marks issued estimates past their valid-until date Expired once a day (not declined, booked or invoiced ones).

**Invoice from an estimate** (`EstimateInvoicer` in the web project, so Estimates does not depend on the Billing licence): needs the estimate linked to
the patient's record, Billing licensed and the Billing manage permission. Makes a draft Billing invoice: one line per priced estimate line
(estimate prices include GST, so each is entered without GST and Billing adds it back – a cent may differ through rounding), payer from the funding
(insurer name; policy and approval number as the reference), surgeon, the booking if known, and a note of any deposit paid (to record as a payment
once issued). The estimate becomes Invoiced and links to the invoice.

**Report**: estimates issued in a period by surgeon, procedure, funding and month: issued, accepted, declined, booked, expired, still open, value
issued and accepted, accept rate (accepted ÷ (accepted + declined + expired)) and average days from issue to acceptance.

## Consequences
- Settings gain FollowUpDays and ExpiryWarningDays (Estimates › Admin › Settings).
- Estimates issued before 0.33 have no follow-up status until someone records one; they still appear in the lists and expire.
