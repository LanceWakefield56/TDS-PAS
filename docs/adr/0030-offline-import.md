# ADR 0030 – Import from file when a connection is down

Status: Accepted (0.18.0)

## Context
Every device and outside system BrightPAS talks to can lose its network link: the laboratory's MLLP connection, the GE CARESCAPE Gateway, SaferSleep, the Olympus towers (DICOM), a microphone or the network in a consulting room. TDS asked that each of these can also be fed from a file – USB stick, network share or a file the sending system saved – so work is never blocked and nothing has to be retyped.

## Decision
- **One way in for every message.** A file is split into the messages the live connection would have delivered, and each goes through the same path: the messaging engine's inbound interface for that module when there is one (stored, viewable, acknowledged, flows run), otherwise straight to the module. No separate "import" logic in the modules, so matching, safety checks and audit are identical.
- **`MessageFile`** (BrightPAS.Messaging) does the splitting: HL7 files with many messages, MLLP framing, FHS/BHS batch envelopes, CR/LF/CRLF line ends, a byte-order mark and Latin-1 legacy files; ZIP archives opened one level deep with limits (2,000 entries, 500 MB an entry, 2 GB in total, sizes checked while reading so a ZIP cannot lie about them); DICOMDIR and hidden files skipped; DICOM recognised by its preamble, not its name.
- **Duplicates:** labs and SaferSleep already refuse a repeated message control ID; DICOM by SOP instance UID (now also for video); monitor readings by monitor + measurement + time. Importing a file twice is safe.
- **Monitor readings from a file are filed by time:** each reading goes to the patient connected to that monitor at the moment it was taken (not whoever is connected now), the monitor is not shown as online, and nobody is auto-connected by NHI. Readings appear in the trend; charting stays a nurse decision.
- **Watched shares:** any inbound HL7 interface can be a "Watch a folder" interface with a module handler (each file may hold many messages); the endoscopy module has its own import folder for DICOM. Files move to `processed\` or `failed\`.
- **Upload endpoints** take one file per request (raw body, name in `X-File-Name`, URL-encoded); the browser sends each chosen file in turn and shows the result of every message. Permissions: lab matching for lab files, devices.use for monitor and SaferSleep files, endoscopy.write for DICOM, scribe.use for recordings (consent still required), integration management for any interface. Every import is audit-logged.
- **Scribe:** a recording made elsewhere (dictaphone, phone) can be uploaded to a session instead of recording live; type is recognised from the file's first bytes.

## Consequences
- Staff have a documented fallback for every connection (guide H24); the robot tests each one.
- Real export formats (Delta lab queue files, CARESCAPE Gateway exports, Olympus USB exports) must be checked when the sample files arrive – the splitter accepts the common variants.
