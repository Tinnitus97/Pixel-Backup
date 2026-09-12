# Pixel Backup

Eine Windows-Anwendung (C# / .NET 8 / Avalonia) zum vollständigen Sichern und Wiederherstellen
von **Android-Geräten** – ähnlich wie Samsung Smart Switch, aber herstellerunabhängig und ohne
zusätzliche App auf dem Telefon.

Die gesamte Gerätekommunikation läuft nativ über **adb** (Android Debug Bridge). Damit funktioniert
das Programm mit jedem Android-Gerät, das USB-Debugging beherrscht – Google Pixel, Samsung, Xiaomi,
OnePlus, Motorola, Sony, Fairphone und andere.

> Die Sicherungen sind **keine undurchsichtigen Container**: Fotos bleiben Fotos, Dokumente bleiben
> Dokumente. Jeder Sicherungssatz lässt sich im Explorer öffnen und einzeln weiterverwenden.

---

## Funktionsumfang

### Vollsicherung
* Sichert den internen Speicher, die installierten Apps und Datenexporte in einem Durchgang.
* Sicherungssätze werden pro Gerät und Zeitpunkt abgelegt, inklusive Manifest (`manifest.json`)
  und lesbarem Bericht (`bericht.txt`).

### Gruppenweise sichern
Statt „alles oder nichts“ lässt sich jede Gruppe einzeln an- und abwählen:

| Gruppe | Inhalt |
| --- | --- |
| 🖼 Fotos | `DCIM`, `Pictures`, `Camera`, `Screenshots` – jpg, png, heic, webp, dng, … |
| 🎬 Videos | `DCIM`, `Movies`, `Pictures` – mp4, mkv, mov, … |
| 🎵 Musik & Aufnahmen | `Music`, `Recordings`, `Podcasts`, `Audiobooks` sowie herstellereigene Aufnahmeordner (Samsung `Sounds`, Xiaomi `MIUI/sound_recorder`, Sony, LG) |
| 📄 Dokumente | `Documents`, `Books` |
| ⬇ Downloads | kompletter Download-Ordner |
| 💬 Messenger-Medien | WhatsApp (auch Business), Telegram, Signal, Threema (Mediendateien) |
| 🔔 Klingeltöne & Töne | `Ringtones`, `Notifications`, `Alarms` |
| 📶 Bluetooth-Empfang | `Bluetooth`, `NearbyShare` |
| 🗂 Sonstige Dateien | alles Übrige im internen Speicher (ohne `Android/`) |
| 📦 Apps (APK) | alle selbst installierten Apps inkl. Split-APKs |
| 🗄 App-Daten | klassische `adb backup`-Sicherung (siehe Grenzen) |
| 👤 Kontakte | Export über den Content-Provider (Rohdaten + CSV) |
| ✉ SMS & MMS | Export über den Content-Provider (Rohdaten + CSV) |
| 📞 Anrufliste | Export über den Content-Provider (Rohdaten + CSV) |
| ⚙ Systemeinstellungen | `settings list system/secure/global` als Dokumentation |

Die Ordnerlisten decken bewusst auch herstellereigene Pfade ab; alles, was dort nicht erfasst
ist, fängt die Gruppe „Sonstige Dateien“ auf. So bleibt die Sicherung auf jedem Android-Gerät
vollständig, ohne Sonderbehandlung je Marke.

Überschneidungen werden automatisch aufgelöst: Ein Video im Ordner `DCIM` landet genau einmal in
der Sicherung, auch wenn „Fotos“ und „Videos“ gleichzeitig gewählt sind.

### Wiederherstellen
* Auswahl von Sicherungssatz **und** Gruppen.
* Konfliktstrategie je Lauf: vorhandene Dateien behalten, überschreiben oder beide behalten.
* Apps werden per `adb install` bzw. `install-multiple` (Split-APKs) installiert.
* Nach dem Kopieren wird der Medienscanner angestoßen, damit Fotos sofort in der Galerie erscheinen.

### Weitere Funktionen
* **Analyse vorab** – zeigt je Gruppe Anzahl und Größe an, bevor etwas übertragen wird.
* **Inkrementelle Sicherung** – überträgt nur neue und geänderte Dateien (Vergleich über Größe und
  Zeitstempel); auf Wunsch werden am Gerät gelöschte Dateien auch aus der Sicherung entfernt.
* **Stapelübertragung** – bis zu 40 Dateien pro adb-Aufruf, was bei vielen kleinen Dateien um ein
  Vielfaches schneller ist als Einzelaufrufe.
* **Prüfsummen & Überprüfung** – SHA-256 je Datei; eine Sicherung lässt sich jederzeit gegen das
  Manifest prüfen (fehlend / falsche Größe / falsche Prüfsumme).
* **ZIP-Archiv mit optionaler Verschlüsselung** – AES-256-GCM, Schlüssel über PBKDF2 (210 000
  Iterationen) aus dem Kennwort.
* **Aufbewahrungsregel** – ältere Sicherungssätze je Gerät automatisch aufräumen.
* **Automatische Sicherung**, sobald ein Gerät angeschlossen wird (optional).
* **WLAN-Modus** – `adb tcpip` aktivieren und drahtlos weiterarbeiten.
* **Fortschritt mit Geschwindigkeit und Restzeit**, jederzeit abbrechbar; abgebrochene Läufe
  hinterlassen einen gültigen, weiterverwendbaren Sicherungssatz.
* **Protokoll** in der Oberfläche und als Tagesdatei unter `%APPDATA%\PixelBackup\logs`.
* Helles und dunkles Erscheinungsbild.

---

## Voraussetzungen

* Windows 10/11 (das Programm läuft dank Avalonia auch unter Linux und macOS).
* [.NET 8 SDK](https://dotnet.microsoft.com/download) zum Bauen bzw. .NET 8 Desktop Runtime zum Ausführen.
* [Android-Plattform-Tools](https://developer.android.com/tools/releases/platform-tools) (`adb`).
  Pixel Backup sucht `adb` automatisch in PATH, im Android-SDK, neben der Anwendung
  (`platform-tools\adb.exe`) und an den üblichen Installationsorten. Alternativ lässt sich der Pfad
  in den Einstellungen setzen.
* Am Gerät: **Entwickleroptionen** aktivieren (siebenmal auf die Build-Nummer tippen) und
  **USB-Debugging** einschalten. Beim ersten Anschließen die Abfrage am Telefon bestätigen.

---

## Bauen und starten

```bash
dotnet restore
dotnet build -c Release
dotnet run --project src/PixelBackup.App
```

Eigenständige Windows-Datei erzeugen:

```bash
dotnet publish src/PixelBackup.App -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -o publish
```

Tests:

```bash
dotnet test
```

---

## Bedienung in Kürze

1. **Gerät** – Telefon anschließen, Status prüfen, gegebenenfalls adb-Server neu starten.
2. **Sichern** – Gruppen wählen, auf *Analysieren* tippen (zeigt Umfang), dann *Sicherung starten*.
3. **Sicherungen** – Sätze ansehen, prüfen, archivieren, löschen, Ordner öffnen.
4. **Wiederherstellen** – Satz und Gruppen wählen, Konfliktstrategie festlegen, starten.
5. **Einstellungen** – Zielordner, adb-Pfad, Aufbewahrung, Automatik, Erscheinungsbild.

---

## Aufbau eines Sicherungssatzes

```
<Sicherungsordner>/
└── Pixel_8_1A2B3C4D/                  Gerät (Modell + Seriennummer)
    └── 2026-09-12_18-30-11/           Zeitpunkt des Satzes
        ├── manifest.json              Inhaltsverzeichnis inkl. Prüfsummen und Laufhistorie
        ├── bericht.txt                lesbare Zusammenfassung
        ├── files/sdcard/DCIM/…        1:1-Abbild des internen Speichers
        ├── apps/<paketname>/*.apk     Installationsdateien
        ├── appdata/app-daten.ab       klassische App-Daten-Sicherung (falls gewählt)
        └── data/kontakte-….txt|.csv   Datenexporte
```

Ein inkrementeller Lauf aktualisiert den jüngsten Satz des Gerätes und hängt einen weiteren Eintrag
an die Laufhistorie im Manifest an.

---

## Grenzen (ehrlich gesagt)

* **App-Daten**: Android hat `adb backup` ab Version 12 praktisch stillgelegt – die meisten Apps
  liefern keine Daten mehr. Ohne Root ist das systembedingt nicht zu umgehen; gesichert werden
  deshalb zuverlässig die APKs, nicht die Spielstände. Für Chatverläufe bleiben die
  app-eigenen Sicherungen (z. B. WhatsApp → Google Drive) der richtige Weg.
* **Kontakte, SMS, Anrufliste**: Der Zugriff über `content query` hängt von Hersteller und
  Android-Version ab. Klappt er nicht, meldet das Protokoll dies deutlich und die übrigen Gruppen
  laufen normal weiter. Die Exporte sind Rohdaten (plus CSV), kein 1:1-Rückweg.
* **`/sdcard/Android/data` und `obb`** werden bewusst ausgelassen: Ohne Root ist der Zugriff
  ab Android 11 gesperrt.
* **Systemeinstellungen** werden dokumentiert, aber nicht automatisch zurückgeschrieben –
  das Zurückschreiben einzelner Werte kann ein Gerät unbrauchbar machen.
* **Sehr lange Pfade**: Unter Windows sollte die Unterstützung langer Pfade aktiviert sein, wenn
  tief verschachtelte Ordner gesichert werden.

---

## Projektstruktur

```
src/PixelBackup.Core/   Fachlogik ohne UI: adb-Hülle, Kategorien, Sicherung, Wiederherstellung,
                        Prüfung, Archivierung, Einstellungen
src/PixelBackup.App/    Avalonia-Oberfläche (MVVM, ohne zusätzliche MVVM-Abhängigkeit)
tests/PixelBackup.Tests/ xUnit-Tests für Pfadabbildung, Parser, Manifest, Stapelbildung, Krypto
```

Details zur Architektur: [docs/ARCHITEKTUR.md](docs/ARCHITEKTUR.md).
