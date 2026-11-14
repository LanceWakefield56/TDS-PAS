# ADR 0028 – BrightPAS Scribe (AI dictation and ambient documentation)

Status: Accepted (0.16.0 – foundations)

## Context
Spec 7.22 asks for TDS's own AI dictation and ambient scribe, with the engine choice (on site vs cloud) decided as an ADR; spec 7.17 allows Medow Health instead, per facility. Drafts only; consent per patient; audio deleted after signing; no training on TDS data.

## Decision
- New module `scribe` (schema `scribe`), licensed separately. Sessions belong to a patient (and optionally a booking), a context and an **approved** template version.
- **Engines are pluggable over HTTP** (`ScribeEngines`): speech uses the OpenAI-compatible `/v1/audio/transcriptions` (verbose_json segments; works with on-site Whisper/faster-whisper/whisper.cpp servers and cloud services); drafting uses either nothing (template only), an OpenAI-compatible `/v1/chat/completions` on-site model, or the Claude Messages API. This lets TDS start with option A (all on site, preferred) or B (on-site speech, cloud drafting) without code changes.
- **Cloud drafting is always de-identified** (`ScribeText.DeIdentify`): the patient's names, NHI, MRN, date of birth, phone, email and address (plus anything NHI- or phone-shaped) become tokens; the draft is re-identified inside BrightPAS. Cloud use needs a recorded privacy reference (who/when stored).
- Drafts are requested as JSON sections of sentences with **source segment numbers**, stored in `DraftSentences`, so the review screen links each sentence to the conversation. Parsing tolerates surrounding text and drops invalid sources.
- **Safety checks** are deterministic (no AI): dose/unit patterns, "units", trailing zeros, a look-alike/sound-alike list, laterality, unsure markers and low-confidence words. NZMT lookup is a later step (the dictionary's Medicine category is used meanwhile).
- **Consent** rows (per session or standing, withdrawable, product named) gate audio upload and external drafts. **Audio** is stored encrypted (Data Protection) in numbered parts under `<data>\scribe`; deleted on sign/discard or after retention; abandoned sessions after 7 days.
- **Signing** is by the session's clinician only; it records the % of words changed (accuracy audit) and files a PDF document and (with a booking) a signed eMR note labelled AI-assisted with engines and edit %.
- **Medow**: enabled only with PIA, HISF supplier assessment and DPA references; minimum data payload; drafts return via `/api/scribe/inbound` with a bearer token (SHA-256 stored, constant-time compare, CSRF-exempt like other machine endpoints) or paste. The real Medow API is to be confirmed.

## Consequences
- An on-site speech server (PAS-SPEECH, GPU) or cloud agreement is needed before live dictation; until then the module can run with Medow, pasted drafts or template-only drafting.
- Clinical templates, the look-alike list and dictionary need clinical governance review; accuracy audits and incident reporting apply.
- Speaker separation depends on the speech engine; clinicians can relabel speakers.
