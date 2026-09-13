#!/usr/bin/env bash
# Erzeugt verteilbare Linux-Pakete in dist/:
#
#   pixel-backup-<fassung>-linux-<arch>.tar.gz   portabel, läuft überall, ohne .NET
#   pixel-backup_<fassung>_<arch>.deb            Debian, Ubuntu, Mint, Pop!_OS …
#
# Beide enthalten eine einzige ausführbare Datei (self-contained, ~91 MiB) samt
# Startmenü-Eintrag und Symbolen. Für Arch liegt ein PKGBUILD in packaging/arch.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DIST="$REPO_ROOT/dist"

case "$(uname -m)" in
  x86_64|amd64)  RID="linux-x64";   DEB_ARCH="amd64" ;;
  aarch64|arm64) RID="linux-arm64"; DEB_ARCH="arm64" ;;
  *) echo "Nicht unterstützte Architektur: $(uname -m)" >&2; exit 1 ;;
esac

VERSION="$(grep -oPm1 '(?<=<Version>)[^<]+' "$REPO_ROOT/Directory.Build.props" || echo "1.0.0")"

echo "▸ Pixel Backup $VERSION für $RID"

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
mkdir -p "$DIST"

echo "▸ Baue eigenständige Fassung …"
dotnet publish "$REPO_ROOT/src/PixelBackup.App" \
  -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None \
  -o "$STAGE/build" >/dev/null

BINARY="$STAGE/build/PixelBackup"
[ -f "$BINARY" ] || { echo "Die Anwendung wurde nicht erzeugt." >&2; exit 1; }

install_shared_files() {
  # $1 = Wurzel des Paketbaums (mit oder ohne /usr)
  local root="$1" prefix="$2"

  install -D -m 0755 "$BINARY" "$root$prefix/lib/pixel-backup/PixelBackup"
  install -D -m 0644 "$REPO_ROOT/packaging/linux/pixel-backup.desktop" \
    "$root$prefix/share/applications/pixel-backup.desktop"

  for size in 16 24 32 48 64 128 256; do
    local icon="$REPO_ROOT/packaging/linux/icons/hicolor/${size}x${size}/apps/pixel-backup.png"
    [ -f "$icon" ] || continue
    install -D -m 0644 "$icon" "$root$prefix/share/icons/hicolor/${size}x${size}/apps/pixel-backup.png"
  done

  install -D -m 0644 "$REPO_ROOT/packaging/pixel-backup.svg" \
    "$root$prefix/share/icons/hicolor/scalable/apps/pixel-backup.svg"

  printf '#!/bin/sh\nexec "%s/lib/pixel-backup/PixelBackup" "$@"\n' "$prefix" \
    > "$STAGE/launcher"
  install -D -m 0755 "$STAGE/launcher" "$root$prefix/bin/pixel-backup"
}

# ------------------------------------------------------------------ tar.gz
echo "▸ Baue tar.gz …"
TAR_ROOT="$STAGE/tar/pixel-backup-$VERSION"
mkdir -p "$TAR_ROOT"
install -D -m 0755 "$BINARY" "$TAR_ROOT/PixelBackup"
install -D -m 0644 "$REPO_ROOT/packaging/linux/pixel-backup.desktop" "$TAR_ROOT/pixel-backup.desktop"
install -D -m 0644 "$REPO_ROOT/packaging/pixel-backup.svg" "$TAR_ROOT/pixel-backup.svg"
install -D -m 0644 "$REPO_ROOT/README.md" "$TAR_ROOT/README.md"
for size in 48 128 256; do
  install -D -m 0644 "$REPO_ROOT/packaging/linux/icons/hicolor/${size}x${size}/apps/pixel-backup.png" \
    "$TAR_ROOT/icons/pixel-backup-${size}.png"
done

cat > "$TAR_ROOT/install.sh" <<'INNER'
#!/bin/sh
# Trägt die enthaltene Anwendung für den angemeldeten Benutzer ins Startmenü ein.
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
PREFIX="${XDG_DATA_HOME:-$HOME/.local/share}/pixel-backup"
BIN="$HOME/.local/bin"
APPS="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
ICONS="${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor"

mkdir -p "$PREFIX" "$BIN" "$APPS" "$ICONS/48x48/apps" "$ICONS/128x128/apps" "$ICONS/256x256/apps"
cp "$HERE/PixelBackup" "$PREFIX/PixelBackup"
chmod +x "$PREFIX/PixelBackup"
printf '#!/bin/sh\nexec "%s/PixelBackup" "$@"\n' "$PREFIX" > "$BIN/pixel-backup"
chmod +x "$BIN/pixel-backup"
cp "$HERE/pixel-backup.desktop" "$APPS/pixel-backup.desktop"
for s in 48 128 256; do cp "$HERE/icons/pixel-backup-$s.png" "$ICONS/${s}x${s}/apps/pixel-backup.png"; done
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$APPS" >/dev/null 2>&1 || true
echo "Eingerichtet. Start über das Startmenü oder: pixel-backup"
echo "Geräteregeln fehlen noch? In der Anwendung unter „Komponenten“ einrichten."
INNER
chmod +x "$TAR_ROOT/install.sh"

TARBALL="$DIST/pixel-backup-$VERSION-$RID.tar.gz"
tar -czf "$TARBALL" -C "$STAGE/tar" "pixel-backup-$VERSION"

# --------------------------------------------------------------------- deb
echo "▸ Baue .deb …"
DEB_ROOT="$STAGE/deb"
install_shared_files "$DEB_ROOT" "/usr"

mkdir -p "$DEB_ROOT/DEBIAN"
INSTALLED_SIZE="$(du -ks "$DEB_ROOT/usr" | cut -f1)"

cat > "$DEB_ROOT/DEBIAN/control" <<CONTROL
Package: pixel-backup
Version: $VERSION
Section: utils
Priority: optional
Architecture: $DEB_ARCH
Maintainer: Pixel Backup <noreply@example.invalid>
Installed-Size: $INSTALLED_SIZE
Depends: libc6, libgcc-s1, libstdc++6, zlib1g, libfontconfig1, libx11-6, libice6, libsm6, libicu76 | libicu74 | libicu72 | libicu71 | libicu70 | libicu67
Recommends: android-sdk-platform-tools-common
Suggests: adb
Homepage: https://github.com/Tinnitus97/Pixel-Backup
Description: Sicherung und Wiederherstellung fuer Android-Geraete
 Pixel Backup sichert Fotos, Videos, Musik, Dokumente, Apps sowie Kontakte,
 Nachrichten und Termine eines Android-Geraetes auf den Rechner und spielt sie
 wieder zurueck - vollstaendig ueber adb, ohne App auf dem Telefon.
 .
 Die Sicherungen bleiben gewoehnliche Dateien und lassen sich auch ohne das
 Programm weiterverwenden. Fuer den Zugriff ohne Root-Rechte richtet das
 Programm auf Wunsch die noetigen udev-Regeln ein.
CONTROL

cat > "$DEB_ROOT/DEBIAN/postinst" <<'POSTINST'
#!/bin/sh
set -e
if [ "$1" = "configure" ]; then
    command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q /usr/share/applications || true
    command -v gtk-update-icon-cache  >/dev/null 2>&1 && gtk-update-icon-cache -q /usr/share/icons/hicolor || true
fi
exit 0
POSTINST
chmod 0755 "$DEB_ROOT/DEBIAN/postinst"

DEB="$DIST/pixel-backup_${VERSION}_${DEB_ARCH}.deb"
dpkg-deb --build --root-owner-group "$DEB_ROOT" "$DEB" >/dev/null

echo
echo "Fertig:"
ls -lh "$TARBALL" "$DEB" | awk '{printf "  %-52s %s\n", $9, $5}'
echo
echo "Installieren:"
echo "  sudo apt install $DEB"
echo "  oder: tar xzf $(basename "$TARBALL") && ./pixel-backup-$VERSION/install.sh"
