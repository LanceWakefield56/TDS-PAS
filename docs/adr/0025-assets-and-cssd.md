# ADR 0025 – Assets & CSSD module (0.13.0)

Status: accepted (2026-11-14)

## Context
Spec 7.9: equipment register with PPM, calibration, test-and-tag (AS/NZS 3760) and lockout; CSSD traceability to AS/NZS 5369:2023 (tray catalogue, barcode labels, washer/steriliser cycles with Belimed log import, parametric release, indicators, storage and expiry, issue to case, set-to-patient traceability, recall by load, nonconformance/CAPA); endoscope traceability; loan sets; purchasing with Xero bills; consumables and implants with a patient implant record. TDS's CSSD has one technician, a Belimed WD 290 IQ washer (printout + log file per cycle), three steriliser loads a day, no tracking labels yet (packet names and indicator labels go on a tray sheet), Zebra thermal printers with autoclave-resistant stock chosen.

## Decisions
- One licensed module `assets` (schema `assets`) with three services: EquipmentService, CssdService, StockService. Pages: Sterile services (CSSD), Assets & equipment, Stock & purchasing; a "Sterile items, scopes & implants" panel on the patient record.
- **Machines are equipment.** Washers, sterilisers, AERs, drying cabinets, endoscopes, monitors and endoscopy towers are all `Equipment` rows (category), so maintenance, lockout and device connection (NetworkAddress, foundation for 0.17.0) work the same for all. An out-of-service machine cannot run loads.
- **Tray lifecycle is enforced:** Dirty → (released wash load) Washed → (contents checked) Packed → (released steriliser load) Sterile until the storage date → Issued to a theatre case → back Dirty. Only sterile, in-date trays from a released load can be issued.
- **Parametric release:** a load is released only if the machine reported a good cycle, no indicator failed or is outstanding (a BI that is used must have passed) and a sterilising temperature is recorded; optional second-person release. Failing a load quarantines its trays and raises a nonconformance automatically. A late failed indicator on a released load warns to recall.
- **Recall by load** quarantines trays still in store and lists every case (patient, date, procedure) the load was used on. Implant recall lists patients by product and lot.
- **Machine logs:** a tolerant parser reads labelled values (batch, program, start/end, temperature, hold, pressure, A0, result; any fault line fails the cycle) and keeps the raw log on the load. The exact Belimed WD 290 IQ export must be confirmed with a real log file; manual entry is always available.
- **Endoscope state** is derived from its event history: Ready only after a passed leak test, manual clean and passed AER cycle since its last use and within the storage time (default 72 h, configurable); failed leak test or microbiology quarantines it and raises a nonconformance; use on a patient requires Ready.
- **Purchasing:** reorder alerts → draft orders per supplier; approval needs `purchasing.approve`; goods receipt records lot and expiry; supplier invoices are matched within $1 or 1 % of goods received (or a difference accepted with a reason); matched invoices export once as a Xero "Import bills" CSV (15 % GST on Expenses). A direct Xero API push is backlog (needs TDS's Xero app connection).
- Permissions: assets.view, assets.manage, cssd.process, cssd.release, stock.manage, purchasing.approve. CSSD Technician gets all CSSD ones; nurses view/process/stock; finance approves purchasing; System Administrator manages the register. Anyone with assets.view may lock out faulty equipment.
- Tray labels: ZPL to the default Zebra label printer (tray, barcode, load, sterilised and use-by dates, packer).

## Consequences
- AS/NZS 5369 traceability and recall can be shown to auditors from BrightPAS (reporting.vTrayTrace) instead of paper tray sheets.
- The theatre record's free-text "trays used" and "scope" fields remain; the CSSD issue and scope-use records are the authoritative trace. Linking the two screens (scan at the case) is a follow-up.
- Found while building the demo data: the demonstration loader failed on some days of the week (a short last window created no lists) – fixed.
