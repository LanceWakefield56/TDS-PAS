# ADR 0031 – Quality management, incidents and complaints

Status: Accepted (0.19.0)

## Context
Spec 7.12 (audit, certification and QMS) and 7.14 (incidents, complaints, risk) ask for one place where TDS runs its quality system for Ngā Paerewa (NZS 8134:2021) certification: incident reporting and review, complaints under the Code of Health and Disability Services Consumers' Rights, the risk register, corrective actions, controlled policies and clinical indicators. Paper forms and spreadsheets lose the link between an event, its actions and the evidence.

## Decision
- **New module `quality` (schema `quality`)**, licensed and set up like the others; permissions quality.report (every staff role – anyone can report), quality.view and quality.manage (new Quality Manager role).
- **Incidents** use the HQSC National Adverse Events Policy: harm level, **SAC 1–4**, the Always Report and Review list. SAC 1–2 or Always Report escalates (escalation time, settings recipient) and asks for **open disclosure** (who was told, when, how) and the external notifications that apply (enum Agency: HQSC, ACC treatment injury, Medsafe, HDC, Health NZ, WorkSafe, Privacy Commissioner, Coroner; one per agency per incident). Status Reported → Triaged → Investigating → Actions → Closed; closing needs findings and learning.
- **Complaints** carry the **Right 10 clock in working days**: acknowledge within 5, decide within 10 of acknowledgement, a notified extension adds 20. `WorkingDays` excludes weekends, NZ public holidays with Mondayisation (Waitangi and ANZAC Mondayised only when on a weekend), Easter, King's Birthday, Matariki (published table), Labour Day, Christmas/Boxing Day, and 25 December–15 January (the HDC Code's "working day" definition), plus extra closed days from settings. Regional anniversary days are added in settings.
- **Risks:** 5×5 likelihood × consequence, inherent and residual, bands Low (1–4) / Moderate (5–9) / High (10–16) / Extreme (20–25); hazards (Health and Safety at Work Act) in the same register flagged IsHazard.
- **Corrective actions** link to any source (incident, complaint, audit, risk…) and need **verification by a different person** from the one who marked it done.
- **Policies** are references to the controlled document (link), with version and review date; staff acknowledge **per version** – a new version asks again.
- **Indicators:** a library seeded with 12 starters (theatre, clinical record, incidents, complaints, endoscopy CIR/ADR, patient satisfaction). Each has a calculator code supplied by the web layer (`IIndicatorCalculators`, reading other modules through their services) or monthly manual entry. Charts use **SPC** (p chart for proportions, u chart for rates, c chart for counts; 3σ limits; a shift of 8 or a trend of 6 flagged), red/amber/green against target/alert, and **equity grouping** by prioritised ethnicity (Māori > Pacific > Other > Not stated) as Ngā Paerewa expects.
- The **quality pack** (incidents, complaints, risks, actions, indicators for a period) is a PDF through the secure export service (watermark, audit).
- Reporting views vIncident and vComplaint; patient merge moves incidents and complaints.

## Consequences
- One audit trail from event to verified action; the certification evidence is a report, not a folder hunt.
- Surveys/PROMs, SSI surveillance, M&M review, PDSA cycles and internal audit tools are deferred to the form designer (0.22.0) so they are configurable by TDS.
- The holiday logic needs a yearly check of the Matariki table (published to 2052).
