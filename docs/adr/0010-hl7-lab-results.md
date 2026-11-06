# ADR 0010 – HL7 lab result intake and patient matching

- **Status:** Accepted · **Date:** 2026-11-07

## Decision
- Lab results arrive as **HL7 v2 ORU^R01** over **MLLP** (TCP listener inside the web app, off unless `Labs:Mllp:Port` is set, optional client IP allow-list) or by manual import (Integration Administrator). A small tolerant parser lives in `BrightPAS.Messaging/Hl7`; it will be reused by the messaging engine.
- Every message is stored as received (`labs.Messages`) and answered with an ACK (AA accepted / duplicate, AR rejected). Duplicates (same sending facility + control id) are acknowledged but not filed twice.
- **Matching** uses NHI (then MRN) **and** requires date of birth and family name (macron-insensitive) to agree. Anything doubtful is held as **Unmatched** with a plain-English reason; staff with `labs.match` match it manually and must record how they confirmed identity (audited). Unmatched results cannot be acknowledged.
- Final/corrected reports supersede earlier versions of the same lab + accession + test for the same patient identifiers.
- Results go to an inbox (to acknowledge / abnormal / all / unmatched) with abnormal flags highlighted; acknowledgement records who, when and comment.

## Consequences
- MLLP is unencrypted; use on the internal network/VPN only until MLLP-TLS is added (backlog).
- In IIS the site must keep running for the listener: Setup sets AlwaysRunning, no idle timeout and preload (Application Initialization feature).
