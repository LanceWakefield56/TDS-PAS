# ADR 0040 – Medicines and medicine safety checks

Status: Accepted (0.27.0)

## Context
TDS has no MIMS or other licensed drug database. Lance asked for dose checks, allergy and interaction alerts built from reliable free NZ
sources (decision 16 Nov 2026). The free NZ Universal List of Medicines (NZULM, built on the NZ Medicines Terminology) gives names, NZMT codes,
ingredients, strengths and pack data, but states that it carries **no clinical decision support** – dosing and interactions are in the
NZ Formulary, which needs a licence. A day-surgery unit gives a small set of medicines (about 50), so a short, clinician-owned rule set is practical.

## Decision
Module `BrightPAS.Modules.Medicines` (schema `meds`, licence `medicines`).

**Medicines list.** Ingredients (NZMT substance names, lower case) with other names (brand, older, overseas – Panadol, lignocaine), medicines
(product, form, usual route, source TDS / NZULM / Example, on the TDS list) with per-ingredient strength (amount + unit, and "per mL" for liquids)
and pack GTINs. A starter list of common day-surgery medicines can be loaded. Files are imported with a **saved column layout** (CSV, tab or pipe;
RFC 4180), because the NZULM file layout is only given with the download – the layout is chosen on screen once and reused monthly. Medicines from
the same source that are missing from a full file are made inactive, never deleted. Rules refer to ingredients by name, so re-imports never break rules.

**Rules (versioned).** Same governance as triage rules (ADR 0035): draft → clinical approval with a note (`medicines.approve`) → one approved set;
approved sets never change. Contents: dose limits per ingredient, optional route and age band (max per dose, per 24 h across every product, per kg,
per kg per 24 h, minimum hours apart; Stop or Warn); groups (allergy classes with how serious another member is – penicillins Stop, opioids Warn –
and interaction classes such as QT-prolonging); cross-reactions between allergy groups; interactions between ingredients or `group:CODE`.
An example set ships as a draft labelled "not yet clinically approved".

**Checks (pure, `MedicineChecks`).** Allergies are always checked (by link, ingredient name or other name); without approved rules only same-medicine
allergies are checked and the screen says so. Doses are read as written (`DoseParser`: mg/g/microgram/unit/mmol, mL via strength per mL, tablets,
capsules, vials) – a bare number is never guessed. Totals use the last 24 h of every episode, including medicines filed from SaferSleep.
Interactions are checked against medicines given in the last 24 h and the medicines the patient listed in the questionnaire. A possible double dose
(same ingredient within 60 min with no interval rule) is flagged. Anything that could not be checked is said in plain English (not in the list,
dose not understood, weight missing, allergy not linked). Weight comes from the entry, the latest triage, or the questionnaire.

**Where the checks run.** `IMedicineSafetyCheck` (Core) is called by `EmrService.GiveCheckedAsync` before a medicine is charted; the web app
implements it only when the module is licensed (`MedicineSafetyGate`, facts from `MedicineFactsProvider`). Modes: Off, **Advise** (default for new
installs and upgrades – shown, never blocked) and Enforce (a Stop needs an override reason; enforcing needs an approved rule set). Every check is
kept with its findings, result (Clear, Warnings, Overridden, Stopped) and reason, linked to the charted medicine; overrides and stops are audited.
Allergy alerts can be linked to an ingredient or allergy group (`meds.AllergyLinks`) so checks do not depend on the alert's wording.

## Consequences
- Dose and interaction safety is only as good as TDS's approved rules: clinical governance (anaesthetists, pharmacist) owns them and must review
  them against current references. A licensed source (NZ Formulary or a commercial drug database) can later replace the rule tables behind the
  same `MedicineChecks` contract.
- NZULM must be requested monthly by email; the import layout is confirmed once the first file arrives.
- Barcode lookup (GTIN) is in place for barcode medication administration (next release).

## Addendum – NZ Formulary PDFs (same release)
Lance confirmed the NZF and NZFC PDFs (nzf.org.nz, released the first working day of each month) can be used. `NzfPdf` (PdfPig, Apache-2.0) reads
lines with position, size and bold; `NzfParser` (pure) uses the NZF's own layout – drug names in large bold lower case, bold section headings,
dosing as indication → route → population lines – after scaling sizes so the NZFC (set larger) reads the same. Each dosing line is kept as published
and BrightPAS reads limits from it (dose, per kg, maximum daily, "without exceeding", hours apart, ages and body-weight bands; titration and
infusions are not turned into limits). Only the current release per edition keeps content; release rows are history. Imports: upload, watched
folder, or monthly download (retries every 6 h). The NZF holds no interaction data. NZF text is TDS's copy for internal use – check the NZF terms
before distributing BrightPAS with or for other organisations; no NZF content ships with BrightPAS.
