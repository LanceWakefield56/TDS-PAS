# ADR 0034 – Procedure estimates (TDS Estimates moved into BrightPAS)

Status: Accepted (0.21.0)

## Context
TDS built a standalone estimate app, **TDS Estimates v1.5** (ASP.NET Core 8, Blazor Server, SQLite/SQL Server), that prices every specialty from the
"Estimates - Facility & Anaesthetist.xlsx" price list and prints the estimate layout of the specialty workbooks. Its handover brief (23 Nov 2026) asked for it to
become a BrightPAS module. BrightPAS already had a generic Billing module ("Estimates & invoices": fee items, GST, fee splits, invoices, payments), but not TDS's
pricing rules: per-procedure facility/anaesthetist/consumables, surgeon fees per procedure and surgeon, add-ons with "included" codes, special packages,
extended theatre time, "if required" lines and the printed layout. The brief proposed a Blazor component library dropped into BrightPAS; BrightPAS's screens
are React with a strict content security policy, its own sign-in with MFA, permissions, audit, secure exports and test robot.

## Decision
- **New licensed module `estimates` ("Procedure estimates", schema `est`)**, separate from Billing. Permissions **estimates.build** (build, issue, PDF,
  email, Incisive, search, outbox) and **estimates.manage** (price list, templates, stock and costing, imports, settings). Reception gets build; Finance gets both.
- **Business rules kept exactly** (`EstimateCalculator`, a pure class – no database or clock): facility (manual › special › standard), anaesthetist only for
  a general anaesthetic or sedation by an anaesthetist, surgeon base fee from the surgeon's row or "All surgeons", surgeon add-ons add to the surgeon fee and
  print as "Add-on – name" under it, all-in add-ons print as their own line, a manual surgeon fee replaces schedule fee and surgeon add-ons, add-ons listed in the
  procedure's IncludedAddOnCodes can't be charged (hidden, and a warning if chosen), "if required" add-ons and lines are listed but not totalled, $90 per started
  15 minutes over the scheduled time, 100%/200% multiplier, 50% self-funding deposit due 14 days before surgery, valid 3 months, the warnings list and the
  supplied-price charge-out (× 1.15 × 1.25 × 1.15). Reference EST-yymmdd-NHI or the last name in capitals, with -2, -3 ….
- **The printed estimate** (`Web/Pdf/EstimatePdf`) reproduces the v1.5 layout with TDS's logo and footer (embedded) in Liberation Sans, which is metric-compatible
  with Arial. Each estimate keeps a snapshot of its printed lines so it always reprints as issued. Draft and cancelled estimates are marked on the PDF.
- **Archive in the database, not a folder:** a PDF is kept when an estimate is issued and again ("_updated") whenever an issued estimate changes, encrypted at
  rest with the document cipher (Data Protection). Database backups cover them; there is no separate archive folder or storage password to manage.
- **Email:** recipient Patient / Insurer / Other; right-recipient checks (an email used for a different patient, an NHI seen with another email or name, an
  address without the patient's name, insurer address list, patient's own address sent as "insurer"); staff must tick to confirm; only issued estimates; the PDF
  password defaults to the DOB as DDMMYY for patients and none for insurers, chosen per send; the email states the rule, never the password. Two modes:
  **Outlook draft** (.eml marked X-Unsent) or **send from BrightPAS** through the Reminders mail server with a **hold period** (default 10 minutes) in an
  Outbox – stop or send now; editing or cancelling an estimate stops its held email; failures retry up to 5 times. The message and PDF are encrypted at rest
  while held and the PDF is removed once sent. This is the estimate-specific part of spec 7.19–7.20 (delayed send, right recipient).
- **Imports keep the spreadsheet layouts** (ClosedXML 0.105, MIT – with DocumentFormat.OpenXml, MIT, and SixLabors.Fonts 1.0, Apache-2.0; no known vulnerabilities): pricing "Export" tab (headings row 3, data row 4; optional "Surgeon Fees" and "Add-ons"), with a
  difference list, approval, warnings for blank prices and moves over 25% (facility or anaesthetist) that must be confirmed, a stop when the layout changes, and
  future-dated prices scheduled (applied on the day – before each save and hourly). Stock "Export" tab or supplier blocks. Downloads in the same layouts go
  through the secure export. A **price-change impact** list shows scheduled changes and how many valid issued estimates are on the old price.
- **Patient link:** "New estimate" from a patient record prefills name, DOB, NHI, contact and address, and a returning patient's funding and insurer from their
  last estimate; the patient page lists their estimates (open / duplicate). Patient details on the estimate are a snapshot, so estimates can be made before a
  patient is registered and linked later (the NHI must match). Patient merge moves estimates.
- **Templates** (starter set of seven seeded once), favourites, order, surgeon visibility; safe delete everywhere – anything used by an estimate or template
  can only be hidden. **Incisive export** writes the PDF to the scans folder (share) with the file-name pattern; only issued estimates.
- **Dropped from the standalone app** because BrightPAS provides them: app user accounts, Windows sign-in, the audit table, backups, storage/database moves,
  SQLite. Times are stored as instants; estimate and procedure dates are NZ calendar dates (`NzTime.Today`), so month ends and leap years follow DateOnly rules.

## Consequences
- The standalone app is **not yet** running on this code: the module's data model and services depend on BrightPAS.Core (audit, NZ time). Sharing one library
  with the standalone app would need those behind small interfaces – a later decision; until then a pricing rule change is made in both, and the unit tests
  (the v1.5 self-test scenarios, run against TDS's real price list) are the check that both agree.
- Billing remains for invoices and payments; turning an issued procedure estimate into a Billing invoice is planned with the claims work.
- Open business items from the brief are unchanged: Incisive file naming, SMTP details, Veins and skin graft fees, Dr Butcher's fees, the MyoSure surgeon fee,
  and the internal costing defaults (placeholders). Procedures without a surgeon fee show "To be advised"; the set-up wizard lists them.
