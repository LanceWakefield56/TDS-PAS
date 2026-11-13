# ADR 0027 – Endoscopy reporting and image/video capture

Status: Accepted (0.15.0)

## Context
TDS uses Olympus EVIS X1 towers. Lance asked for EndoBASE-like documentation: pictures and video recorded from the tower, structured gastroscopy and colonoscopy reporting (polyps, tattoos, biopsies …), and patient reports with pictures.

## Decision
- New module `endoscopy` (schema `endo`), licensed separately. One **Procedure** per theatre/procedure-room booking; DICOM **accession number** `E` + yyMMdd + 4 digits and a 2.25 UUID-derived **Study Instance UID** are given when the procedure is opened or the tower queries the worklist.
- **Two capture routes, both supported:**
  1. **DICOM** (`EndoscopyDicomHost`, fo-dicom 5.2.6): C-ECHO, Modality Worklist C-FIND (modality ES), C-STORE (JPEG baseline frames kept as-is; 8-bit RGB/YBR/greyscale written as PNG by our own encoder – no imaging library; MPEG-4 AVC fragments saved as MP4) and MPPS N-CREATE/N-SET. Separate port and AE title from the messaging engine. Devices accepted by the tower asset's network address or listed AE titles; "any device" is for testing only. Pictures that match no accession or study wait in an unmatched queue for a person to assign.
  2. **Capture card in the browser:** the card appears as a camera; getUserMedia + canvas JPEG for pictures and MediaRecorder (WebM/MP4) for video, uploaded in numbered 3-second chunks (append-only, out-of-order refused, retries ignored) so a closed browser loses at most a few seconds. Keyboard/foot pedal: Space and R (F9/F10).
- Media are **files** in a media folder (default `<data>\endoscopy`, set by Setup and inheriting the app pool's Modify right; can be changed in Settings), with a SHA-256 in the row; names are generated server-side; served with range requests for video seeking. Deleting removes the file and keeps the row with who/when/why. Optional video retention.
- **Terminology** follows MST-style minimum standard terminology and ESGE/BSG quality measures: BBPS per segment, caecal landmarks, withdrawal time, Gloucester comfort, Paris/NICE/JNET, LA/Prague/Forrest/Mayo grades. Lists live in `Terminology.cs` and need TDS endoscopy-lead review.
- **Sign** enforces the minimum dataset; signed reports are read-only until **amended** with a reason; histology may be added after signing and drives ADR/SSLDR and the surveillance suggestion (BSG/ACPGBI 2020 structure, labelled "confirm clinically").
- **Reports** are MigraDoc PDFs with embedded pictures (clinical and patient), through `SecureExportService`, or stored in patient documents once signed.
- Integration: scope use is written to Assets & CSSD scope events (refused if the scope is not ready); sedation medicines are read from the clinical record (eMR). Permissions-Policy now allows `camera=(self)`.

## Consequences
- The tower's DICOM options (worklist, storage, MPPS, video send) and AE titles must be confirmed with Olympus NZ for the TDS CV-1500 units; without them, use a capture card (e.g. HDMI/SDI-to-USB) on the procedure-room PC.
- Video needs disk space (≈ 50 MB per minute at 6 Mbit/s); set a retention period or plan storage.
- Clinical wording and surveillance logic need TDS clinical governance sign-off before live use; NBSP reporting extracts are a later step.
