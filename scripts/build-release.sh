#!/usr/bin/env bash
# Builds a release: UI, web package (BrightPAS-Web-<version>.zip), guided installer (BrightPAS-Setup-<version>.exe) and the installation guide (.docx).
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION=$(grep -oP '(?<=<Version>)[^<]+' Directory.Build.props)
OUT=$(realpath -m "${1:-artifacts/$VERSION}")
rm -rf "$OUT" && mkdir -p "$OUT"

echo "== UI"
(cd src/Platform/BrightPAS.Web/ClientApp && npm ci --silent && npm run build --silent)

echo "== Web application"
PUB=$(mktemp -d)
dotnet publish src/Platform/BrightPAS.Web -c Release -o "$PUB" --nologo -v q
rm -f "$PUB"/appsettings.Development.json "$PUB"/*.pdb
(cd "$PUB" && zip -qr "$OUT/BrightPAS-Web-$VERSION.zip" .)
rm -rf "$PUB"

echo "== Installer"
./scripts/generate-db-scripts.sh >/dev/null
SPUB=$(mktemp -d)
dotnet publish src/Installer/BrightPAS.Setup -c Release -r win-x64 -p:PublishTrimmed=false -o "$SPUB" --nologo -v q
cp "$SPUB/Setup.exe" "$OUT/BrightPAS-Setup-$VERSION.exe"
rm -rf "$SPUB"
cp docs/installer.md "$OUT/Installer guide.md"

echo "== Sandbox licence and vendor tools"
cp licensing/sandbox/BrightPAS-Sandbox-Licence.lic "$OUT/"
mkdir -p "$OUT/Vendor"
LPUB=$(mktemp -d)
dotnet publish tools/BrightPAS.LicenceManager -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=false -o "$LPUB" --nologo -v q
cp "$LPUB/BrightPAS-LicenceManager.exe" "$OUT/Vendor/BrightPAS-LicenceManager-$VERSION.exe"
rm -rf "$LPUB"

echo "== Test robot (demonstration systems only)"
TPUB=$(mktemp -d)
dotnet publish tools/BrightPAS.TestBot -c Release -r win-x64 --self-contained -p:PublishTrimmed=false -o "$TPUB/BrightPASTestBot" --nologo -v q
rm -f "$TPUB"/BrightPASTestBot/*.pdb
cp tools/BrightPAS.TestBot/README.md "$TPUB/BrightPASTestBot/How to run the test robot.md"
(cd "$TPUB" && zip -qr "$OUT/BrightPAS-TestBot-$VERSION.zip" BrightPASTestBot)
rm -rf "$TPUB"

echo "== Installation guide"
NODE_PATH=$(npm root -g) node scripts/docs/install-guide.js "$OUT/BrightPAS-Installation-Guide-$VERSION.docx" "$VERSION"
ls -la "$OUT"
