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

## Unterschiede zwischen den Betriebssystemen

Der Kern ist plattformneutral; nur der Zugang zum Gerät unterscheidet sich. `ComponentService`
wählt dafür zur Laufzeit den passenden Dienst:

| | Windows (Hauptsystem) | Linux | macOS |
| --- | --- | --- | --- |
| Zugangskomponente | `UsbDriverService` (Google-USB-Treiber) | `LinuxDeviceAccessService` (udev-Regeln) | – |
| Prüfung | PowerShell: Treiberspeicher, gebundener Treiber, Geräte mit Problemkennung | Regeldateien in `/etc/udev/rules.d` und `/usr/lib/udev/rules.d`, Gruppenzugehörigkeit, `adb devices` | entfällt |
| Einrichtung | `pnputil /add-driver … /install` mit Rückfrage der Benutzerkontensteuerung | Skript über `pkexec` bzw. `sudo`: Regeln schreiben, Gruppe `plugdev`, `udevadm` neu laden | entfällt |
| adb | wird bei Bedarf von Google geladen | ebenso; alternativ das Paket der Verteilung | ebenso |

`LinuxEnvironment` liest `/etc/os-release`, erkennt daraus die Paketverwaltung (apt, pacman, dnf,
zypper, apk) und nennt in der Oberfläche den passenden Befehl. Meldet `adb devices` ein Gerät mit
`no permissions`, wird daraus der Gerätezustand `NoPermissions` – die Geräteseite blendet dann einen
Hinweis ein, die Komponenten-Seite meldet ein Problem und bietet die Regeln an.

### Grafische Sitzung: X11 und Wayland

Avalonia zeichnet unter Linux über **X11**; in einer **Wayland-Sitzung** übernimmt **XWayland**.
`LinuxEnvironment.DetectSession` wertet dafür `XDG_SESSION_TYPE`, `WAYLAND_DISPLAY` und `DISPLAY`
aus – bewusst als reine Funktion, damit sie ohne Umgebung prüfbar bleibt (Wayland hat Vorrang,
denn dort ist `DISPLAY` nur die Adresse von XWayland). Die Sitzungsart steht auf der Seite
„Komponenten“ und in der ersten Protokollzeile.

`Program.Main` fragt vor dem Start von Avalonia `LinuxEnvironment.DisplayProblem` ab. Fehlt eine
nutzbare Anzeige – Wayland ohne XWayland oder gar keine Sitzung –, gibt es statt eines
Stapelauszugs einen erklärenden Satz samt Installationsbefehl der erkannten Verteilung und den
Rückgabewert 78 (`EX_CONFIG`). Der `AppBuilder` setzt `X11PlatformOptions` mit
`UseDBusFilePicker`, `UseDBusMenu`, `EnableIme` und `EnableMultiTouch`: Dateiauswahl und Menüs
laufen damit über die Portale des Desktops und verhalten sich unter Wayland wie dort erwartet.
Der Startmenü-Eintrag trägt `StartupWMClass=PixelBackup` passend zur `WM_CLASS` des Fensters.

Avalonia lädt den X11-Satz (`libX11`, `libXext`, `libXi`, `libXrandr`, `libXcursor`, `libXfixes`,
`libICE`, `libSM`, dazu wahlweise `libGL`) erst zur Laufzeit per `dlopen`. Ein Paketbauwerkzeug
sieht diese Abhängigkeiten deshalb nicht im ELF-Kopf; `.deb`, `.rpm` und PKGBUILD nennen sie
ausdrücklich (im rpm als Soname-Abhängigkeiten, weil die Paketnamen je nach Verteilung anders
lauten). Fehlt zur Laufzeit doch eine, ersetzt `LinuxEnvironment.DescribeMissingLibrary` die
seitenlange Suchliste von .NET durch einen Satz mit Bibliotheksname und Installationsbefehl der
erkannten Verteilung.

## Bereitstellung

Beide Systeme bekommen dieselbe Bauweise: eine eigenständige Einzeldatei
(`PublishSingleFile` samt `IncludeNativeLibrariesForSelfExtract`, damit auch SkiaSharp und
HarfBuzz mit in die Datei wandern). Unter Windows ist das `PixelBackup.exe`, unter Linux die
endungslose ELF-Datei `PixelBackup` (46 MiB). Beim ersten Start entpackt die .NET-Laufzeit die
eingebetteten nativen Bibliotheken in einen Zwischenspeicher des Benutzers.

`EnableCompressionInSingleFile` komprimiert die eingebettete Laufzeit: 91 → 46 MiB unter Linux,
96 → 46 MiB unter Windows. Bezahlt wird das mit rund einer Zehntelsekunde beim allerersten Start
(gemessen 0,97 s statt 0,84 s bis zum Fenster, danach identisch), weil die Laufzeit einmalig in den
Zwischenspeicher des Benutzers entpackt.

`packaging/linux/package.sh` baut daraus fünf Verteilwege:

| Format | Werkzeug | Inhalt |
| --- | --- | --- |
| `.deb` | `dpkg-deb` | Dateien unter `/usr/lib/pixel-backup`, Starter in `/usr/bin`, Startmenü-Eintrag und Symbole; `postinst` frischt Desktop- und Symbolzwischenspeicher auf |
| `.rpm` | `rpmbuild` | derselbe `/usr`-Baum; Nachbearbeitung und Debug-Paket sind abgeschaltet, damit die fertige Einzeldatei unverändert bleibt |
| `.AppImage` | `mksquashfs` | Typ-2-Abbild aus AppImage-Laufzeit und zstd-Dateisystem, läuft ohne Installation; enthält bewusst die **unkomprimierte** Einzeldatei, weil das Abbild selbst schon komprimiert ist (sonst 39 statt 34 MB) |
| `.flatpak` | `flatpak-builder` | Bündel auf `org.freedesktop.Platform//24.08`, Freigaben für X11/Wayland, USB-Geräte und Benutzerordner; bewusst nur PNG-Symbole, weil `appstreamcli` am Ende des Baus ein SVG ohne librsvg-Lader nicht lesen kann |
| `.tar.gz` | `tar` | portables Archiv mit kleinem Einrichtungsskript für den angemeldeten Benutzer |

`packaging/flatpak/check-metainfo.sh` macht in zwei Sekunden dieselbe Prüfung
(`appstreamcli validate` und `compose`), mit der `flatpak-builder` einen Bau erst nach Minuten
abbrechen würde; beide Abläufe in `.github/workflows/` rufen sie vor dem Paketbau auf.

`--formats` wählt einzelne Formate aus, `--require-all` bricht ab, sobald eines fehlschlägt (so
läuft es in der CI). Ohne diese Option wird ein Format, dessen Werkzeug fehlt, übersprungen und am
Ende benannt. Für Arch liegt zusätzlich ein PKGBUILD bereit, das aus dem Git-Stand baut und dabei
auch die Tests laufen lässt.

## Versionscheck und Updater

Drei Bausteine, alle im Kern und damit ohne Oberfläche prüfbar:

* **`InstallationInfo`** beantwortet zwei Fragen: welche Fassung läuft (aus
  `AssemblyInformationalVersion`) und wie sie eingespielt wurde. Die Entscheidung steckt in der
  reinen Funktion `Detect(executablePath, APPIMAGE, FLATPAK_ID, /.flatpak-info, isWindows,
  isLinux, packageOwner)`; erst der Rückruf `packageOwner` fragt tatsächlich `dpkg -S` und
  `rpm -qf`. `ArchOf` übersetzt die Architektur in die Schreibweise des jeweiligen Formats
  (amd64, x86_64, x64 …).
* **`UpdateService`** holt `update.json` (jüngste Veröffentlichung, Rückfallweg: Standardzweig),
  liest sie, vergleicht die Nummern über `System.Version` und wählt mit `SelectPackage` die Datei,
  die zu Einbauart und Architektur passt. Der Download läuft mit Fortschrittsmeldung und
  SHA-256-Vergleich; eine Datei, deren Summe nicht stimmt, wird gelöscht.
* **`UpdateInstaller`** spielt ein. EXE, AppImage und portable Fassung werden ausgetauscht – eine
  laufende Datei kann sich nicht selbst überschreiben, deshalb schreibt der Installer ein kleines
  Skript (`.cmd` bzw. `.sh`), das auf das Ende des Vorgangs wartet, die Datei ersetzt und neu
  startet; die Anwendung beendet sich dafür über das Ereignis `ExitRequested`. Für `.deb`, `.rpm`
  und Flatpak baut `BuildPackageCommand` den Aufruf der Paketverwaltung (`pkexec apt-get`,
  `pkexec dnf`, `zypper`, `flatpak install --user --bundle`; im Flatpak-Behälter davor
  `flatpak-spawn --host`).

Aus dem portablen Archiv wird ausschließlich der Eintrag `PixelBackup` übernommen
(`IsProgramEntry`) – ein manipuliertes Archiv kann so nichts anderes ablegen.

In der Oberfläche ist das Ganze der dritte Eintrag auf der Seite „Komponenten“ und nutzt dieselbe
`ComponentStatus`-Darstellung wie adb und die Geräteregeln.

`.github/workflows/release.yml` erzeugt die Gegenseite: Es baut auf einem Windows- und einem
Linux-Läufer alle sieben Dateien, bildet `SHA256SUMS`, erzeugt mit
`packaging/make-update-manifest.sh` die Datei `update.json` und hängt alles an die
Veröffentlichung.

## Stand der Prüfung

Der Quellstand ist mit dem .NET-10-SDK gebaut (`dotnet build -c Release`, ohne Warnungen),
`dotnet test` meldet 154 bestandene Tests, und die Anwendung wurde unter Linux (Ubuntu 24.04) sowohl
unter **X11** als auch in einer echten **Wayland-Sitzung** (Weston 13 mit Xwayland 23.2.6)
gestartet und durchgeklickt – inklusive Sprach- und Themenwechsel, Einrichtung der udev-Regeln und
Erkennung eines echten adb (34.0.4). Auch die Bereitstellung ist erprobt: `.deb` gebaut, mit
`dpkg -i` eingespielt, über `/usr/bin/pixel-backup` gestartet und wieder entfernt; `.rpm`,
`.AppImage` (entpackt und über `AppRun` gestartet) und `.tar.gz` sind ebenfalls gebaut. Das
Flatpak-Bündel ließ sich hier nicht erzeugen, weil die Laufzeit von Flathub in dieser Umgebung
nicht erreichbar ist – die Bauanleitung wurde stattdessen mit `flatpak-builder --show-manifest`
(1.4.2) geprüft, und die CI baut das Bündel mit `--require-all`.

Auch der Updater ist echt gelaufen: über einen lokalen Webserver wurde eine Fassung 0.9.1
angeboten, die Anwendung hat sie erkannt („Fassung 0.9.1 ist verfügbar (installiert: 0.9.0)“),
nach Rückfrage geladen, die Prüfsumme verglichen, die Programmdatei aus dem Archiv geholt,
sich beendet, die Datei ausgetauscht (neue Inode-Nummer) und sich neu gestartet. Dabei sind drei Fehler aufgefallen und behoben worden:

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
