#!/usr/bin/env bash
# Builds a release: UI, web package (TDS-PAS-Web-<version>.zip) and the guided installer (Setup-<version>.exe).
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION=$(grep -oP '(?<=<Version>)[^<]+' Directory.Build.props)
OUT=$(realpath -m "${1:-artifacts/$VERSION}")
rm -rf "$OUT" && mkdir -p "$OUT"

echo "== UI"
(cd src/Platform/Tds.Pas.Web/ClientApp && npm ci --silent && npm run build --silent)

echo "== Web application"
PUB=$(mktemp -d)
dotnet publish src/Platform/Tds.Pas.Web -c Release -o "$PUB" --nologo -v q
rm -f "$PUB"/appsettings.Development.json "$PUB"/*.pdb
(cd "$PUB" && zip -qr "$OUT/TDS-PAS-Web-$VERSION.zip" .)
rm -rf "$PUB"

echo "== Installer"
./scripts/generate-db-scripts.sh >/dev/null
SPUB=$(mktemp -d)
dotnet publish src/Installer/Tds.Pas.Setup -c Release -r win-x64 -o "$SPUB" --nologo -v q
cp "$SPUB/Setup.exe" "$OUT/Setup-$VERSION.exe"
rm -rf "$SPUB"
cp docs/installer.md "$OUT/Installer guide.md"
ls -la "$OUT"
