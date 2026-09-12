# Architektur

## Überblick

```
┌───────────────────────────┐        ┌──────────────────────────────┐
│ PixelBackup.App (Avalonia)│        │ PixelBackup.Core             │
│  Views (XAML)             │        │  Adb/      Prozesshülle      │
│  ViewModels (MVVM)        │ ─────▶ │  Model/    Kategorien, Plan  │
│  Services/AppSession      │        │  Services/ Sichern, Restore  │
└───────────────────────────┘        │  Util/     Pfade, Hashes     │
                                     └──────────────┬───────────────┘
                                                    │ Prozessaufrufe
                                                    ▼
                                            adb (Android-Gerät)
```

`PixelBackup.Core` kennt keine Oberfläche und lässt sich unabhängig testen oder in einem
Kommandozeilenwerkzeug wiederverwenden.

## Gerätekommunikation

Alles läuft über Prozessaufrufe an `adb`; es gibt keine gerätespezifischen Sonderwege:

| Aufgabe | Kommando |
| --- | --- |
| Geräte auflisten | `adb devices -l` |
| Geräteinfos | `adb shell getprop …`, `df -k /sdcard`, `dumpsys battery` |
| Dateien finden | `adb shell find <dir> -type f -exec stat -c '%s\|%Y\|%n' {} +` |
| Dateien holen | `adb pull -a <datei…> <zielordner>` (Stapel bis 40 Dateien) |
| Dateien zurückspielen | `adb shell mkdir -p …`, `adb push` |
| Apps ermitteln | `pm list packages -3 --show-versioncode`, `pm path <paket>` |
| Apps installieren | `adb install -r` bzw. `adb install-multiple -r` |
| Datenexporte | `adb shell content query --uri …`, `adb shell settings list …` |
| App-Daten (klassisch) | `adb backup` / `adb restore` |

Kennt die Geräte-Shell kein `stat` oder kein `find -exec … +`, fällt die Dateisuche automatisch auf
ein reines `find -type f` zurück; die Größen werden dann beim Kopieren ermittelt.

## Ablauf einer Sicherung

1. **Plan** (`BackupService.CreatePlanAsync`): Je gewählter Gruppe werden die Zielordner durchsucht.
   Ein Verzeichnis wird nur einmal gelesen (Cache), jede Datei genau einer Gruppe zugeordnet
   (Reihenfolge des Katalogs). Bei einem inkrementellen Lauf werden unveränderte Dateien als
   „überspringen“ markiert (Vergleich: Größe + Änderungszeit + lokal vorhanden).
2. **Stapel** (`BackupService.BuildBatches`): Aufeinanderfolgende Dateien desselben Geräteordners
   werden zu Stapeln zusammengefasst. Namen, die auf der Festplatte angepasst werden müssten
   (z. B. `Rechnung:2024.pdf`), werden einzeln geholt, damit der Zielname exakt stimmt.
3. **Übertragung**: `adb pull -a`, danach optional SHA-256; jeder Eintrag wandert ins Manifest.
   Fehler einzelner Dateien brechen den Lauf nicht ab, sondern landen als Hinweis im Ergebnis.
4. **Abschluss**: Manifest und Bericht schreiben, optional ZIP-Archiv (mit optionaler
   AES-256-GCM-Verschlüsselung), optional Aufbewahrungsregel anwenden.

Ein Abbruch ist an jeder Stelle möglich: Das Manifest wird auch dann geschrieben, sodass der Satz
gültig bleibt und ein späterer inkrementeller Lauf nahtlos weitermacht.

## Zustand in der Oberfläche

`AppSession` hält den gemeinsamen Zustand (Einstellungen, adb-Verbindung, Geräteliste,
Sicherungsverwaltung, Protokoll) und wird an alle Seiten-Ansichtsmodelle durchgereicht.
`DeviceWatcher` fragt die Geräteliste alle drei Sekunden ab und pausiert währenddessen
automatisch, solange eine Sicherung läuft.

Die MVVM-Basis (`ObservableObject`, `RelayCommand`, `AsyncRelayCommand`) ist bewusst selbst
geschrieben – das Projekt kommt damit ohne weitere Abhängigkeiten neben Avalonia aus.

## Datenformate

* `manifest.json` – Schemaversion, Gerät, Kategorien, alle Einträge (Gerätepfad, lokaler Pfad,
  Größe, Zeitstempel, SHA-256, Paketname) und die Historie aller Läufe.
* `bericht.txt` – dieselben Informationen in Kurzform zum Nachlesen.
* `*.zip` / `*.zip.pbenc` – optionales Archiv; das verschlüsselte Format besteht aus
  `PBK1` + Salt + Blöcken aus `Länge | Nonce | Chiffrat | Tag`.
