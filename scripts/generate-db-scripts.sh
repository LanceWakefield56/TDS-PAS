#!/usr/bin/env bash
# Regenerates the idempotent SQL Server schema scripts embedded in Setup.exe.
# Run after adding or changing any EF Core migration.
set -euo pipefail
cd "$(dirname "$0")/.."
out=src/Installer/BrightPAS.Setup/Database
dotnet ef migrations script --idempotent --project src/Platform/BrightPAS.Core -o "$out/01-core.sql"
dotnet ef migrations script --idempotent --project src/Platform/BrightPAS.Identity -o "$out/02-identity.sql"
dotnet ef migrations script --idempotent --project src/Modules/BrightPAS.Modules.Patients -o "$out/03-patients.sql"
dotnet ef migrations script --idempotent --project src/Modules/BrightPAS.Modules.Labs -o "$out/04-labs.sql"
dotnet ef migrations script --idempotent --project src/Modules/BrightPAS.Modules.Theatre -o "$out/05-theatre.sql"
dotnet ef migrations script --idempotent --project src/Modules/BrightPAS.Modules.Forms -o "$out/06-forms.sql"
dotnet ef migrations script --idempotent --project src/Modules/BrightPAS.Modules.Billing -o "$out/07-billing.sql"
dotnet ef migrations script --idempotent --project src/Modules/BrightPAS.Modules.Reminders -o "$out/08-reminders.sql"
dotnet ef migrations script --idempotent --project src/Modules/BrightPAS.Modules.Referrals -o "$out/09-referrals.sql"
dotnet ef migrations script --idempotent --project src/Modules/BrightPAS.Modules.Letters -o "$out/10-letters.sql"
dotnet ef migrations script --idempotent --project src/Modules/BrightPAS.Modules.Rostering -o "$out/11-roster.sql"
dotnet ef migrations script --idempotent --project src/Modules/BrightPAS.Modules.Credentialing -o "$out/12-credentialing.sql"
dotnet ef migrations script --idempotent --project src/Modules/BrightPAS.Modules.Portal -o "$out/13-portal.sql"
dotnet ef migrations script --idempotent --project src/Modules/BrightPAS.Modules.Messaging -o "$out/14-messaging.sql"
dotnet ef migrations script --idempotent --project src/Modules/BrightPAS.Modules.Assets -o "$out/15-assets.sql"
dotnet ef migrations script --idempotent --project src/Modules/BrightPAS.Modules.Emr -o "$out/16-emr.sql"
dotnet ef migrations script --idempotent --project src/Modules/BrightPAS.Modules.Endoscopy -o "$out/17-endoscopy.sql"
dotnet ef migrations script --idempotent --project src/Modules/BrightPAS.Modules.Scribe -o "$out/18-scribe.sql"
echo "Schema scripts written to $out"
