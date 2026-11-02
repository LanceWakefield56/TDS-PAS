# Installer guide (sandbox testing)

## Step 1 – prerequisite check (available now)
1. Copy `Setup.exe` to the server VM (e.g. PAS-APP).
2. Right-click → **Run as administrator**. Windows may warn about an unknown publisher (not yet code-signed): choose *More info → Run anyway* in the sandbox only.
3. Enter the SQL Server VM name when asked (or press Enter to skip).
4. Read the results: **OK**, **WARN** (fine for testing), **FAIL** (fix before installing). Each problem shows "What to do".
5. A report `setup-report-<date>.txt` is saved next to Setup.exe – send it back for review.

Command-line options: `Setup.exe --sql-server PAS-SQL[:1433] --drive D:\ --quiet`
Exit codes: 0 ready · 1 ready with warnings · 2 not ready.

Nothing is installed or changed in step 1.
