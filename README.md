# Pixel Backup

Eine Windows-Anwendung (C# / .NET 8 / Avalonia) zum vollständigen Sichern und Wiederherstellen
von **Android-Geräten** – ähnlich wie Samsung Smart Switch, aber herstellerunabhängig und ohne
zusätzliche App auf dem Telefon.

Die gesamte Gerätekommunikation läuft nativ über **adb** (Android Debug Bridge). Damit funktioniert
das Programm mit jedem Android-Gerät, das USB-Debugging beherrscht – Google Pixel, Samsung, Xiaomi,
OnePlus, Motorola, Sony, Fairphone und andere.

**Windows ist das Hauptsystem**, für das die Anwendung gedacht ist. **Linux wird gleichwertig
unterstützt** – mit Startmenü-Eintrag, Installationsskript und den Geräteregeln (udev) direkt aus
der Anwendung heraus.

> Die Sicherungen sind **keine undurchsichtigen Container**: Fotos bleiben Fotos, Dokumente bleiben
> Dokumente. Jeder Sicherungssatz lässt sich im Explorer öffnen und einzeln weiterverwenden.

> **Kurz zu Android 13+ (auch 16/17):** Dateien, Apps, Kontakte, SMS, Anrufliste und Kalender lassen
> sich vollständig sichern und auf ein neues Telefon bringen. Die **Daten installierter Apps** kann
> ohne Root kein PC-Werkzeug mehr auslesen – das verbietet Android seit Version 12.
> Details und den empfohlenen Umzugsweg beschreibt der Abschnitt
> [„Was auf Android 13–17 wirklich geht“](#was-auf-android-13-14-15-16-und-17-wirklich-geht).

---

## Oberfläche

### Echte Aufnahmen des laufenden Programms

Gebaut mit dem .NET-10-SDK, gestartet unter X11 (Ubuntu 24.04); im Testlauf war **kein Gerät
angeschlossen** und es lagen **keine Sicherungen** vor – deshalb sind die Listen leer. Unter Windows
sieht die Anwendung genauso aus, nur mit den Windows-eigenen Bedienelementen.

**Gerät** – ohne angeschlossenes Telefon; unten rechts meldet die Statuszeile den fehlenden adb-Stand

![Ansicht „Gerät“](docs/screenshots/live/01-geraet.png)

**Sichern** – die Gruppenliste mit Beschreibungen und Hinweisen

![Ansicht „Sichern“](docs/screenshots/live/02-sichern.png)

**Wiederherstellen** – Satzliste, Konfliktstrategie und Optionen

![Ansicht „Wiederherstellen“](docs/screenshots/live/03-wiederherstellen.png)

**Einstellungen** – Sprache und Erscheinungsbild mit dem, was am System erkannt wurde

![Ansicht „Einstellungen“](docs/screenshots/live/04-einstellungen.png)

**Komponenten, dunkles Erscheinungsbild** – adb fehlt und kann installiert werden; unter Linux
wird kein USB-Treiber benötigt

![Ansicht „Komponenten“ im dunklen Erscheinungsbild](docs/screenshots/live/05-komponenten-dunkel.png)

**English** – dasselbe Programm auf einem System mit englischer Anzeigesprache (keine Einstellung
nötig, die Sprache wird erkannt)

![Components view in English](docs/screenshots/live/06-components-english.png)

### Mit angeschlossenem Gerät

Diese Bilder sind **maßstabsgetreue Layout-Darstellungen** aus dem XAML (kein Testlauf), weil hier
kein Android-Gerät und keine Sicherungen zur Verfügung standen:

**Sichern mit laufender Sicherung** – Fortschritt, Geschwindigkeit und Restzeit

![Sichern während einer laufenden Sicherung](docs/screenshots/02-sichern-laeuft.png)

**Gruppen** – auf Android 13+ werden nicht mögliche Gruppen gesperrt und begründet

![Gruppenauswahl mit gesperrten Gruppen](docs/screenshots/03-gruppen.png)

**Wiederherstellen** (dunkel) – mit Sicherungssätzen und Gruppen

![Wiederherstellen mit Daten](docs/screenshots/04-wiederherstellen-dunkel.png)

**Sicherungen** – Inhalt und Laufhistorie eines Satzes

![Sicherungen mit Daten](docs/screenshots/05-sicherungen.png)

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
| 🎮 App-Ordner im Speicher | `Android/data`, `Android/obb` – Spielstände, soweit das Gerät den Zugriff erlaubt |
| 📦 Apps (APK) | alle selbst installierten Apps inkl. Split-APKs |
| 🔐 App-Daten vollständig | `/data/data` je App als tar-Archiv – **nur mit Root** |
| 🗄 App-Daten (klassisch) | `adb backup` – nur für Geräte bis Android 11 sinnvoll |
| 👤 Kontakte | Export über den Content-Provider (Rohdaten + CSV) |
| ✉ SMS & MMS | Export über den Content-Provider (Rohdaten + CSV) |
| 📞 Anrufliste | Export über den Content-Provider (Rohdaten + CSV) |
| 📅 Kalender | Termine aus dem Gerätekalender (Rohdaten + CSV) |
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

### Umzug auf ein neues Telefon

Aus den Datenexporten erzeugt Pixel Backup automatisch Dateien, die sich auf **jedem** neuen
Android-Telefon einspielen lassen:

| Datei | Inhalt | Import auf dem neuen Telefon |
| --- | --- | --- |
| `data/kontakte.vcf` | Namen, Rufnummern, E-Mail-Adressen | Kontakte-App ▸ Importieren |
| `data/kalender.ics` | Termine | Kalender-App bzw. calendar.google.com ▸ Importieren |
| `data/sms.xml` | SMS/MMS | App „SMS Backup & Restore“ ▸ Wiederherstellen |
| `data/anrufliste.xml` | Anrufliste | App „SMS Backup & Restore“ ▸ Wiederherstellen |

Beim Wiederherstellen legt Pixel Backup diese Dateien unter `/sdcard/PixelBackup-Import` auf dem
Zielgerät ab. Jeder Sicherungssatz enthält zusätzlich `umzug-anleitung.txt` mit den Schritten für
genau diesen Satz.

### Sprache, Erscheinungsbild und Komponenten

* **Deutsch und Englisch** – die Oberfläche startet in der **Anzeigesprache des Systems**
  (Deutsch bei einem deutschen System, sonst Englisch) und lässt sich jederzeit umschalten;
  die Umschaltung wirkt sofort, ohne Neustart.
* **Hell und dunkel** – Voreinstellung ist „Wie das System“: Pixel Backup übernimmt den
  Hell-/Dunkelmodus des Betriebssystems und zeigt an, was erkannt wurde. Feste Wahl ist möglich.
* **Komponenten-Seite** – prüft die Android-Plattform-Tools (adb) und unter Windows den
  Google-USB-Treiber:
  * fehlt adb, wird es **beim Start automatisch** aus Googles offizieller Paketliste geladen
    (SHA-1-geprüft) und eingerichtet;
  * gibt es eine neuere Fassung, erscheint ein **Update-Angebot** (kein stiller Austausch);
  * meldet Windows ein Problemgerät oder fehlt der USB-Treiber, lässt er sich von hier aus
    laden und über `pnputil` installieren.

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

## Betriebssysteme

| System | Stand |
| --- | --- |
| **Windows 10 / 11** (x64, ARM64) | **Hauptsystem.** USB-Treiberprüfung und -einrichtung über `pnputil`, eigenständige EXE über das Veröffentlichungsskript. |
| **Linux** – Debian, Ubuntu, Mint, Arch, Manjaro, Fedora, openSUSE, Alpine … | **Voll unterstützt.** Gleicher Funktionsumfang, Startmenü-Eintrag, Erkennung der Verteilung, Geräteregeln (udev) aus der Anwendung heraus. |
| macOS (x64, Apple Silicon) | Läuft grundsätzlich (Avalonia und adb sind dort zu Hause), wird aber nicht regelmäßig geprüft. Ein Treiber wird nicht benötigt. |

Der Unterschied zwischen den Systemen betrifft nur den Zugang zum Gerät:

* **Windows** braucht bei manchen Geräten den **Google-USB-Treiber**.
* **Linux** braucht **udev-Regeln**, sonst meldet adb `no permissions`. Pixel Backup erkennt das,
  schreibt die Regeln auf Wunsch nach `/etc/udev/rules.d/51-android.rules`, legt die Gruppe
  `plugdev` an und lädt die Regeln neu.
* **macOS** braucht nichts davon.

## Voraussetzungen

* **Windows 10/11** (Hauptsystem) oder **Linux**; macOS läuft ebenfalls.
* [.NET 10 SDK](https://dotnet.microsoft.com/download) zum Bauen bzw. die .NET 10 Desktop Runtime
  zum Ausführen (dieselbe Grundlage wie OfficeInstall). Wer die eigenständigen Fassungen aus
  `packaging/` verwendet, braucht auf dem Zielrechner gar kein .NET.
* **Android-Plattform-Tools (`adb`) – müssen nicht von Hand installiert werden.**
  Pixel Backup sucht `adb` in PATH, im Android-SDK, neben der Anwendung und an den üblichen
  Orten; findet es nichts, lädt es die Plattform-Tools beim Start selbst herunter:
  * Windows: `%LOCALAPPDATA%\PixelBackup\platform-tools`
  * Linux/macOS: `~/.local/share/PixelBackup/platform-tools`

  Unter Linux geht genauso gut das Paket der Verteilung (`android-sdk-platform-tools`,
  `android-tools`); der Pfad lässt sich in den Einstellungen jederzeit überschreiben.
* **Gerätezugang** – unter Windows bei Bedarf der Google-USB-Treiber, unter Linux die udev-Regeln.
  Die Komponenten-Seite erkennt beides und richtet es ein.
* Am Gerät: **Entwickleroptionen** aktivieren (siebenmal auf die Build-Nummer tippen) und
  **USB-Debugging** einschalten. Beim ersten Anschließen die Abfrage am Telefon bestätigen.

---

## Bauen und starten

```bash
dotnet restore
dotnet build -c Release
dotnet run --project src/PixelBackup.App
```

Die Oberfläche baut auf Avalonia 11.3.22 auf. Der Stand ist mit dem .NET-10-SDK gebaut, gestartet
und getestet: `dotnet build -c Release` läuft ohne Warnung durch, `dotnet test` meldet 63 grüne
Tests, und die Anwendung startet unter X11 (siehe Aufnahmen oben).

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

## Einrichten unter Linux

**1. .NET besorgen** (einmalig, je nach Verteilung):

```bash
sudo apt install dotnet-sdk-10.0        # Debian, Ubuntu, Mint
sudo pacman -S dotnet-sdk               # Arch, Manjaro
sudo dnf install dotnet-sdk-10.0        # Fedora
sudo zypper install dotnet-sdk-10.0     # openSUSE
```

**2. Pixel Backup einrichten:**

```bash
git clone https://github.com/Tinnitus97/Pixel-Backup.git
cd Pixel-Backup
./packaging/linux/install.sh            # nur für den angemeldeten Benutzer
./packaging/linux/install.sh --system   # systemweit nach /opt
./packaging/linux/install.sh --udev     # zusätzlich die Geräteregeln einrichten
```

Das Skript baut eine eigenständige Fassung (kein .NET auf dem Zielrechner nötig), legt den Starter
`pixel-backup` an und trägt die Anwendung mit Symbol ins Startmenü ein. Entfernen:
`./packaging/linux/uninstall.sh`.

**3. Geräteregeln** – nötig, damit adb ohne Root-Rechte auf das Telefon darf. Entweder in der
Anwendung unter **Komponenten ▸ Geräteregeln einrichten** oder von Hand:

```bash
./packaging/linux/install-udev-rules.sh   # schreibt 51-android.rules, legt plugdev an, lädt neu
```

Danach das Gerät einmal ab- und wieder anstecken. Erscheint es weiterhin als „kein Zugriff“, einmal
ab- und wieder anmelden (die neue Gruppenzugehörigkeit greift erst dann). Wer lieber die Pakete der
Verteilung nimmt: `android-sdk-platform-tools-common` (Debian/Ubuntu) oder `android-udev` (Arch).

## Einrichten unter Windows

Entweder das Projekt mit dem .NET-10-SDK bauen und starten, oder eine eigenständige EXE erzeugen:

```powershell
powershell -ExecutionPolicy Bypass -File .\packaging\windows\publish.ps1
```

Ergebnis ist `publish\PixelBackup.exe` – eine einzelne Datei ohne vorinstalliertes .NET. adb lädt
die Anwendung beim ersten Start selbst nach; den USB-Treiber richtet sie auf der Komponenten-Seite ein.

## Bedienung in Kürze

1. **Gerät** – Telefon anschließen, Status prüfen, gegebenenfalls adb-Server neu starten.
2. **Sichern** – Gruppen wählen, auf *Analysieren* tippen (zeigt Umfang), dann *Sicherung starten*.
3. **Sicherungen** – Sätze ansehen, prüfen, archivieren, löschen, Ordner öffnen.
4. **Wiederherstellen** – Satz und Gruppen wählen, Konfliktstrategie festlegen, starten.
5. **Komponenten** – prüft adb und den USB-Treiber, installiert und aktualisiert beides.
6. **Einstellungen** – Sprache, Erscheinungsbild, Zielordner, adb-Pfad, Aufbewahrung, Automatik.

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
        ├── appdata-root/<paket>.tar   vollständige App-Daten (nur mit Root)
        ├── data/kontakte-….txt|.csv   Rohdaten der Exporte
        ├── data/kontakte.vcf          Importdateien für das neue Telefon
        ├── data/sms.xml · kalender.ics
        └── umzug-anleitung.txt        Schritte für den Wechsel auf ein neues Telefon
```

Ein inkrementeller Lauf aktualisiert den jüngsten Satz des Gerätes und hängt einen weiteren Eintrag
an die Laufhistorie im Manifest an.

---

## Was auf Android 13, 14, 15, 16 und 17 wirklich geht

Android hat den Zugriff von außen über die Jahre stark eingeschränkt. Diese Tabelle sagt, was ein
PC-Werkzeug per adb heute noch leisten kann – und was nicht:

| Inhalt | Ohne Root | Mit Root |
| --- | --- | --- |
| Fotos, Videos, Musik, Dokumente, Downloads | ✅ vollständig | ✅ |
| Messenger-**Medien** (`Android/media`, z. B. WhatsApp-Bilder) | ✅ vollständig | ✅ |
| Apps als APK (inkl. Split-APKs) | ✅ | ✅ |
| Kontakte, SMS, Anrufliste, Kalender | ✅ als Export **und** als Importdatei (vcf/ics/xml) | ✅ |
| Systemeinstellungen | ✅ als Dokumentation (kein Rückschreiben) | ✅ |
| `Android/data`, `Android/obb` (Spielstände) | ⚠️ geräteabhängig, ab Android 11 oft gesperrt | ✅ |
| **App-Daten** (Chatverläufe, Logins, Spielstände) | ❌ systembedingt nicht möglich | ✅ vollständig (`/data/data` als tar) |

**Warum App-Daten ohne Root nicht gehen:** Der alte Weg `adb backup` wurde ab Android 12 von fast
allen Apps ignoriert und ist ab Android 13 praktisch abgeschaltet – unabhängig vom PC-Programm.
Auch Smart Switch und ähnliche Werkzeuge holen App-Daten deshalb nicht über den PC, sondern über
die geräteeigene Übertragung. Pixel Backup blendet die Gruppe auf solchen Geräten aus und sagt
statt dessen klar, welcher Weg funktioniert.

### Empfohlener Weg für einen vollständigen Umzug

1. **Pixel Backup** auf dem alten Telefon: alle Dateigruppen, Apps (APK) sowie Kontakte, SMS,
   Anrufliste und Kalender sichern. Das ist der Teil, den man dauerhaft und prüfbar auf dem PC
   liegen haben will.
2. **Neues Telefon einrichten** und dabei die Android-Übertragung (Kabel oder WLAN) verwenden –
   nur dieser Weg bringt App-Daten mit, weil er auf dem Gerät selbst läuft.
3. **Pixel Backup ▸ Wiederherstellen** auf das neue Telefon: fehlende Dateien zurückspielen, Apps
   nachinstallieren, Importdateien ablegen und importieren.
4. Für einzelne Apps zusätzlich deren eigene Sicherung nutzen (WhatsApp ▸ Google Drive und ähnlich).

Mit gerootetem Gerät entfällt Schritt 2: Dann sichert und restauriert Pixel Backup mit der Gruppe
**„App-Daten vollständig (Root)“** auch `/data/data` je App (tar über `adb exec-out`, beim
Zurückspielen inklusive Besitzer- und SELinux-Korrektur).

## Weitere Grenzen

* **Systemeinstellungen** werden dokumentiert, aber nicht automatisch zurückgeschrieben –
  einzelne Werte zurückzuschreiben kann ein Gerät unbrauchbar machen.
* **Kontakte, SMS, Anrufliste, Kalender** werden über `content query` gelesen. Erlaubt ein Gerät
  das nicht, meldet das Protokoll dies deutlich; alle übrigen Gruppen laufen normal weiter.
* **Sehr lange Pfade**: Unter Windows sollte die Unterstützung langer Pfade aktiviert sein, wenn
  tief verschachtelte Ordner gesichert werden.
* **Root-Sicherungen** enthalten auch die Cache-Ordner der Apps und sind entsprechend groß.

## Projektstruktur

```
src/PixelBackup.Core/   Fachlogik ohne UI: adb-Hülle, Kategorien, Sicherung, Wiederherstellung,
                        Prüfung, Archivierung, Einstellungen, Sprachverwaltung,
                        Komponenten (Plattform-Tools, USB-Treiber)
src/PixelBackup.App/    Avalonia-Oberfläche (MVVM, ohne zusätzliche MVVM-Abhängigkeit)
tests/PixelBackup.Tests/ xUnit-Tests für Pfadabbildung, Parser, Manifest, Stapelbildung, Krypto,
                        Sprachumschaltung, Paketlisten und die Linux-Geräteregeln
packaging/linux/        Installationsskripte, Geräteregeln, Startmenü-Eintrag, Symbole
packaging/windows/      Veröffentlichungsskript für die eigenständige EXE
.github/workflows/      Bau und Tests unter Windows und Linux
```

Details zur Architektur: [docs/ARCHITEKTUR.md](docs/ARCHITEKTUR.md).
