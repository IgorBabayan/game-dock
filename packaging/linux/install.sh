#!/usr/bin/env bash
# Adds App Launcher to the desktop's application menu (per user, no root needed).
# Run it from the unpacked GameDock folder; run it again after moving the folder.
#   ./install.sh            add the menu entry
#   ./install.sh --remove   remove the menu entry
set -euo pipefail

APP_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
DESKTOP_FILE="$DATA_HOME/applications/app-launcher.desktop"
ICON_FILE="$DATA_HOME/icons/hicolor/256x256/apps/app-launcher.png"

if [[ "${1:-}" == "--remove" ]]; then
  rm -f "$DESKTOP_FILE" "$ICON_FILE"
  echo "App Launcher was removed from the application menu."
  exit 0
fi

mkdir -p "$(dirname "$DESKTOP_FILE")" "$(dirname "$ICON_FILE")"
cp "$APP_DIR/app-launcher.png" "$ICON_FILE"

cat > "$DESKTOP_FILE" <<DESKTOP
[Desktop Entry]
Type=Application
Name=App Launcher
Comment=Launcher and updater for gaming tools and companion applications
Exec="$APP_DIR/GameDock"
Path=$APP_DIR
Icon=app-launcher
Terminal=false
Categories=Game;Utility;
StartupWMClass=GameDock
DESKTOP

command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$(dirname "$DESKTOP_FILE")" || true
echo "App Launcher was added to the application menu."
