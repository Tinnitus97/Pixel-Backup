#!/usr/bin/env bash
# Erzeugt verteilbare Linux-Pakete in dist/:
#
#   pixel-backup-<fassung>-linux-<arch>.tar.gz   portabel, jede Verteilung
#   pixel-backup_<fassung>_<arch>.deb            Debian, Ubuntu, Mint, Pop!_OS …
#   pixel-backup-<fassung>-1.<arch>.rpm          Fedora, openSUSE, RHEL …
#   PixelBackup-<fassung>-<arch>.AppImage        läuft ohne Installation
#   pixel-backup-<fassung>.flatpak               Flatpak-Bündel
#
# Alle enthalten eine einzige ausführbare Datei (self-contained, ~91 MiB) samt
# Startmenü-Eintrag und Symbolen. Kein .NET auf dem Zielrechner nötig.
#
#   ./package.sh                           alle Formate, fehlende Werkzeuge werden gemeldet
#   ./package.sh --formats deb,rpm         nur bestimmte Formate
#   ./package.sh --require-all             bricht ab, wenn ein Format nicht gebaut werden kann
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DIST="$REPO_ROOT/dist"
CACHE="${XDG_CACHE_HOME:-$HOME/.cache}/pixel-backup-packaging"
APP_ID="io.github.tinnitus97.PixelBackup"

FORMATS="tar,deb,rpm,appimage,flatpak"
REQUIRE_ALL="no"

while [ $# -gt 0 ]; do
  case "$1" in
    --formats) FORMATS="${2:-}"; shift 2 ;;
    --formats=*) FORMATS="${1#*=}"; shift ;;
    --require-all) REQUIRE_ALL="yes"; shift ;;
    -h|--help) sed -n '2,18p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unbekannte Option: $1" >&2; exit 2 ;;
  esac
done

wants() { printf '%s' ",$FORMATS," | grep -q ",$1,"; }

case "$(uname -m)" in
  x86_64|amd64)  RID="linux-x64";   DEB_ARCH="amd64"; RPM_ARCH="x86_64";  APPIMAGE_ARCH="x86_64" ;;
  aarch64|arm64) RID="linux-arm64"; DEB_ARCH="arm64"; RPM_ARCH="aarch64"; APPIMAGE_ARCH="aarch64" ;;
  *) echo "Nicht unterstützte Architektur: $(uname -m)" >&2; exit 1 ;;
esac

VERSION="$(grep -oPm1 '(?<=<Version>)[^<]+' "$REPO_ROOT/Directory.Build.props" || echo "1.0.0")"

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
mkdir -p "$DIST" "$CACHE"

BUILT=""
SKIPPED=""

note_skip() {
  SKIPPED="$SKIPPED\n  $1: $2"
  if [ "$REQUIRE_ALL" = "yes" ]; then
    echo "✖ $1 lässt sich nicht bauen: $2" >&2
    exit 1
  fi
  echo "  übersprungen ($2)"
}

# --------------------------------------------------------------- Anwendung
echo "▸ Pixel Backup $VERSION für $RID"
echo "▸ Baue eigenständige Fassung …"

dotnet publish "$REPO_ROOT/src/PixelBackup.App" \
  -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None \
  -o "$STAGE/build" >/dev/null

BINARY="$STAGE/build/PixelBackup"
[ -f "$BINARY" ] || { echo "Die Anwendung wurde nicht erzeugt." >&2; exit 1; }

ICON_DIR="$REPO_ROOT/packaging/linux/icons/hicolor"
DESKTOP="$REPO_ROOT/packaging/linux/pixel-backup.desktop"
SVG="$REPO_ROOT/packaging/pixel-backup.svg"

# Legt den üblichen /usr-Baum unterhalb von $1 an (für deb und rpm).
stage_usr_tree() {
  local root="$1"

  install -D -m 0755 "$BINARY" "$root/usr/lib/pixel-backup/PixelBackup"
  install -D -m 0644 "$DESKTOP" "$root/usr/share/applications/pixel-backup.desktop"
  install -D -m 0644 "$SVG" "$root/usr/share/icons/hicolor/scalable/apps/pixel-backup.svg"

  for size in 16 24 32 48 64 128 256; do
    [ -f "$ICON_DIR/${size}x${size}/apps/pixel-backup.png" ] || continue
    install -D -m 0644 "$ICON_DIR/${size}x${size}/apps/pixel-backup.png" \
      "$root/usr/share/icons/hicolor/${size}x${size}/apps/pixel-backup.png"
  done

  printf '#!/bin/sh\nexec /usr/lib/pixel-backup/PixelBackup "$@"\n' > "$STAGE/launcher"
  install -D -m 0755 "$STAGE/launcher" "$root/usr/bin/pixel-backup"
}

# -------------------------------------------------------------------- tar.gz
build_tar() {
  echo "▸ tar.gz …"
  local root="$STAGE/tar/pixel-backup-$VERSION"
  mkdir -p "$root/icons"

  install -D -m 0755 "$BINARY" "$root/PixelBackup"
  install -D -m 0644 "$DESKTOP" "$root/pixel-backup.desktop"
  install -D -m 0644 "$SVG" "$root/pixel-backup.svg"
  install -D -m 0644 "$REPO_ROOT/README.md" "$root/README.md"
  for size in 48 128 256; do
    install -D -m 0644 "$ICON_DIR/${size}x${size}/apps/pixel-backup.png" "$root/icons/pixel-backup-${size}.png"
  done

  cat > "$root/install.sh" <<'INNER'
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
  chmod +x "$root/install.sh"

  tar -czf "$DIST/pixel-backup-$VERSION-$RID.tar.gz" -C "$STAGE/tar" "pixel-backup-$VERSION"
  BUILT="$BUILT pixel-backup-$VERSION-$RID.tar.gz"
}

# ----------------------------------------------------------------------- deb
build_deb() {
  echo "▸ .deb …"
  if ! command -v dpkg-deb >/dev/null 2>&1; then
    note_skip "deb" "dpkg-deb fehlt (Paket dpkg-dev)"
    return
  fi

  local root="$STAGE/deb"
  stage_usr_tree "$root"
  mkdir -p "$root/DEBIAN"

  cat > "$root/DEBIAN/control" <<CONTROL
Package: pixel-backup
Version: $VERSION
Section: utils
Priority: optional
Architecture: $DEB_ARCH
Maintainer: Pixel Backup <noreply@example.invalid>
Installed-Size: $(du -ks "$root/usr" | cut -f1)
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

  cat > "$root/DEBIAN/postinst" <<'POSTINST'
#!/bin/sh
set -e
if [ "$1" = "configure" ]; then
    command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q /usr/share/applications || true
    command -v gtk-update-icon-cache  >/dev/null 2>&1 && gtk-update-icon-cache -q /usr/share/icons/hicolor || true
fi
exit 0
POSTINST
  chmod 0755 "$root/DEBIAN/postinst"

  dpkg-deb --build --root-owner-group "$root" "$DIST/pixel-backup_${VERSION}_${DEB_ARCH}.deb" >/dev/null
  BUILT="$BUILT pixel-backup_${VERSION}_${DEB_ARCH}.deb"
}

# ----------------------------------------------------------------------- rpm
build_rpm() {
  echo "▸ .rpm …"
  if ! command -v rpmbuild >/dev/null 2>&1; then
    note_skip "rpm" "rpmbuild fehlt (Paket rpm bzw. rpm-build)"
    return
  fi

  local root="$STAGE/rpmroot"
  stage_usr_tree "$root"

  local top="$STAGE/rpmtop"
  mkdir -p "$top/BUILD" "$top/RPMS" "$top/SOURCES" "$top/SPECS" "$top/SRPMS"

  cat > "$top/SPECS/pixel-backup.spec" <<SPEC
# Die Anwendung ist bereits fertig gebaut; rpm soll sie nicht nachbearbeiten.
%global __os_install_post %{nil}
%global debug_package %{nil}
%global __brp_check_rpaths %{nil}
%global _build_id_links none

Name:           pixel-backup
Version:        $VERSION
Release:        1
Summary:        Sicherung und Wiederherstellung fuer Android-Geraete
License:        Proprietary
URL:            https://github.com/Tinnitus97/Pixel-Backup
BuildArch:      $RPM_ARCH

Requires:       fontconfig
Requires:       zlib
Recommends:     android-tools

%description
Pixel Backup sichert Fotos, Videos, Musik, Dokumente, Apps sowie Kontakte,
Nachrichten und Termine eines Android-Geraetes auf den Rechner und spielt sie
wieder zurueck - vollstaendig ueber adb, ohne App auf dem Telefon.

Die Sicherungen bleiben gewoehnliche Dateien und lassen sich auch ohne das
Programm weiterverwenden. Fuer den Zugriff ohne Root-Rechte richtet das
Programm auf Wunsch die noetigen udev-Regeln ein.

%install
cp -a $root/. %{buildroot}/

%files
/usr/bin/pixel-backup
/usr/lib/pixel-backup/PixelBackup
/usr/share/applications/pixel-backup.desktop
/usr/share/icons/hicolor/*/apps/pixel-backup.*

%changelog
* Sun Sep 13 2026 Pixel Backup <noreply@example.invalid> - $VERSION-1
- Erste Fassung
SPEC

  rpmbuild -bb \
    --define "_topdir $top" \
    --define "_rpmdir $STAGE/rpmout" \
    --target "$RPM_ARCH" \
    "$top/SPECS/pixel-backup.spec" >"$STAGE/rpmbuild.log" 2>&1 || {
      tail -20 "$STAGE/rpmbuild.log" >&2
      note_skip "rpm" "rpmbuild ist fehlgeschlagen (siehe Ausgabe oben)"
      return
    }

  local built
  built="$(find "$STAGE/rpmout" -name '*.rpm' | head -1)"
  if [ -z "$built" ]; then
    note_skip "rpm" "rpmbuild hat keine Datei erzeugt"
    return
  fi

  cp "$built" "$DIST/"
  BUILT="$BUILT $(basename "$built")"
}

# ------------------------------------------------------------------ AppImage
build_appimage() {
  echo "▸ .AppImage …"
  if ! command -v mksquashfs >/dev/null 2>&1; then
    note_skip "appimage" "mksquashfs fehlt (Paket squashfs-tools)"
    return
  fi

  local runtime="${APPIMAGE_RUNTIME:-$CACHE/runtime-$APPIMAGE_ARCH}"
  if [ ! -f "$runtime" ]; then
    echo "  hole die AppImage-Laufzeit …"
    curl -sSL --max-time 300 -o "$runtime.part" \
      "https://github.com/AppImage/type2-runtime/releases/download/continuous/runtime-$APPIMAGE_ARCH" || {
        rm -f "$runtime.part"
        note_skip "appimage" "Laufzeit nicht ladbar (APPIMAGE_RUNTIME=... setzen)"
        return
      }
    mv "$runtime.part" "$runtime"
  fi

  local appdir="$STAGE/AppDir"
  install -D -m 0755 "$BINARY" "$appdir/usr/bin/PixelBackup"
  install -D -m 0644 "$DESKTOP" "$appdir/usr/share/applications/pixel-backup.desktop"
  install -D -m 0644 "$ICON_DIR/256x256/apps/pixel-backup.png" \
    "$appdir/usr/share/icons/hicolor/256x256/apps/pixel-backup.png"

  # Eine AppImage erwartet Startdatei, Eintrag und Symbol auch im Wurzelverzeichnis.
  install -m 0644 "$DESKTOP" "$appdir/pixel-backup.desktop"
  install -m 0644 "$ICON_DIR/256x256/apps/pixel-backup.png" "$appdir/pixel-backup.png"
  install -m 0644 "$SVG" "$appdir/pixel-backup.svg"

  cat > "$appdir/AppRun" <<'APPRUN'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
export PATH="$HERE/usr/bin:$PATH"
exec "$HERE/usr/bin/PixelBackup" "$@"
APPRUN
  chmod 0755 "$appdir/AppRun"

  mksquashfs "$appdir" "$STAGE/app.squashfs" \
    -root-owned -noappend -no-xattrs -comp zstd -Xcompression-level 19 -b 1M >/dev/null

  local target="$DIST/PixelBackup-$VERSION-$APPIMAGE_ARCH.AppImage"
  cat "$runtime" "$STAGE/app.squashfs" > "$target"
  chmod 0755 "$target"
  BUILT="$BUILT $(basename "$target")"
}

# ------------------------------------------------------------------- Flatpak
build_flatpak() {
  echo "▸ .flatpak …"
  if ! command -v flatpak-builder >/dev/null 2>&1; then
    note_skip "flatpak" "flatpak-builder fehlt (Paket flatpak-builder)"
    return
  fi

  if ! flatpak info org.freedesktop.Sdk//24.08 >/dev/null 2>&1; then
    note_skip "flatpak" "Laufzeit fehlt: flatpak install flathub org.freedesktop.Platform//24.08 org.freedesktop.Sdk//24.08"
    return
  fi

  local src="$STAGE/flatpak-src"
  mkdir -p "$src"

  install -m 0755 "$BINARY" "$src/PixelBackup"
  install -m 0644 "$REPO_ROOT/packaging/flatpak/$APP_ID.metainfo.xml" "$src/"
  install -m 0644 "$REPO_ROOT/packaging/flatpak/$APP_ID.yml" "$src/"
  install -m 0644 "$SVG" "$src/icon.svg"
  for size in 48 64 128 256; do
    install -m 0644 "$ICON_DIR/${size}x${size}/apps/pixel-backup.png" "$src/icon-${size}.png"
  done

  # Flatpak verlangt Eintrag und Symbol unter der Anwendungskennung.
  sed 's/^Icon=pixel-backup$/Icon='"$APP_ID"'/' "$DESKTOP" > "$src/$APP_ID.desktop"
  printf '#!/bin/sh\nexec /app/lib/pixel-backup/PixelBackup "$@"\n' > "$src/pixel-backup.sh"
  chmod 0755 "$src/pixel-backup.sh"

  ( cd "$src" && flatpak-builder --repo="$STAGE/repo" --force-clean --disable-rofiles-fuse \
      "$STAGE/flatpak-build" "$APP_ID.yml" ) >"$STAGE/flatpak.log" 2>&1 || {
        tail -20 "$STAGE/flatpak.log" >&2
        note_skip "flatpak" "flatpak-builder ist fehlgeschlagen (siehe Ausgabe oben)"
        return
      }

  flatpak build-bundle "$STAGE/repo" "$DIST/pixel-backup-$VERSION.flatpak" "$APP_ID" \
    >>"$STAGE/flatpak.log" 2>&1 || {
      tail -20 "$STAGE/flatpak.log" >&2
      note_skip "flatpak" "build-bundle ist fehlgeschlagen"
      return
    }

  BUILT="$BUILT pixel-backup-$VERSION.flatpak"
}

wants tar      && build_tar
wants deb      && build_deb
wants rpm      && build_rpm
wants appimage && build_appimage
wants flatpak  && build_flatpak

echo
echo "Fertig in $DIST:"
for f in $BUILT; do
  [ -f "$DIST/$f" ] && printf '  %-46s %s\n' "$f" "$(du -h "$DIST/$f" | cut -f1)"
done

if [ -n "$SKIPPED" ]; then
  echo
  printf 'Nicht gebaut:%b\n' "$SKIPPED"
fi

echo
echo "Einspielen:"
echo "  sudo apt install ./dist/pixel-backup_${VERSION}_${DEB_ARCH}.deb"
echo "  sudo dnf install ./dist/pixel-backup-${VERSION}-1.${RPM_ARCH}.rpm"
echo "  chmod +x dist/PixelBackup-${VERSION}-${APPIMAGE_ARCH}.AppImage && ./dist/PixelBackup-…AppImage"
echo "  flatpak install --user ./dist/pixel-backup-${VERSION}.flatpak"
echo "  tar xzf dist/pixel-backup-${VERSION}-${RID}.tar.gz && ./pixel-backup-${VERSION}/install.sh"
