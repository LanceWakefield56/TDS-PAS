# ADR 0047 – Direct insurer claiming, linked EFTPOS terminal and fuller Xero

Status: Accepted (0.34.0)

## Context
Claims & payments (0.26.0, ADR 0039) makes claim files for ACC and insurers, records EFTPOS results keyed by hand and sends invoices to Xero.
Finance still uploads files, keys amounts twice, cannot refund to a card or reduce an invoice without voiding it, and Xero gets duplicate contacts.
Lance approved 0.34 on 28 Nov 2026 (direct claiming with ACC, Southern Cross and nib; EFTPOS; Xero).

## Decision
Extends the Claims and Billing modules; no new licence or permissions.

**Direct claiming** (`DirectClaimService`). Each funder can be set to Off (file or portal, as before), Simulator (training) or Connected (FHIR R4:
`Claim` posted to the funder's address, answers read from `ClaimResponse`; use "preauthorization" for prior approvals). Ready claims and draft prior
approvals are sent with one button; the claims worker asks for answers every 15 minutes (or Check funders now) and records accepted, rejected,
part-paid and paid claims – payments go into Billing with method ACC or Insurer, so the invoice balance stays the truth. Approved prior approvals get
the funder's reference, amount and valid-until date. The API key is stored protected. If the funder can't be reached the claim stays ready and can go
by file. Simulator: accepted on the first check, paid on the next; amounts ending .13 are rejected (claims) or declined (approvals).
Each funder's real API layout must be confirmed under their provider agreement – the FHIR client is the starting point, not a certified integration.

**Linked EFTPOS terminal (Windcave HIT).** Terminal type Windcave sends the amount to the terminal (`{url}/pxmi3/pos.aspx`, Purchase or Refund)
and the screen asks for the result every 3 seconds. Only the card type and last four digits are kept. **Refunds to the card** (Finance, Claims manage):
up to what is left on an approved payment, with a reason; recorded on the invoice as a negative payment. Settlement shows refunds separately.
Manual and Simulator terminals work as before (Simulator refunds approve at once).

**Credit notes** (Billing). Take money off an issued invoice without a payment: numbered CN000001…, kept as a payment with method CreditNote so
balances, statements and reports are right. Ordinary payments can't use that method.

**Xero.** Contacts are matched by name and email (or created) once and remembered (`claims.XeroContacts`), so Xero doesn't fill with duplicates.
Credit notes go as ACCRECCREDIT credit notes allocated to the invoice (API mode) or as negative lines in the import file (Xero imports those as credit
notes). Refunds can't be sent through Xero's API as payments, so they are listed once to record by hand.

## Consequences
- Migrations: claims `DirectClaiming`, billing `CreditNotes` (07-billing.sql and 26-claims.sql). New settings default off; demo puts Southern Cross on the simulator.
- Live use needs: provider agreements and API details from ACC, Southern Cross and nib; a Windcave merchant agreement with HIT user and key; the
  Xero app as before. All are tested only against fakes and the simulators so far.
