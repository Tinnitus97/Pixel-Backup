#!/usr/bin/env bash
# Prüft die AppStream-Angaben so, wie es der Flatpak-Bau am Ende selbst tut.
#
#   ./packaging/flatpak/check-metainfo.sh
#
# Warum eigens: flatpak-builder ruft zum Schluss "appstreamcli compose" auf.
# Schlägt das fehl – etwa weil ein Symbol nicht lesbar ist –, bricht der ganze
# Bau nach mehreren Minuten ab. Diese Prüfung braucht zwei Sekunden und nennt
# denselben Fehler.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
APP_ID="io.github.tinnitus97.PixelBackup"

if ! command -v appstreamcli >/dev/null 2>&1; then
  echo "appstreamcli fehlt (Paket appstream) – Prüfung übersprungen." >&2
  exit 0
fi

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

FILES="$STAGE/files"
mkdir -p "$FILES/share/applications" "$FILES/share/metainfo"

# Genau der Baum, den die Bauanleitung in /app anlegt.
install -m 0644 "$REPO_ROOT/packaging/flatpak/$APP_ID.metainfo.xml" "$FILES/share/metainfo/"
sed 's/^Icon=pixel-backup$/Icon='"$APP_ID"'/' "$REPO_ROOT/packaging/linux/pixel-backup.desktop" \
  > "$FILES/share/applications/$APP_ID.desktop"

for size in 48 64 128 256; do
  install -D -m 0644 "$REPO_ROOT/packaging/linux/icons/hicolor/${size}x${size}/apps/pixel-backup.png" \
    "$FILES/share/icons/hicolor/${size}x${size}/apps/$APP_ID.png"
done

echo "▸ appstreamcli validate …"
appstreamcli validate --no-net "$FILES/share/metainfo/$APP_ID.metainfo.xml"

echo "▸ appstreamcli compose …"
appstreamcli compose \
  --origin=pixel-backup \
  --components="$APP_ID" \
  --prefix=/ \
  --result-root="$STAGE/out" \
  --data-dir="$STAGE/out/xmls" \
  --icons-dir="$STAGE/out/icons" \
  --hints-dir="$STAGE/hints" \
  "$FILES"

echo "Die AppStream-Angaben sind in Ordnung."
