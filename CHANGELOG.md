# Changelog

## 0.1.0 – 2026-11-02 (Phase 0, increment 1)
- Module framework: module manifests, dependency-ordered install plan, circular/missing dependency detection, safe-disable check.
- Organisation model (Organisation → Site → Facility → Location) with TDS default setup; Theatre 2 configured but inactive.
- NHI validation for old (AAANNNC) and new (AAANNAX) formats; test NHIs (Z…) flagged.
- Tamper-evident audit: SHA-256 hash chain, audit writer, SQL Server 2022 append-only ledger table.
- Initial EF Core migration for schema `core` (SQL Server).
- Guided installer Setup.exe (self-contained, Windows) – step 1 prerequisite checks with plain-English fixes and a saved report.
- Tests: 41 automated tests (core 30, installer 11), all passing.
- Docs: data dictionary (core), ADR 0003 (audit), ADR 0004 (installer), installer guide.

## 0.0.1 – 2026-11-02
- Repository skeleton and documentation; specification v1.6.
