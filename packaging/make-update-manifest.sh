#!/usr/bin/env bash
# Erzeugt update.json – die Datei, die der Versionscheck der Anwendung liest.
#
#   ./packaging/make-update-manifest.sh --version 0.9.1 --tag v0.9.1 --dist dist
#
# Gelesen wird der Ordner mit den fertigen Paketen; für jede erkannte Datei
# entsteht ein Eintrag mit Einbauart, Architektur, Adresse und SHA-256.
# Die Adresse zeigt auf die Dateien der Veröffentlichung mit diesem Namen.
set -euo pipefail

REPO="Tinnitus97/Pixel-Backup"
VERSION=""
TAG=""
DIST="dist"
OUT=""
RELEASED="$(date -u +%Y-%m-%d)"

while [ $# -gt 0 ]; do
  case "$1" in
    --version) VERSION="${2:-}"; shift 2 ;;
    --tag)     TAG="${2:-}"; shift 2 ;;
    --dist)    DIST="${2:-}"; shift 2 ;;
    --out)     OUT="${2:-}"; shift 2 ;;
    --repo)    REPO="${2:-}"; shift 2 ;;
    --released) RELEASED="${2:-}"; shift 2 ;;
    -h|--help) sed -n '2,9p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unbekannte Option: $1" >&2; exit 2 ;;
  esac
done

[ -n "$VERSION" ] || { echo "--version fehlt" >&2; exit 2; }
[ -n "$TAG" ] || TAG="v$VERSION"
[ -n "$OUT" ] || OUT="$DIST/update.json"
[ -d "$DIST" ] || { echo "Ordner nicht gefunden: $DIST" >&2; exit 2; }

BASE="https://github.com/$REPO/releases/download/$TAG"

# Ordnet eine Datei ihrer Einbauart und Architektur zu.
classify() {
  local name="$1" lower
  lower="$(printf '%s' "$name" | tr '[:upper:]' '[:lower:]')"

  local arch=""
  case "$lower" in
    *arm64*|*aarch64*) arch="arm64" ;;
    *x64*|*amd64*|*x86_64*) arch="x64" ;;
  esac

  case "$lower" in
    *.exe)      printf 'windows-exe %s' "${arch:-x64}" ;;
    *.deb)      printf 'deb %s' "$([ "$arch" = arm64 ] && echo arm64 || echo amd64)" ;;
    *.rpm)      printf 'rpm %s' "$([ "$arch" = arm64 ] && echo aarch64 || echo x86_64)" ;;
    *.appimage) printf 'appimage %s' "$([ "$arch" = arm64 ] && echo aarch64 || echo x86_64)" ;;
    *.flatpak)  printf 'flatpak %s' "$([ "$arch" = arm64 ] && echo aarch64 || echo x86_64)" ;;
    *.tar.gz)   printf 'tarball %s' "${arch:-x64}" ;;
    *)          printf '' ;;
  esac
}

entries=""
count=0

for file in "$DIST"/*; do
  [ -f "$file" ] || continue
  name="$(basename "$file")"
  [ "$name" = "update.json" ] && continue
  [ "$name" = "SHA256SUMS" ] && continue

  read -r kind arch <<<"$(classify "$name")" || true
  [ -n "${kind:-}" ] || continue

  sha="$(sha256sum "$file" | cut -d' ' -f1)"
  size="$(stat -c%s "$file")"

  [ -n "$entries" ] && entries="$entries,"
  entries="$entries
    {
      \"kind\": \"$kind\",
      \"arch\": \"$arch\",
      \"url\": \"$BASE/$name\",
      \"sha256\": \"$sha\",
      \"size\": $size
    }"
  count=$((count + 1))
done

[ "$count" -gt 0 ] || { echo "Keine Pakete in $DIST gefunden." >&2; exit 1; }

mkdir -p "$(dirname "$OUT")"
cat > "$OUT" <<JSON
{
  "schema": 1,
  "version": "$VERSION",
  "released": "$RELEASED",
  "notes": "https://github.com/$REPO/releases/tag/$TAG",
  "packages": [$entries
  ]
}
JSON

echo "$OUT – $count Pakete, Fassung $VERSION ($TAG)"
