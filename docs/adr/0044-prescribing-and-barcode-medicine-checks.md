# ADR 0044 – Prescribing, barcode medicine checks (BCMA) and NZ ePrescriptions

Status: Accepted (0.31.0)

## Context
0.27.0 (ADR 0040) checks a medicine against allergies, dose limits and interactions when it is charted, and stores pack barcodes (GTIN) in the
medicines list – but nothing checks that the medicine in the nurse's hand is the right one for the patient in front of them, there is no medicine
chart (orders), and take-home prescriptions are handwritten. Lance approved 0.31 (barcode checking and NZ e-prescriptions) on 28 Nov 2026.
Barcode medicine administration (BCMA) is the main defence against wrong-patient and wrong-drug errors. The NZ ePrescription Service (NZePS) lets any
pharmacy dispense from a barcode on the printed prescription; connecting needs a Health NZ agreement, conformance testing and keys TDS does not have yet.

## Decision
Module `BrightPAS.Modules.Prescribing` (schema `rx`, licence `prescribing`, depends on `emr` and `medicines`). Permissions View / Prescribe / Manage
(Anaesthetist all three, Surgeon View and Prescribe, Nurse View, System administrator Manage).

**Medicine chart (orders).** A prescriber writes an order on the episode: medicine (from the list or typed), dose, route, once / regular / as needed,
how often, what for. Nurses give against an order ("Give" fills the charting form); a once-only order becomes Given; orders are stopped with a reason.
Orders after discharge are refused (write a prescription instead).

**Barcode check.** `BcmaService` runs as one more `IMedicineSafetyCheck` (`BcmaGate`, only when licensed), so it sits inside `EmrService.GiveCheckedAsync`
with the 0.27 checks and shares the override reason. `MedicineGiveCheck` gains the wristband scanned and the order id. Checks: wristband = the patient's
NHI or MRN (the 0.6 wristband barcode); pack GS1 barcode (`Gs1` reads DataMatrix with ]d2 and group separators, the bracketed text, or plain GTIN-8/12/13/14;
expiry day 00 = month end) → medicine via the GTIN in the medicines list; same medicine = same id, else same ingredients (different product or strength
is a warning), else same name; expiry before the day given; the order chosen must be active and the same medicine, otherwise an active order is matched
automatically, and optionally a warning when none. Modes: Off (default for installs and upgrades), Advise (shown only), Enforce (a Stop needs a reason;
someone else's wristband can never be overridden; an unscanned wristband is a Stop, an unscanned pack a warning – drawn-up syringes have no barcode).
Every check is kept (`rx.Scans`: what was scanned, batch, expiry, serial, result, reason) and linked to the charted medicine; stops and overrides are audited.
A report shows how often wristbands and packs were scanned and lists every stop and override. A lookup endpoint shows the nurse what a scan means before Record.

**Prescriptions.** Prescribers are set up with their BrightPAS user, HPI person number (CPN), registration and scope. Only a registered prescriber
writes; only the same prescriber signs. Each prescription gets a number (RX000001) and an 18-character NZePS id (17 random characters + ISO 7064
Mod 37,2 check character), printed as a Code 128 barcode (own PNG writer – no new package). Items: medicine (NZMT id when from the NZULM list), directions,
quantity and unit, repeats (0–11; none for controlled drugs). Sending: Print only (default) / Simulator / NZePS (`INzepsApi`: FHIR R4 bundle – Patient
with NHI, Practitioner with CPN, a MedicationRequest per item sharing the NZePS id, facility HPI; cancel by id; bearer key stored protected). A failed send
leaves the prescription Failed with the reason, to print or send again. An optional folder gets a FHIR copy of every signed prescription (the file
fallback). The printed copy needs the prescriber's own signature when it was not sent electronically or holds a controlled drug; reprints say COPY.

## Consequences
- The NZePS message layout and authentication must be confirmed with Health NZ (and conformance tested) before switching from Print only; the id format
  is checked the same way pharmacy scanners do but may need the official generator.
- Real benefit needs every medicine's pack barcode in the list (NZULM file or scanned in) and wristbands printed with barcodes.
- Clinical governance decides when to move from Advise to Enforce, using the report.
- Electronic signature of controlled-drug prescriptions is not attempted: they are always printed for a wet signature.
