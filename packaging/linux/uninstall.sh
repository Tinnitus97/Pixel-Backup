#!/usr/bin/env bash
# Entfernt Pixel Backup wieder. Sicherungen und Einstellungen bleiben erhalten.
set -euo pipefail

MODE="user"
[ "${1:-}" = "--system" ] && MODE="system"

if [ "$MODE" = "system" ]; then
  PREFIX="/opt/pixel-backup"; BIN="/usr/local/bin/pixel-backup"
  DESKTOP="/usr/share/applications/pixel-backup.desktop"; ICON_ROOT="/usr/share/icons/hicolor"
  SUDO=""; [ "$(id -u)" -eq 0 ] || SUDO="sudo"
else
  PREFIX="${XDG_DATA_HOME:-$HOME/.local/share}/pixel-backup"; BIN="$HOME/.local/bin/pixel-backup"
  DESKTOP="${XDG_DATA_HOME:-$HOME/.local/share}/applications/pixel-backup.desktop"
  ICON_ROOT="${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor"; SUDO=""
fi

$SUDO rm -rf "$PREFIX"
$SUDO rm -f "$BIN" "$DESKTOP"
for size in 16 24 32 48 64 128 256; do
  $SUDO rm -f "$ICON_ROOT/${size}x${size}/apps/pixel-backup.png"
done

echo "Entfernt. Nicht angetastet:"
echo "  Sicherungen:   ${HOME}/PixelBackup (oder der eingestellte Ordner)"
echo "  Einstellungen: ${XDG_CONFIG_HOME:-$HOME/.config}/PixelBackup"
echo "  adb:           ${XDG_DATA_HOME:-$HOME/.local/share}/PixelBackup/platform-tools"
