# Local browser walk-through (developer use, not shipped)
- `DevDb.csproj` / `Program.cs`: creates database BrightPASDev from the installer SQL scripts and prints a first-admin code (`dotnet run` in a copy of this folder outside the solution).
- `run-web.sh`: runs the web app on https://localhost:5001 against that database (Production mode).
- `lib.js`, `flow.js`, `flow2.js`, `licence.js`: Playwright scripts (node) – first admin with TOTP, licence load, module set-up wizards, referral → triage → waitlist → booking. Screenshots go to /tmp/e2e-*.png.
