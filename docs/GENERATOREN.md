# Generatoren

Beschreibt, wie aus der Konfiguration einer Softwareversion die Dateien eines Builds entstehen (Spec §9, §11). Das Dokument wächst mit den Scheiben von M3 (M3a Snapshot und Intune-Werte, M3b Erkennungsskript, M3c HTML-Anleitung, M3d Wrapper-Konfiguration) und wird im selben PR wie der Code gepflegt.

## Grundsatz: alles aus dem Snapshot

Jede erzeugte Datei entsteht aus einem **`BuildSnapshot`**, nie aus dem Zustand der Oberfläche. Der Snapshot hält fest, was die Ausgabe eines Builds bestimmt (Format: `docs/DATENFORMAT.md`, Abschnitt `configuration.snapshot.json`):

- eine **private Kopie** der Konfiguration (spätere Änderungen an der Version ändern den Snapshot nicht),
- die Build-ID, die Sprache der erzeugten Texte (`de` oder `en`, beim Build festgelegt), die Version des Tools und der Fingerabdruck der Quelle.

Die **Build-ID** (`yyyyMMdd-HHmmss-xxxx`, UTC plus vier zufällige Hexzeichen) bezeichnet genau einen Paketbau und taugt als Ordnername. Sie ist **nicht** die Softwareversion: Eine neue Build-ID ist allein kein Grund, dieselbe Software erneut installieren zu lassen (Spec §6.4). Der **Fingerabdruck der Quelle** ist ein SHA-256 über Pfad, Größe und Prüfsumme jeder Quelldatei, sortiert nach Pfad und ohne Zeitstempel; dieselbe Quelle ergibt immer denselben Wert.

## `IntuneSettings`: eine Quelle für alle Intune-Werte

`IntuneSettingsBuilder.From(snapshot)` erzeugt **alle** Werte, die ein Administrator für diesen Build in Intune einträgt. Anleitung, JSON, CSV und Ergebnisseite werden aus diesem einen Objekt gerendert und können deshalb nicht auseinanderlaufen. Eine globale Vorlage gibt es nicht; die Kopierfunktion der Ergebnisseite übernimmt die Werte des gewählten Builds.

Eine Konfiguration, die der Validator beanstandet (zum Beispiel eine EXE ohne Herstellerwerte), wird **nie** in Einstellungen verwandelt: `InvalidConfigurationException` nennt die Probleme.

### Inhalt

| Bereich | Wert | Herkunft |
|---|---|---|
| `app` | `type` = `Win32`, `packageFileName` = `<projekt-id>.intunewin`, `name`, `publisher`, `version`, `description` | Konfiguration (Name, Hersteller, Zielversion); die Beschreibung ist `<Name> <Version>`, bis es ein eigenes Feld gibt |
| `program` | `installCommand` = `Install.cmd`, `uninstallCommand` = `Install.cmd -DeploymentType Uninstall`, `installContext` = `System`, `timeoutMinutes`, `restartBehavior` | feste Schnittstelle (`DeploymentInterface`), Zeitlimit aus der Konfiguration |
| `requirements` | `architecture` = `x64`, `minimumWindowsVersion` | siehe „Annahmen“ |
| `returnCodes` | Liste aus `code` und `type` (`Success`, `SoftReboot`, `HardReboot`, `Retry`), aufsteigend nach Code | Konfiguration, siehe unten |
| `detection` | `scriptFileName` = `Detect-App.ps1`, `runAs32Bit` = `false`, `enforceSignatureCheck` = `false`, `signatureStatus` = `Unsigned`, `rule` (Methode, ProductCode oder Pfad, Mindestversion) | Konfiguration; der Signaturstatus ist der **tatsächliche**: das Skript ist nicht signiert, solange es keinen Signierschritt gibt |
| `logs` | Zielgeräte-Ordner `C:\Windows\Logs\Intune\PackageDeploy\<projekt-id>`, `Deployment.log`, bei MSI `MsiInstall.log` und `MsiUninstall.log`, Intune-Logordner | feste Schnittstelle |
| `processesToClose` | Programme, die vor Installation und Deinstallation geschlossen sein müssen | Konfiguration (leere Einträge entfallen) |

Werte, die zu einem Pfad gehören, stehen genau einmal in `DeploymentInterface` (Eingangsskript, Befehle, Erkennungsskript, Logdateien, Dateinamen der erzeugten Dateien). Die Vorlagen der Client-Laufzeit (M5) müssen genau diese Dateien liefern; ein Test stellt das dort sicher.

### Rückgabecodes und Neustart

- Standard: `0` Erfolg, `3010` Erfolg mit Neustartbedarf (**Soft reboot**), `1618` erneuter Versuch (**Retry**). Weitere Herstellercodes aus der Konfiguration werden aufgenommen, doppelte Einträge entfallen.
- **`1641` wird immer als `HardReboot` aufgeführt.** Der Code bedeutet, dass der Installer den Neustart schon selbst ausgelöst hat; Intune soll ihn deshalb nie als unauffälligen Erfolg behandeln (Spec §8.4). Der Validator lehnt `1641` in jeder der drei Listen (Erfolg, Neustart, Wiederholung) ab: Ein solcher Herstellerinstaller braucht überarbeitete Neustartparameter und einen erneuten Pilot.
- Das Neustartverhalten ist **„Verhalten anhand der Rückgabecodes bestimmen“** (`DetermineBasedOnReturnCodes`): Der Wrapper löst nie einen erzwungenen Neustart aus.

### Annahmen (offene Entscheidungen, siehe `docs/PLANUNG.md` §8)

- **Mindest-Windows-Version:** Platzhalter `Windows 10 1607`. Die Anleitung kennzeichnet ihn ausdrücklich als Platzhalter, bis die Entscheidung gefallen ist; `IntuneSettingsOptions.MinimumWindowsVersion` überschreibt ihn.
- **Architektur:** Die Anforderung ist ein 64-Bit-Windows, weil der Wrapper im nativen 64-Bit-Prozess läuft. Ein x86-Herstellerprogramm läuft darauf weiterhin; die Architektur des Programms (`install.targetArchitecture`) ist davon getrennt.

## JSON und CSV

`SettingsWriter` rendert dieselben Werte in zwei Dateien (`Einstellungen.json`, `Einstellungen.csv` im Ordner `intune` des Builds):

- **JSON:** camelCase, Aufzählungen als Text, eingerückt, ohne `null`-Werte, UTF-8 ohne BOM. Anführungszeichen, Backslashes, Zeilenumbrüche, Unicode (auch Zeichen außerhalb der Grundebene) werden vom Serialisierer korrekt maskiert; datumsähnliche Texte bleiben unverändert.
- **CSV:** wird **aus dem JSON-Dokument** abgeleitet, eine Zeile je Wert mit Schlüssel wie `program.installCommand` oder `returnCodes[0].code`. Damit transportieren beide Dateien dieselben Werte (getestet). Kopfzeile `Key,Value`, Komma als Trenner, CRLF, UTF-8 **mit BOM** (Tabellenprogramme lesen Umlaute dann richtig).
- **CSV-Maskierung:** Zellen mit Komma, Anführungszeichen oder Zeilenumbruch werden nach RFC 4180 in Anführungszeichen gesetzt (`"` wird verdoppelt). Text, den ein Tabellenprogramm als Formel lesen könnte (erstes Zeichen `=`, `+`, `-`, `@`, Tabulator oder Wagenrücklauf), bekommt einen führenden Apostroph; einfache Zahlen bleiben unverändert. Dadurch können Namen oder Hersteller aus den Metadaten keine Formeln einschleusen (Abnahme A17). Der Apostroph ist die einzige Abweichung zwischen CSV und JSON.

Beide Dateien werden atomar geschrieben.

## Erkennungsskript (M3b)

`DetectionScriptGenerator` erzeugt das eigenständige Skript `Detect-App.ps1` aus den `IntuneSettings` des Builds (Erkennungsregel, Build-ID). Es prüft den **tatsächlichen Zustand** des Geräts und liest weder Dateien des Pakets noch des Intune-Cache; es schreibt keine Datei und keinen Marker (Spec §8.3).

### Verfahren

| Installertyp | Prüfung |
|---|---|
| MSI | Registrierung unter `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\<ProductCode>` in **beiden** Registry-Ansichten (64 und 32 Bit, unabhängig von der Bitness des Prozesses); erkannt, wenn `DisplayVersion` mindestens der konfigurierten Mindestversion entspricht |
| EXE | Datei (Pfad aus der Konfiguration, Umgebungsvariablen wie `%ProgramFiles%` werden aufgelöst) ist vorhanden und ihre Dateiversion (`FileMajorPart` bis `FilePrivatePart`) ist mindestens die Mindestversion. Pfad mit unaufgelöster Variable, Verzeichnis, fehlende Datei und Datei ohne Versionsinformation gelten als nicht erkannt. Läuft das Skript als 32-Bit-Prozess auf 64-Bit-Windows, bricht es mit Fehler ab (die Umgebungsvariablen zeigten sonst auf die 32-Bit-Ordner); dafür steht in den Intune-Werten `runAs32Bit = false` |

Der Vergleich ist numerisch über bis zu vier Teile, fehlende Teile zählen als 0 (`1.10` ist größer als `1.9`, `4.2.1.0` entspricht `4.2.1`). Ein Suffix wie `-beta` hinter der Versionsnummer wird ignoriert; eine Version, die nicht mit bis zu vier Zahlenteilen beginnt (zum Beispiel fünf Teile oder reiner Text), ist **nicht erkannt**. Die Prüfung ist bewusst konservativ: Im Zweifel wird nachinstalliert, nicht fälschlich erkannt.

### Intune-Vertrag

- **Erkannt:** genau eine Zeile auf der Standardausgabe (`Detected <ProductCode oder Pfad> version <Version>`) und Exitcode 0.
- **Nicht erkannt:** keine Ausgabe auf der Standardausgabe und Exitcode 1.
- **Fehler** (Ausnahme im Skript): Meldung auf der Fehlerausgabe, Exitcode 1, nie eine positive Ausgabe.

### Skriptform und Maskierung

- Windows PowerShell 5.1, UTF-8 **mit BOM**, CRLF, `Set-StrictMode`, kein PowerShell-7-Syntax.
- Werte aus der Konfiguration (ProductCode, Pfad, Mindestversion) erscheinen nur als **einfach angeführte Zeichenketten**. `PowerShellLiteral` verdoppelt das gerade Apostroph **und die typografischen Apostrophe** U+2018, U+2019, U+201A und U+201B, die PowerShell ebenfalls als Anführungszeichen liest. Steuerzeichen, Zeilentrenner (U+0085, U+2028, U+2029) und unsichtbare Richtungszeichen werden nicht maskiert, sondern abgelehnt (`UnsafeScriptValueException`).
- Der ProductCode muss eine GUID in Klammern sein, die Mindestversion eine numerische Version mit ein bis vier Teilen; sonst wird kein Skript erzeugt. Die Build-ID steht nur als Kommentar, Zeichen außerhalb von ASCII werden dort durch `?` ersetzt.
- Geschrieben wird atomar über `AtomicFile` mit BOM.

### Tests

- xUnit (`DetectionScriptGeneratorTests`): Inhalt je Verfahren, Intune-Vertrag, Eigenständigkeit, BOM, Maskierung aller Anführungszeichen, abgelehnte Eingaben, Kommentar-Einschleusung.
- Pester (`tests/pester/DetectionScript.Tests.ps1`, Windows PowerShell 5.1): erzeugt das Skript über die gebaute `Generation.dll` und führt es in einem Kindprozess gegen **echte Registry-Schlüssel** (zufälliger ProductCode, beide Ansichten, Aufräumen nach dem Test) und **echte Dateien** (`kernel32.dll` als Referenz) aus: älter, passend, neuer, fremd installiert, nicht installiert, Verzeichnis, fehlende Datei, Einschleusungsversuch (A15); Skript allein in einem leeren Ordner, aus anderem Arbeitsordner, ohne Bezug zu Paket oder Intune-Cache (A16). Der Pester-Job in `ci.yml` baut dafür vorher `Generation`.
- **Nicht abgedeckt:** das Verhalten unter dem Konto `LocalSystem` und innerhalb der Intune Management Extension; das belegt erst der Gerätetest in M8.

