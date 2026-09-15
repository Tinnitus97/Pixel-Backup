#!/usr/bin/env bash
# Erzeugt aus der Handbuchseite die Textfassung, die in der Anwendung steckt
# (pixel-backup --manual) und die unter Windows mitgeliefert wird.
#
#   packaging/man/render.sh
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC="$HERE/pixel-backup.1"
OUT="$HERE/pixel-backup.1.txt"

# Fett und unterstrichen kommen als Überdruck (Zeichen, Rücktaste, Zeichen)
# bzw. als Farbfolgen. Beides wird hier entfernt – "col" ist dafür nicht
# überall vorhanden, sed schon.
entferne_ueberdruck() {
  # UTF-8-Umgebung, sonst frisst "." nur ein Byte eines Umlauts.
  LC_ALL=C.UTF-8 sed -e 's/\x1b\[[0-9;]*m//g' -e 's/.\x08//g' -e 's/[[:space:]]*$//'
}

if command -v mandoc >/dev/null 2>&1; then
  mandoc -T utf8 -O width=88 "$SRC" | entferne_ueberdruck > "$OUT"
elif command -v groff >/dev/null 2>&1; then
  groff -man -T utf8 -rLL=88n "$SRC" | entferne_ueberdruck > "$OUT"
else
  echo "Weder mandoc noch groff vorhanden – Textfassung bleibt unverändert." >&2
  exit 0
fi

echo "$OUT ($(wc -l < "$OUT") Zeilen)"
