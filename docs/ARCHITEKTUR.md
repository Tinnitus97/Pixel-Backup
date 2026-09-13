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
| Root erkennen | `adb shell id`, `adb shell su -c id` |
| App-Daten mit Root | `adb exec-out su -c 'tar -c -C /data/data <paket>'` |
| App-Daten zurück (Root) | `adb push` ▸ `tar -x -C /data/data` ▸ `chown` ▸ `restorecon` |

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

## Umzugshilfen

Nach jedem Lauf erzeugt `ImportFileBuilder` aus den Rohdaten der Content-Provider Dateien, die
ein neues Telefon direkt versteht: `kontakte.vcf` (vCard 3.0, Telefon- und E-Mail-Datensätze
werden über `contact_id` zusammengeführt), `kalender.ics`, sowie `sms.xml` und `anrufliste.xml`
im Format der App „SMS Backup & Restore“. Diese Dateien landen als eigene Manifest-Einträge
(`ImportFile`) im Satz und werden beim Wiederherstellen nach `/sdcard/PixelBackup-Import`
geschoben.

Die Erzeugung ist von der Sicherung entkoppelt: `ImportFileBuilder.BuildAsync` arbeitet allein
auf einem vorhandenen Satz und lässt sich daher auch nachträglich auf ältere Sätze anwenden.

## Root-Betrieb

`AdbClient.DetectRootAsync` prüft zuerst, ob adbd selbst als root läuft (userdebug-Abbilder),
danach `su` (Magisk). Der ermittelte Modus steckt in `DeviceInfo.RootAccess` und im `BackupPlan`.
Die Sicherung schreibt den tar-Strom über `adb exec-out` binärsicher in eine Datei – deshalb gibt
es neben `ProcessRunner.RunAsync` die Variante `RunToFileAsync`, die die Standardausgabe roh in
einen `FileStream` kopiert, statt sie als Text einzulesen.

Beim Zurückspielen wird die App zuerst installiert, dann gestoppt (`am force-stop`), das Archiv
nach `/data/local/tmp` geschoben, nach `/data/data` entpackt und anschließend der Besitzer
(numerische UID aus `dumpsys package`) sowie der SELinux-Kontext (`restorecon -R`) richtiggestellt.

## Sprache und Erscheinungsbild

Die Sprachverwaltung liegt in `PixelBackup.Core.Localization` und folgt demselben Muster wie
OfficeInstall: `Loc.Tr("deutsch", "english")` liefert den Text der aktuellen Sprache, ein
Wechsel meldet sich über `Localizer.I.LanguageChanged`. Die Ansichtsmodelle stellen ihre
Beschriftungen als Eigenschaften bereit (`LabelStart`, `SectionPaths` …) und melden nach einem
Wechsel alle Eigenschaften als geändert – dadurch wirkt die Umschaltung sofort, ohne Neustart.
Kategorien tragen ihre Texte zweisprachig im Katalog (`NameDe`/`NameEn`).

Die Startsprache kommt aus `CultureInfo.CurrentUICulture` (Deutsch bleibt Deutsch, alles andere
wird Englisch); zusätzlich stellt `Localizer.ApplyCulture()` Zahlen- und Datumsformate um.

Das Erscheinungsbild steuert `ThemeService`: „Wie das System“ setzt `ThemeVariant.Default`,
womit Avalonia den Hell-/Dunkelmodus des Betriebssystems übernimmt; `ActualThemeVariant` sagt,
was daraus geworden ist. Die Farben liegen als `ThemeDictionaries` in `App.axaml`.

## Komponenten (adb und USB-Treiber)

`SdkRepositoryClient` liest Googles offizielle Paketlisten – `repository2-3.xml` für die
Plattform-Tools, `addon2-3.xml` für den USB-Treiber –, wählt das Archiv passend zum
Betriebssystem, lädt es mit Fortschrittsmeldung, prüft die SHA-1-Prüfsumme und entpackt es.

`PlatformToolsInstaller` vergleicht die Fassung aus `adb version` mit der angebotenen Revision
und installiert nach `%LOCALAPPDATA%\PixelBackup\platform-tools`. Vor einem Austausch wird der
adb-Server beendet, sonst blockiert Windows die Dateien.

`UsbDriverService` fragt Windows in einem einzigen PowerShell-Aufruf ab: Treiberspeicher
(`pnputil /enum-drivers`), Geräte mit Problemkennung (`Win32_PnPEntity`) und den gebundenen
Treiber (`Win32_PnPSignedDriver`). Die Installation läuft über `pnputil /add-driver … /install`
mit Rückfrage der Benutzerkontensteuerung. Auf Linux und macOS meldet der Dienst „nicht
erforderlich“ samt Hinweis auf die udev-Regeln.

## Stand der Prüfung

Der Quellstand ist mit dem .NET-10-SDK gebaut (`dotnet build -c Release`, ohne Warnungen),
`dotnet test` meldet 63 bestandene Tests, und die Anwendung wurde unter X11 gestartet und
durchgeklickt. Dabei sind drei Fehler aufgefallen und behoben worden:

* `PlatformToolsInstaller.ParseVersion` las die Protokollfassung „1.0.41“ statt der
  Werkzeugfassung – dadurch hätte die Aktualisierungsprüfung immer ein Update gemeldet.
* Nach einem Sprachwechsel blieben zwei gemerkte Sätze (Plan-Zusammenfassung, adb-Statuszeile)
  in der alten Sprache stehen; beide werden jetzt neu gebildet.
* Ein Anheben von `Tmds.DBus.Protocol` auf 0.94.2 (gegen eine Sicherheitsmeldung) brach den
  Start unter Linux, weil Avalonia 11.2.3 die ältere Schnittstelle erwartet. Gelöst über
  Avalonia 11.3.22, das die geprüfte Fassung selbst mitbringt.

## Datenformate

* `manifest.json` – Schemaversion, Gerät, Kategorien, alle Einträge (Gerätepfad, lokaler Pfad,
  Größe, Zeitstempel, SHA-256, Paketname) und die Historie aller Läufe.
* `bericht.txt` – dieselben Informationen in Kurzform zum Nachlesen.
* `*.zip` / `*.zip.pbenc` – optionales Archiv; das verschlüsselte Format besteht aus
  `PBK1` + Salt + Blöcken aus `Länge | Nonce | Chiffrat | Tag`.
