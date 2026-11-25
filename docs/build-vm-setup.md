# BrightPAS build VM – setup

A Windows VM where Claude Code builds, tests and installs BrightPAS. One-time setup, about an hour.

## 1. The VM
- Windows 11 Pro or Windows Server 2025, 4+ cores, 16 GB RAM (8 GB minimum), 120 GB disk.
- Separate from the sandbox test server (BRIGHTPAS-SRV) so a bad build never touches it.
- Take a snapshot once the steps below are done.

## 2. Install (in this order)
1. **Git for Windows** – https://git-scm.com/download/win (defaults; this also gives you *Git Bash*, which the release scripts need).
2. **.NET 10 SDK** – https://dotnet.microsoft.com/download/dotnet/10.0 (SDK x64, not just the runtime).
3. **Node.js 22 LTS** – https://nodejs.org (tick "Automatically install the necessary tools" is not needed).
4. **SQL Server 2025 Developer** (free) – https://www.microsoft.com/sql-server/sql-server-downloads
   - Choose *Custom* › Database Engine; **Mixed Mode** authentication; set an `sa` password and note it.
   - Optional: SQL Server Management Studio.
5. **IIS** (to test real installs) – *Turn Windows features on or off* › Internet Information Services (Web Management Tools + World Wide Web Services), then the **ASP.NET Core 10 Hosting Bundle** from the .NET 10 download page.
6. **PowerShell 7** – https://aka.ms/powershell (the test robot's browser installer uses it).
7. **Claude Code** – follow *Claude Code › Set up* at https://docs.claude.com (Windows install). Sign in with your Claude account when it first starts.

Then open *Git Bash* once and run:
```
npm install -g docx
dotnet --version     # should start with 10.
node --version       # v22.x
```

## 3. Get the code (once)
Keep the working copy **outside OneDrive** (OneDrive and Git fight over files):
```
git clone "F:/OneDrive/TDS-PAS/TDS-PAS.bundle" C:/src/TDS-PAS
cd C:/src/TDS-PAS
git checkout main
git tag --sort=v:refname | tail -3     # latest should be v0.28.0
```
(If F: isn't mapped on the VM, copy `TDS-PAS.bundle` across first.)

Later, to back up to OneDrive after a session: `git bundle create "F:/OneDrive/TDS-PAS/TDS-PAS.bundle" --all`.
Better long-term: a **private GitHub repository** (push there instead of bundles) – ask Claude Code to set it up.

## 4. Settings for tests
In *System › Environment variables* (user), add:
- `PAS_TEST_SQLSERVER` = `Server=localhost;User ID=sa;Password=<your sa password>;TrustServerCertificate=True`
- `BRIGHTPAS_SQL_PASSWORD` = `<your sa password>`

## 5. First run
In *Git Bash*:
```
cd C:/src/TDS-PAS
claude
```
Then ask it: **"Read CLAUDE.md, check the toolchain, restore, build the UI, run all the tests, and tell me the result."**
All tests should pass (415 at 0.28.0). After that, ask it to build the release and install it on this VM with demo data, then run the test robot against it.
