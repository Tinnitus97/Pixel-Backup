#!/usr/bin/env bash
# Baut Pixel Backup und richtet es unter Linux ein (Startmenü-Eintrag inklusive).
#
#   ./packaging/linux/install.sh            # nur für den angemeldeten Benutzer
#   ./packaging/linux/install.sh --system   # systemweit nach /opt (benötigt Rechte)
#   ./packaging/linux/install.sh --udev     # zusätzlich die Geräteregeln einrichten
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MODE="user"
WITH_UDEV="no"

for arg in "$@"; do
  case "$arg" in
    --system) MODE="system" ;;
    --udev)   WITH_UDEV="yes" ;;
    -h|--help)
      sed -n '2,6p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
      exit 0 ;;
    *) echo "Unbekannte Option: $arg" >&2; exit 2 ;;
  esac
done

case "$(uname -m)" in
  x86_64|amd64)  RID="linux-x64" ;;
  aarch64|arm64) RID="linux-arm64" ;;
  *) echo "Nicht unterstützte Architektur: $(uname -m)" >&2; exit 1 ;;
esac

if ! command -v dotnet >/dev/null 2>&1; then
  echo "Das .NET-SDK fehlt. Je nach Verteilung:"
  echo "  Debian/Ubuntu : sudo apt install dotnet-sdk-10.0"
  echo "  Arch          : sudo pacman -S dotnet-sdk"
  echo "  Fedora        : sudo dnf install dotnet-sdk-10.0"
  echo "  openSUSE      : sudo zypper install dotnet-sdk-10.0"
  exit 1
fi

if [ "$MODE" = "system" ]; then
  PREFIX="/opt/pixel-backup"
  BIN_DIR="/usr/local/bin"
  DESKTOP_DIR="/usr/share/applications"
  ICON_ROOT="/usr/share/icons/hicolor"
  MAN_DIR="/usr/share/man/man1"
  SUDO=""
  [ "$(id -u)" -eq 0 ] || SUDO="sudo"
else
  PREFIX="${XDG_DATA_HOME:-$HOME/.local/share}/pixel-backup"
  BIN_DIR="$HOME/.local/bin"
  DESKTOP_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
  ICON_ROOT="${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor"
  MAN_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/man/man1"
  SUDO=""
fi

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

echo "▸ Baue Pixel Backup für $RID …"
dotnet publish "$REPO_ROOT/src/PixelBackup.App" \
  -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None \
  -o "$STAGE" >/dev/null

echo "▸ Installiere nach $PREFIX …"
$SUDO rm -rf "$PREFIX"
$SUDO mkdir -p "$PREFIX" "$BIN_DIR" "$DESKTOP_DIR"
$SUDO cp -r "$STAGE"/. "$PREFIX/"
$SUDO chmod +x "$PREFIX/PixelBackup"

printf '#!/bin/sh\nexec "%s/PixelBackup" "$@"\n' "$PREFIX" > "$STAGE/launcher"
$SUDO install -m 0755 "$STAGE/launcher" "$BIN_DIR/pixel-backup"

echo "▸ Richte Startmenü-Eintrag ein …"
$SUDO install -m 0644 "$REPO_ROOT/packaging/linux/pixel-backup.desktop" "$DESKTOP_DIR/pixel-backup.desktop"

for size in 16 24 32 48 64 128 256; do
  src="$REPO_ROOT/packaging/linux/icons/hicolor/${size}x${size}/apps/pixel-backup.png"
  [ -f "$src" ] || continue
  $SUDO mkdir -p "$ICON_ROOT/${size}x${size}/apps"
  $SUDO install -m 0644 "$src" "$ICON_ROOT/${size}x${size}/apps/pixel-backup.png"
done

# Handbuchseite: danach beantwortet "man pixel-backup" alle Fragen zur Kommandozeile.
$SUDO mkdir -p "$MAN_DIR"
$SUDO install -m 0644 "$REPO_ROOT/packaging/man/pixel-backup.1" "$MAN_DIR/pixel-backup.1"

command -v update-desktop-database >/dev/null 2>&1 && $SUDO update-desktop-database "$DESKTOP_DIR" >/dev/null 2>&1 || true
command -v gtk-update-icon-cache   >/dev/null 2>&1 && $SUDO gtk-update-icon-cache -q "$ICON_ROOT" >/dev/null 2>&1 || true

if [ "$WITH_UDEV" = "yes" ]; then
  echo "▸ Richte Geräteregeln ein …"
  "$REPO_ROOT/packaging/linux/install-udev-rules.sh"
fi

echo
echo "Fertig. Start über das Startmenü oder mit: pixel-backup"
if [ "$MODE" = "user" ] && ! printf '%s' "$PATH" | tr ':' '\n' | grep -qx "$BIN_DIR"; then
  echo "Hinweis: $BIN_DIR liegt nicht in PATH – dann mit $BIN_DIR/pixel-backup starten."
fi
if [ "$WITH_UDEV" != "yes" ]; then
  echo "Geräteregeln fehlen noch? In der Anwendung unter „Komponenten“ einrichten"
  echo "oder: ./packaging/linux/install-udev-rules.sh"
fi
