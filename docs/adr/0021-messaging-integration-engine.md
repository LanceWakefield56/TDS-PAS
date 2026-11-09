# ADR 0021 – TDS's own messaging & integration engine (0.12.0)

Status: accepted (2026-11-10)

## Context
Spec 6.2: TDS builds its own messaging and workflow engine instead of buying Orion Health Rhapsody or MuleSoft. Lance asked for a Rhapsody-style GUI to create and test message translations and flows (especially DICOM → HL7), plus a SQL connector, an MCP server and an AI troubleshooting assistant, with security and privacy reviewed before sandbox testing.

## Decisions
1. **Module `messaging` (schema `msg`)**, self-contained project `BrightPAS.Modules.Messaging` (engine, mappings, flows) with the transports in the web host (`MessagingHost`). It runs integrated now and can move to its own server later (spec 6.2.4). Other modules only *record* their traffic through `IMessageTap` (Core) – HealthLink, SMS and email keep sending themselves.
2. **Message store:** content encrypted with the Data Protection cipher, SHA-256 of the plain text (tamper check on viewing), every view audited. Outbound queue with back-off 1, 2, 5, 10, 30, 60, 120, 240 min and a dead-letter state after *MaxAttempts* (setting). Replay/resend creates a new linked message and needs a reason.
3. **Flows** are graphs (FlowNode/FlowEdge/FlowCondition, 3NF) – Input, Filter, Decision (Yes/No), Mapper, Output. Checks: one input, reachable outputs, labelled branches, no loops, and the message format at each step must fit the mapper/output. The test harness runs the in-memory drawing (draft mappings optional) and stores/sends nothing.
4. **Mappings** are versioned (MappingVersion + MappingRule + FilterRule). A version goes Draft → AwaitingApproval (sample must map cleanly) → Live only when approved by someone other than the editor/submitter (setting, on by default); rollback puts a retired version back live immediately with a reason. Paths: HL7 `SEG[n]-f.c.s`, JSON `a.b[0].c`, DICOM keywords/tags with sequences, XML slash paths with `@attr`, X12 like HL7. `[*]` repeats a rule per occurrence.
5. **DICOM** via fo-dicom 5.2.6 (MS-PL, permissive). Headers are stored as the DICOM JSON model (PS3.18 F) with pixel data, OB/OW/UN and private tags removed; original files can be archived to a folder. The DICOM node accepts only devices registered by IP **and** AE title, and only its own called AE title. Worklist (MWL) and MPPS come with the endoscopy module.
6. **XML/CDA** parsing prohibits DTDs and external entities (XXE). **X12** separators are read from the ISA segment.
7. **SQL connector:** SQL Server via Microsoft.Data.SqlClient; single statement, no `;` or comments, no DDL/xp_/OPENROWSET etc.; SELECT-only for inbound (watermark passed as `@watermark`), INSERT/UPDATE/MERGE/EXEC for outbound with values only as `@parameters` from the JSON message (no DELETE). Connection strings encrypted; Encrypt must not be Optional. Use least-privilege logins.
8. **Inbound REST** (`/api/integration/in/{code}`) and **MCP** (`/api/mcp`) are machine-to-machine: no cookies, so CSRF does not apply; they authenticate with keys (REST: per-interface key, constant-time compare; MCP: bearer tokens stored as SHA-256, expiring, revocable), are rate-limited per IP and licence-gated.
9. **MCP** is hand-written JSON-RPC 2.0 over HTTP POST (no SSE) with read-only tools (`engine_status`, `list_problem_messages`, `get_message`, `list_flows`). It never returns message content; errors and step details pass through `Redactor` (NHI, quoted values, long numbers, emails masked); NHIs are shown masked.
10. **AI assistant:** off by default; switching on requires a privacy assessment reference and confirmation (who/when recorded). The payload is built by `AiAssistService.PayloadAsync`: interface settings, redacted step history and errors, the message **skeleton** (values replaced by shapes like A(4)/9(8); only message-structure fields such as MSH type, OBX code/units are kept), flow structure and mapping rules (translation tables by name and size only). The user sees the payload before sending; each request is audited with its size and hash. The answer is advice only – mapping changes still need second-person approval. A built-in rule-based explainer covers common failures without sending anything.

## Consequences
- No licence cost or vendor lock-in; TDS (and its future developer) maintains the engine (spec risk R16).
- The AI service is outside NZ/TDS: the PIA must cover it before use (Lance is Privacy Officer). With it off, nothing leaves TDS.
- Not yet: visual drag-to-map lines, C# extension scripts, MLLP/DICOM over TLS, DICOM MWL/MPPS, workflow engine human tasks/escalations.
