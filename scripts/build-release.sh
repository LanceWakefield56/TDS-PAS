#!/usr/bin/env bash
# Builds a release: UI, web package (Galenica-Web-<version>.zip), guided installer (Galenica-Setup-<version>.exe) and the installation guide (.docx).
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION=$(grep -oP '(?<=<Version>)[^<]+' Directory.Build.props)
OUT=$(realpath -m "${1:-artifacts/$VERSION}")
rm -rf "$OUT" && mkdir -p "$OUT"

echo "== UI"
(cd src/Platform/Galenica.Web/ClientApp && npm ci --silent && npm run build --silent)

echo "== Web application"
PUB=$(mktemp -d)
dotnet publish src/Platform/Galenica.Web -c Release -o "$PUB" --nologo -v q
rm -f "$PUB"/appsettings.Development.json "$PUB"/*.pdb
(cd "$PUB" && zip -qr "$OUT/Galenica-Web-$VERSION.zip" .)
rm -rf "$PUB"

echo "== Installer"
./scripts/generate-db-scripts.sh >/dev/null
SPUB=$(mktemp -d)
dotnet publish src/Installer/Galenica.Setup -c Release -r win-x64 -o "$SPUB" --nologo -v q
cp "$SPUB/Setup.exe" "$OUT/Galenica-Setup-$VERSION.exe"
rm -rf "$SPUB"
cp docs/installer.md "$OUT/Installer guide.md"

echo "== Installation guide"
NODE_PATH=$(npm root -g) node scripts/docs/install-guide.js "$OUT/Galenica-Installation-Guide-$VERSION.docx" "$VERSION"
ls -la "$OUT"
