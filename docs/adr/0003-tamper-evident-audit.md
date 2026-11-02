# ADR 0003 – Tamper-evident audit log

- **Status:** Accepted · **Date:** 2026-11-02

## Decision
Audit records are protected twice: (1) an application-level SHA-256 hash chain (each record includes the previous record's hash; verification finds the first broken record), and (2) the `core.AuditEvents` table is a SQL Server 2022 **append-only ledger table**, created by raw SQL in the migration because EF Core cannot express it. Writes use a serialisable transaction to keep the sequence gap-free.

## Consequences
Edits, deletions and re-ordering are detectable even by a database administrator. The hash chain also works on non-SQL Server databases (tests use SQLite; it also supports the PostgreSQL alternative in Appendix B of the spec).
