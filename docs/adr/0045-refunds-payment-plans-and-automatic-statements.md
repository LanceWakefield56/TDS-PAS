# ADR 0045 – Refunds, payment plans, automatic statements and overdue reminders

Status: Accepted (0.32.0)

## Context
0.30.0 (ADR 0043) lets patients pay invoices online and sends statements, but refunds had to be done in the Windcave portal and then recorded by hand,
there was no way to agree instalments, statements had to be started by Finance each month, nobody was reminded when an invoice went overdue, and
insurer shortfalls (what is left on an insurer invoice after the insurer pays) were not on statements. Lance approved 0.32 on 28 Nov 2026.

## Decision
Extends the Online payments module (`payments` schema and licence); no new module.

**Refunds.** Finance (new permission `onlinepay.refund`, Finance role) refunds part or all of an approved online payment from the payment row.
Windcave: a refund transaction (`POST /api/v1/transactions`, type refund) against the original transaction id, now kept from the session result;
Simulator: always succeeds. Money never applied to an invoice is refunded first (and clears Needs attention when all of it is refunded); the rest
comes off the invoices the payment paid, newest first, as a negative payment in Billing (`BillingService.RecordRefundAsync`; never more than has been
paid; status goes back to part paid or issued). A refund the provider declines is kept as "did not go through" and changes nothing.
Numbered RF000001…; listed under Refunds; audited.

**Payment plans.** Finance agrees instalments for one or more of a patient's invoices (self-funded, plus shortfalls when switched on; not on another
active plan; one active plan per patient): amount, weekly / fortnightly / monthly, first date (within two months), at most 24 instalments. Monthly
dates are counted from the first date each time (31 Jan → 28/29 Feb → 31 Mar). Progress = everything paid on the plan's invoices since the plan began,
by any method, so nothing is matched by hand. Behind = less paid than the instalments already due. The plan completes itself when paid off; cancelling
needs a reason. Numbered PP000001….

**Daily accounts round.** The payments worker runs `AutomationService` once a day after 9 am NZ time (Finance can press "Run the daily round now"):
1. completes paid-off plans and sends a reminder (text/email, new message kind "Payment plan reminder", with a plan payment link) a set number of days
   before each instalment, once per instalment;
2. on the chosen day of the month (1–28; 0 = off) prepares the statement run, and sends it if "send straight away" is on;
3. sends a first and a second overdue reminder (new kind "Overdue reminder", with an invoice payment link) a set number of days after the due date,
   once each per invoice (`payments.Overdue`), skipping invoices on an active plan and balances under the statement minimum.
Patients keeping up with a plan are left off statements. All of it is off until Finance sets it up (new gates default off).

**Insurer shortfalls.** When switched on, an insurer invoice the insurer has paid (an Insurer payment recorded) with money still owing is treated as the
patient's to pay: it appears on statements (marked as a shortfall), overdue reminders and plans.

## Consequences
- Windcave refunds need the refund permission on the Windcave REST user; payments taken before 0.32 have no transaction id and must be refunded in the portal.
- Message templates gain {Amount} and {InvoiceNumber}.
- Database: `payments.Refunds`, `payments.Plans`, `payments.PlanInvoices`, `payments.Overdue`; new columns on settings, links (PlanId), payments
  (TransactionId, Refunded) and allocations (Refunded).
