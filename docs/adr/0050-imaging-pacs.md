# ADR 0050 – Imaging (PACS): X-rays and scans with the patient record

Status: Accepted (0.37.0)

## Context
BrightPAS only took in endoscopy pictures and video (ADR 0027). Lance asked (28 Nov 2026) for a PACS module that stores and shows imaging (X-ray and scans) linked to patients and episodes. The imaging device is not chosen yet (likely GE), so for now images come from a watched network folder or manual upload; a vendor link must be easy to add later.

## Decision
- New licensed module **imaging** ("Imaging (PACS)"), project `BrightPAS.Modules.Pacs` (namespace kept off `Imaging` so it does not clash with `Endoscopy.Imaging`), schema `imaging`: Requests, Studies, Series, Images. Permissions View / File and request (`imaging.import`) / Settings and delete (`imaging.manage`).
- **Files, not database blobs:** every DICOM object is kept exactly as received in the imaging folder (`imaging.storagePath`, default `<data>\Imaging` from Setup's `Imaging:StoragePath`) as `yyyy\MM\{id}.dcm` with a SHA-256 in the row. Same SOP Instance UID = stored once. Study/series follow the DICOM UIDs.
- **One entry point** `ImagingService.StoreAsync(bytes, source, who)` for upload (files, ZIP, a whole CD folder), the watched folder (`ImagingFolderWorker`, every minute, sub-folders, files left 30 s to finish copying, moved to processed\ or failed\) and the later network link.
- **Matching:** (1) accession number or study UID of a BrightPAS imaging request → that patient and episode; (2) if allowed (on by default), Patient ID equal to one patient's NHI or MRN **and** the same date of birth → that patient, and the episode when the patient has exactly one booking that day; (3) otherwise "To match". A person files it; a different date of birth must be confirmed. All filing, viewing, export and deletion is audited.
- **Requests** give accession numbers X + yyMMdd + 4 digits and a 2.25 study UID; `WorklistAsync` already returns them in Modality Worklist shape (NHI or MRN as Patient ID).
- **Viewer:** the server draws pictures with fo-dicom and its own PNG writer (no imaging library): 8–16-bit greyscale with rescale slope/intercept, window centre/width (file value, CT presets or adjusted), MONOCHROME1, invert, scaled to the screen; 8-bit colour; JPEG baseline passed through. Other compressions (JPEG lossless, JPEG 2000) are drawn only when a fo-dicom codec is installed – otherwise the image is kept and exported for a DICOM viewer. The browser shows it on a canvas with zoom, pan, stack scrolling (wheel/arrow keys), series and thumbnails. Labelled "for reference – not for primary diagnosis"; the radiologist's report (pasted in or typed as a note) is the record.
- Exports of the original DICOM files go through SecureExportService (encrypted ZIP).

## Later: vendor link (GE or other)
Add a DICOM node like `EndoscopyDicomHost` (own port and AE title, devices allowed by IP/AE title): C-ECHO, **C-STORE** → `StoreAsync(…, ImageSource.Network, …)`, **Modality Worklist C-FIND** from `WorklistAsync`, optionally MPPS, and query/retrieve from a GE PACS if TDS keeps one. Add `fo-dicom.Codecs` if the device sends compressed images. The watched folder and upload stay as the fallback.

## Consequences
- Disk space: CR/DX ≈ 10–30 MB per image, CT several hundred MB per study; back up the imaging folder with the database.
- Not a diagnostic viewer (no measurements, hanging protocols or calibrated display). Clinical governance to confirm use for reference only.
