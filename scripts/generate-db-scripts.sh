#!/usr/bin/env bash
# Regenerates the idempotent SQL Server schema scripts embedded in Setup.exe.
# Run after adding or changing any EF Core migration.
set -euo pipefail
cd "$(dirname "$0")/.."
out=src/Installer/Tds.Pas.Setup/Database
dotnet ef migrations script --idempotent --project src/Platform/Tds.Pas.Core -o "$out/01-core.sql"
dotnet ef migrations script --idempotent --project src/Platform/Tds.Pas.Identity -o "$out/02-identity.sql"
dotnet ef migrations script --idempotent --project src/Modules/Tds.Pas.Modules.Patients -o "$out/03-patients.sql"
echo "Schema scripts written to $out"
