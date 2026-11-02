# ADR 0002 – Interim source control without Azure DevOps

- **Status:** Accepted (interim)
- **Date:** 2026-11-02

## Context
TDS does not yet have Azure DevOps. The AI build environment is temporary and cleared after inactivity, so code must not live only there. The TDS-PAS folder is in OneDrive; putting a live `.git` folder in a OneDrive-synced folder risks corruption.

## Decision
- The full Git history is stored as a single **Git bundle** file, `TDS-PAS.bundle`, in the TDS-PAS OneDrive folder.
- A plain source snapshot, `TDS-PAS-source-latest.zip`, is stored beside it for easy browsing.
- At the start of each working session the bundle is cloned in the build environment; at the end, a new bundle and snapshot are written back.
- OneDrive version history gives an extra backup; TDS should also copy the folder to the TDS server or SharePoint regularly.

## Consequences
- No automatic pipeline builds: tests are run manually each session.
- Single source of truth is one file, so it must be backed up.
- Moving to Azure DevOps later: `git clone TDS-PAS.bundle`, add the Azure DevOps remote, `git push --all` (about 15 minutes, full history kept).
