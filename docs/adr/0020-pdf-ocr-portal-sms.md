# ADR 0020 – Password-protected PDFs, OCR text search, portal links by SMS (0.11.0)

Status: accepted (2026-11-09)

## Context
Lance asked for password-protected PDF letters and reports alongside the encrypted ZIP, searchable scanned documents, and patient portal links sent automatically by SMS with the pre-admission form and estimate available in the portal.

## Decisions
1. **PDF library: PDFsharp-MigraDoc 6.2.0 (MIT), pinned.** PDFsharp 6.3 and later (including 6.5) are dual-licensed AGPL/commercial and throw a licence exception without a key – **do not upgrade** without a commercial licence decision. PDFs are encrypted with `PdfDefaultEncryption.V5` (AES-256, /AESV3); user password = export password, random owner password, printing allowed, editing not. Fonts: Liberation Sans (SIL OFL) embedded as resources – no dependence on server fonts.
2. PDFs go through `SecureExportService.ExportPdfAsync`, the same policy (protection required/optional, minimum length) and audit as ZIP exports. ZIP remains for CSV/multi-file exports.
3. **OCR: Tesseract 5.2 (charlesw wrapper, Apache 2.0)** with `eng.traineddata` (tessdata_fast) shipped in `tessdata/`. Windows natives are bundled; the server needs the **VC++ Redistributable x64**. If the engine cannot start, the admin sees why on Security & printers and documents stay Pending/Failed (retryable) – nothing else is affected.
4. PDF text is read from the PDF's own text layer first (PDFsharp content streams); only pages without text are OCR'd (JPEG, 8-bit grey/RGB Flate, CCITT G4 images; max 30 pages). PdfPig was rejected (package source offered only a non-standard version).
5. Extracted text lives in `patients.DocumentTexts` (1:1 with Documents), **encrypted with the same Data Protection cipher** as the file. Search is per patient only (decrypt in memory, audited). No full-text index – deliberately, so no plaintext clinical text is held in SQL Server.
6. Background `DocumentTextWorker` processes 5 documents every 20 s, up to 3 attempts, one OCR at a time.
7. **Portal links by SMS** use the Reminders outbox (new kind PortalLink). The secret link is stored only until sent, the logged body shows "[portal link]", it cannot be resent, and a new link cancels any queued one. Automatic sending on booking is an admin setting (off by default) and only fires when the patient has no active link.
8. Portal shows questionnaire state and the latest issued, unexpired estimate; acceptance sets the estimate to Accepted and raises a portal request for staff. Both endpoints require link + date of birth and are rate-limited.
