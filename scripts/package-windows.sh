#!/bin/zsh
# Builds dist/AntCloud-<version>-win-x64.zip: a self-contained AntCloud.exe (no .NET install needed) with
# Moonlight's portable release in a Moonlight folder beside it. Runs on macOS or Linux; the zip runs on
# Windows 10/11 x64. Usage: scripts/package-windows.sh
set -euo pipefail
cd "$(dirname "$0")/.."

VERSION=$(sed -nE 's/.*AppVersion = "([^"]+)".*/\1/p' Core/Config.cs)
MOONLIGHT_VERSION=6.2.0
OUT=dist/AntCloud
CACHE=dist/cache

rm -rf "$OUT" && mkdir -p "$OUT" "$CACHE"

dotnet publish -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=none -o "$OUT"

# Portable Moonlight keeps its settings in an ini in its own folder (marked by portable.dat), so AntCloud's
# pairings stay out of any Moonlight the user installed.
ZIP="$CACHE/MoonlightPortable-x64-$MOONLIGHT_VERSION.zip"
[[ -f "$ZIP" ]] || curl -fL -o "$ZIP" \
  "https://github.com/moonlight-stream/moonlight-qt/releases/download/v$MOONLIGHT_VERSION/MoonlightPortable-x64-$MOONLIGHT_VERSION.zip"
EXTRACT=$(mktemp -d)
unzip -q "$ZIP" -d "$EXTRACT"
EXE=$(find "$EXTRACT" -name Moonlight.exe | head -1)
mv "$(dirname "$EXE")" "$OUT/Moonlight"
rm -rf "$EXTRACT"
touch "$OUT/Moonlight/portable.dat"

cat > "$OUT/README.txt" <<EOF
AntCloud for Windows $VERSION (unofficial)

Run AntCloud.exe. Keep the Moonlight folder next to it; AntCloud streams through it.
Logs: %APPDATA%\AntCloud\Logs\antcloud.log

Streaming by Moonlight $MOONLIGHT_VERSION (GPLv3, https://moonlight-stream.org), included unmodified.
EOF

rm -f "dist/AntCloud-$VERSION-win-x64.zip"
(cd dist && zip -qr "AntCloud-$VERSION-win-x64.zip" AntCloud)
echo "dist/AntCloud-$VERSION-win-x64.zip"
