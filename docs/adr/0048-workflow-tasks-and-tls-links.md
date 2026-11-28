# ADR 0048 – Workflow tasks, timers and escalation; MLLP and DICOM over TLS

Status: Accepted (0.35.0)

## Context
The messaging engine (ADR 0020 onward) moves and maps messages but cannot ask a person to do something, wait, or chase work that nobody did.
HL7 (MLLP) and DICOM links run in plain text on the local network; NZ health security guidance (HISO 10029) expects encryption in transit.
The endoscopy worklist (MWL) and procedure step (MPPS) already work (0.15–0.16); they gain TLS here. Lance approved 0.35 on 28 Nov 2026.

## Decision
Extends the Messaging module; one new permission, **tasks.work** ("Work on tasks"), given to every default role.

**Tasks** (`WorkflowService`, table msg.Tasks). A task has a title, optional detail, the role that does it, an optional time to do it by, an optional
escalation role, a priority, and optionally the patient and the message that made it. Everyone with the role sees it; one person takes it ("I'll do it")
or hands it back; it is finished as Done (with an optional note) or Not needed (with a reason). Finished tasks are kept with who and when. Tasks are
numbered T000001…. Integration managers see every task. Staff make tasks on the Tasks screen; flows make them with a Task step.

**Flow steps.** Task: makes a task when a message reaches it; `{field}` in the title is filled from the message (e.g. `{PID-5.1}`). A path may end at a
Task. Wait: holds the message for 1 minute to 30 days (table msg.FlowWaits), then carries on from the next step. A test run shows "Would wait" and carries
on so the whole flow can be checked. A flow must reach at least one Output or Task.

**Escalation.** The engine checks every 30 seconds: an open task past its due time with an escalation role moves to that role once, is marked Urgent
and loses its assignee; the move is audited (user "system"). Held messages whose time is up are released in the same cycle.

**TLS.** Interfaces with MLLP (listen or send) or DICOM listen can be encrypted: TLS 1.2 or 1.3 only. Certificates come from the Windows certificate
store by thumbprint (LocalMachine then CurrentUser, private key required) – BrightPAS never stores private keys. A listener needs its own certificate
and the partner's thumbprint (mutual TLS, pinned); a sender may pin the server and present its own certificate. Endoscopy settings get the same
switch for the DICOM node. Other transports already use https.

## Consequences
- Tasks are not a full BPMN engine: no parallel joins, no forms. Enough for "someone must check this" and "chase if late".
- TLS links need certificates issued and installed on the server and on each partner system; plain links still work and stay the default.
- Each partner's TLS support (lab engine, GE/Olympus towers) must be confirmed before switching it on.
