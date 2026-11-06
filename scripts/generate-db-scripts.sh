#!/usr/bin/env bash
# Regenerates the idempotent SQL Server schema scripts embedded in Setup.exe.
# Run after adding or changing any EF Core migration.
set -euo pipefail
cd "$(dirname "$0")/.."
out=src/Installer/BrightPAS.Setup/Database
dotnet ef migrations script --idempotent --project src/Platform/BrightPAS.Core -o "$out/01-core.sql"
dotnet ef migrations script --idempotent --project src/Platform/BrightPAS.Identity -o "$out/02-identity.sql"
dotnet ef migrations script --idempotent --project src/Modules/BrightPAS.Modules.Patients -o "$out/03-patients.sql"
echo "Schema scripts written to $out"
