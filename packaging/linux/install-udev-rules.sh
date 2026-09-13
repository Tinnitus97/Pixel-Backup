#!/usr/bin/env bash
# Richtet die Android-Geräteregeln ein, damit adb ohne Root-Rechte auf das
# Telefon zugreifen darf. Dieselben Regeln schreibt auch die Anwendung selbst
# unter „Komponenten“.
set -euo pipefail

RULES="/etc/udev/rules.d/51-android.rules"
GROUP="plugdev"
# $USER ist nicht in jeder Umgebung gesetzt – deshalb id -un als Rückfall.
USER_NAME="${SUDO_USER:-${USER:-$(id -un)}}"

VENDORS="0502:Acer 0b05:ASUS 413c:Dell 0489:Foxconn 091e:Garmin-Asus 18d1:Google 201e:Haier
109b:Hisense 12d1:Huawei 0bb4:HTC 24e3:K-Touch 2116:KT-Tech 0482:Kyocera 17ef:Lenovo 1004:LG
0e8d:MediaTek 22b8:Motorola 0409:NEC 2080:Nook 0955:Nvidia 2a70:OnePlus 22d9:OPPO 10a9:Pantech
1d4d:Pegatron 0471:Philips 05c6:Qualcomm 04e8:Samsung 04dd:Sharp 0fce:Sony 2340:Teleepoch
0930:Toshiba 2d95:Vivo 2717:Xiaomi 19d2:ZTE 2ae5:Fairphone 1bbb:TCL 2916:Android 0e79:Archos"

TMP="$(mktemp)"
{
  echo "# Android-Geräteregeln, erzeugt von Pixel Backup."
  echo "# TAG+=\"uaccess\" gibt das Gerät an die angemeldete Sitzung frei,"
  echo "# GROUP=\"$GROUP\" deckt Systeme ohne logind ab."
  echo
  for entry in $VENDORS; do
    id="${entry%%:*}"; name="${entry##*:}"
    echo "# $name"
    echo "SUBSYSTEM==\"usb\", ATTR{idVendor}==\"$id\", MODE=\"0660\", GROUP=\"$GROUP\", TAG+=\"uaccess\""
  done
} > "$TMP"

SUDO=""; [ "$(id -u)" -eq 0 ] || SUDO="sudo"

# -D legt /etc/udev/rules.d an, falls es die Verteilung noch nicht hat.
$SUDO install -D -m 0644 "$TMP" "$RULES"
rm -f "$TMP"

getent group "$GROUP" >/dev/null 2>&1 || $SUDO groupadd -f "$GROUP"
id -nG "$USER_NAME" | tr ' ' '\n' | grep -qx "$GROUP" || $SUDO usermod -aG "$GROUP" "$USER_NAME"

if command -v udevadm >/dev/null 2>&1; then
  $SUDO udevadm control --reload-rules || true
  $SUDO udevadm trigger --subsystem-match=usb || true
else
  echo "Hinweis: udevadm nicht gefunden – die Regeln greifen nach dem nächsten Anstecken."
fi

echo "Geräteregeln eingerichtet: $RULES"
echo "Bitte das Gerät einmal abziehen und wieder anstecken."
echo "Falls der Zugriff weiterhin fehlt: einmal ab- und wieder anmelden (Gruppe $GROUP)."
