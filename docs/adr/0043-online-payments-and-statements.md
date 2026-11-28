# ADR 0043 – Online payments and statements

Status: Accepted (0.30.0)

## Context
Self-funded patients pay at reception (EFTPOS, ADR 0039), by bank transfer or by phone. Unpaid invoices are chased by hand and there are no
statements. Competitor systems let patients pay online from a link. It was suggested as 0.30 and Lance chose it on 28 Nov 2026. TDS has no online
merchant agreement yet; Windcave is the common New Zealand provider and offers a hosted payment page, so card numbers never reach BrightPAS
(keeping BrightPAS out of PCI DSS card-data scope beyond SAQ A).

## Decision
Module `BrightPAS.Modules.Payments` (schema `payments`, licence `payments`, depends on `billing` and `reminders`).

**Provider.** Off (default) / Simulator (training and the robot; approve, decline or cancel on a BrightPAS page, no money moves) / Windcave
(REST API: create a purchase session, send the patient to Windcave's hosted page, read the session back). The Windcave API key is stored protected
(Data Protection), like the Xero secret. `IWindcaveApi` is faked in tests.

**Payment links.** Same pattern as ADR 0011/0042: random token, only its SHA-256 hash stored, date of birth as a second factor, five wrong dates
lock for 30 minutes, expiry (30 days by default), a new link for the same invoice stops the old one. A link covers one issued invoice, or all the
invoices on a statement. Sent by text/email (Reminders kinds PaymentLink and Statement) or shown to staff to copy. Public endpoints `/api/pay/{token}`
are rate-limited per IP; the patient sees what is owing (current balances, oldest first) and pays the whole balance or part of it (at least the
minimum, $10 by default).

**Results are only believed from the provider.** The return address and Windcave's notification (`/api/pay-notify`) only make BrightPAS ask
Windcave for the session itself. A background worker asks again every 10 minutes for payments the patient started but never came back from;
unfinished simulator payments close after an hour, Windcave ones after 2 days. Completion is decided by one atomic status change
(Started → Approved/Declined/Cancelled), so the return page and the notification arriving together apply the money once.

**Applying money.** Approved money is recorded through `BillingService.RecordPaymentAsync` (method Credit card, reference = merchant reference +
auth code), oldest invoice first; each part is an allocation row. Money that cannot be applied (paid another way meanwhile) or an amount different
from what was asked is flagged **Needs attention**; Finance refunds it in Windcave or applies it by hand and records what they did. Billing stays
the single source of the balance. Only card type and last four digits are kept.

**Statements.** Finance prepares a run for a statement date: every patient with self-funded invoices issued or part paid, total at least the
statement minimum ($5), snapshotted as JSON (numbered ST000001…). One unsent run at a time; an unsent run can be deleted. Send: email or text with a
payment link where the patient has an address and has not opted out; everyone else goes in the posting batch – one PDF (MigraDoc, each statement its
own section and page numbers, address placed for a DL window envelope) with a fresh payment link printed on each. Insurer, ACC and Health NZ invoices
go through Claims, not statements. A statement PDF can be downloaded by staff, and by the patient through their link (no link printed on that copy).

## Consequences
- Patients can pay from home at any time; payments reach the invoice without anyone keying them in.
- Needs, before going live: a Windcave merchant agreement and REST API credentials, a published https address, and a test of the hosted page in
  Windcave's UAT environment.
- Not yet: refunds through Windcave from BrightPAS, payment plans / scheduled payments, card surcharges, automatic monthly runs, overdue reminder
  texts, statements for insurer shortfalls.
