#!/usr/bin/env bash
# Builds the portable Linux package of App Launcher:
#   dist/GameDock-<version>-<rid>.tar.gz  ->  GameDock/ (self-contained app, icon, install.sh)
#
# Usage: packaging/build-linux.sh <linux-x64|linux-arm64> [version] [output dir]
set -euo pipefail

RID="${1:?runtime identifier required: linux-x64 or linux-arm64}"
VERSION="${2:-0.1.0}"
OUT="${3:-dist}"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/GameDock/GameDock/GameDock.csproj"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
APP_DIR="$STAGE/GameDock"

dotnet publish "$PROJECT" \
  -c Release \
  -r "$RID" \
  --self-contained true \
  -p:Version="$VERSION" \
  -p:DebugType=none \
  -o "$APP_DIR"

cp "$ROOT/packaging/linux/app-launcher.png" "$APP_DIR/"
cp "$ROOT/packaging/linux/install.sh" "$APP_DIR/"
chmod +x "$APP_DIR/GameDock" "$APP_DIR/install.sh"

mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
tar -C "$STAGE" -czf "$OUT/GameDock-$VERSION-$RID.tar.gz" GameDock
echo "Created $OUT/GameDock-$VERSION-$RID.tar.gz"
