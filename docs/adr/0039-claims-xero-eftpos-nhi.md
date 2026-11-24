# ADR 0039 – Claims, Xero, EFTPOS and NHI/HPI lookups

Status: Accepted (0.26.0)

## Context
Spec 7.12 (claims and payments) and 7.2 (NHI/HPI): most TDS revenue comes from ACC and health insurers through prior approvals and claims; Finance
keys invoices into Xero; reception takes EFTPOS payments on a stand-alone terminal; identities are checked on the Health NZ portal. Direct funder,
terminal and Health NZ connections all need agreements TDS does not yet hold, so every connection must have a file or manual fallback that works today.

## Decision
Module `BrightPAS.Modules.Claims` (schema `claims`, licence `claims`), depending on Patients, Billing and Reminders (secret protection).

**Funders and prior approvals.** Funder register (ACC, Southern Cross, nib, AIA, Partners Life, Accuro, UniMed, Health NZ outsourced) with how claims
are sent (file, portal, direct), whether prior approval is needed and TDS's provider number. Prior approvals: draft → sent → approved / part approved /
declined (reason required) with reference, amount and valid-until; expired automatically each night (`ClaimsWorker`).

**Claims.** Made from an issued ACC/insurer/Health NZ invoice; lines are a snapshot (incl. GST) so later invoice edits don't change a sent claim.
`ChecksAsync` gives plain-English readiness checks, each with how to fix it: NHI recorded, lines, ACC claim number format, membership number, prior
approval attached / granted / valid / covers the amount. Ready claims go in a **claim file** (CSV, one row per line, formula-injection safe, encrypted
via secure export) per funder, or are marked as entered on the funder's portal. Responses (accepted / rejected with reason); write-off leaves the
balance on the invoice. One open claim per invoice (filtered unique index).

**Remittances.** Funder CSV (claim reference, amount, outcome, reason – header names matched loosely). Each paid line records a payment on the invoice
through `BillingService.RecordPaymentAsync`, so Billing remains the single source of the balance; declined lines reject the claim; unmatched lines are
kept with what BrightPAS did. SHA-256 of the file prevents double import.

**Xero.** File mode: Xero "Import sales invoices" CSV (tax exclusive, NZ dates, OUTPUT2/NONE). API mode: OAuth 2.0 refresh token (rotated and stored
protected on every use), invoices as ACCREC AUTHORISED, then payments against the Xero invoice ID. `XeroLinks` records each invoice/payment sent so
nothing goes twice; earlier files can be downloaded again. `IXeroApi` is faked in tests.

**EFTPOS.** Manual terminal (default): staff key the amount and record the receipt result; Simulator for training and the robot (.51 declines, .52
times out). Only card type and last four digits are stored – a longer number is refused. Approved → EFTPOS payment on the invoice with the auth code.
Daily settlement page (NZ day, daylight-saving aware). Refunds and integrated terminals are in the backlog (merchant agreement needed).

**NHI and HPI.** Off / Simulator / Live (Health NZ FHIR R4, client credentials, `userid` header, https only). NHI check on the patient page compares
name, date of birth, address and contacts; contact details can be accepted (through `PatientService.UpdateContactAsync`); name, date of birth or a
dormant NHI must go through an identity correction (second-person check, ADR 0010). HPI practitioner and facility search; a facility can be added as
a GP practice. Fallback: GP practice list imported from CSV (matched by facility ID). Lookups are audited and rate-limited to 30/minute per user.

**Journey.** The "Paid" stage for funder invoices becomes the claim: make it → fix it → send it → with the funder. My list shows claims to fix,
rejections and prior approvals not yet sent.

## Consequences
- BrightPAS suggests (checks, matching); Finance decides (send, accept, write off).
- Every external connection has a working fallback: claim files/portal, remittance CSV, Xero file, manual terminal, practice CSV.
- 3NF: claim lines, remittance lines and Xero links are rows; settings are single-row tables.
