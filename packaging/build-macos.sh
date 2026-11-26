#!/usr/bin/env bash
# Builds the macOS package of App Launcher (run on macOS):
#   dist/GameDock-<version>-<rid>.zip  ->  App Launcher.app
# The bundle is signed ad hoc (required on Apple silicon), not notarized: on first start use
# right-click > Open, or run: xattr -dr com.apple.quarantine "/Applications/App Launcher.app"
#
# Usage: packaging/build-macos.sh <osx-arm64|osx-x64> [version] [output dir]
set -euo pipefail

RID="${1:?runtime identifier required: osx-arm64 or osx-x64}"
VERSION="${2:-0.1.0}"
OUT="${3:-dist}"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/GameDock/GameDock/GameDock.csproj"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
BUNDLE="$STAGE/App Launcher.app"

mkdir -p "$BUNDLE/Contents/MacOS" "$BUNDLE/Contents/Resources"

dotnet publish "$PROJECT" \
  -c Release \
  -r "$RID" \
  --self-contained true \
  -p:Version="$VERSION" \
  -p:DebugType=none \
  -o "$BUNDLE/Contents/MacOS"

sed "s/__VERSION__/$VERSION/g" "$ROOT/packaging/macos/Info.plist" > "$BUNDLE/Contents/Info.plist"
cp "$ROOT/packaging/macos/App.icns" "$BUNDLE/Contents/Resources/App.icns"
chmod +x "$BUNDLE/Contents/MacOS/GameDock"

if command -v codesign >/dev/null 2>&1; then
  codesign --force --deep --sign - "$BUNDLE"
fi

mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
ARCHIVE="$OUT/GameDock-$VERSION-$RID.zip"
rm -f "$ARCHIVE"
if command -v ditto >/dev/null 2>&1; then
  ditto -c -k --keepParent "$BUNDLE" "$ARCHIVE"
else
  (cd "$STAGE" && zip -qry "$ARCHIVE" "App Launcher.app")
fi
echo "Created $ARCHIVE"
