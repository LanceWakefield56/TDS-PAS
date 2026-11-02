# ADR 0004 – Installer technology

- **Status:** Accepted · **Date:** 2026-11-02

## Decision
The guided installer is a self-contained, single-file **Setup.exe** (.NET 10, trimmed, ~11 MB) that runs on Windows Server without .NET pre-installed. It is cross-compiled in the build environment, so no Windows build machine or WiX is required. Step 1 (this release) runs read-only prerequisite checks; later steps install and configure IIS, the database and modules, and hand over to the browser-based Setup Centre. Server facts are read through an `ISystemProbe` interface so every check is unit-tested with a fake server.

## Consequences
Until a code-signing certificate is purchased, Windows SmartScreen will warn that the publisher is unknown. Signing (osslsigncode or signtool) is added before production use.
