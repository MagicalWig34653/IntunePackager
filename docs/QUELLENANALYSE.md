# Quellenanalyse

Beschreibt, wie das Autorentool Installationsquellen untersucht (Spec §7.1, §7.3). Das Dokument wächst mit den Scheiben von M2 (M2a MSI, M2b EXE und Manifest, M2c sicherer Import) und wird im selben PR wie der Code gepflegt.

**Grundregel:** Die Analyse führt die Software nie aus. Es wird nichts installiert, keine Custom Action läuft, kein Installer wird gestartet. Gelesen werden nur Dateiinformationen und die MSI-Datenbank.

## MSI (M2a)

Umsetzung: `IntunePackageBuilder.Analysis` (`Msi/MsiReader`, `Msi/MsiMetadata`, `Msi/NativeMsi`).

### Vorgehen

- Vorab wird die Dateikennung geprüft: Eine MSI ist eine OLE-Verbunddatei und beginnt mit den Bytes `D0 CF 11 E0 A1 B1 1A E1`. Andere Dateien (auch leere oder umbenannte Textdateien) werden sofort als `NotAnInstallerDatabase` abgelehnt, ohne vom Fehlercode der Installer-API abzuhängen (die CI hat gezeigt, dass dieser für fremde Dateien nicht `1619` oder `1620` ist).
- Die MSI wird über `MsiOpenDatabase` mit `MSIDBOPEN_READONLY` geöffnet. Das ist reiner Datenbankzugriff: Der Windows Installer wertet dabei keine Sequenzen aus und startet keine Custom Actions. Die Datei wird nicht verändert (Test: Prüfsumme und Änderungszeit bleiben gleich).
- Abgefragt werden die Tabellen `Property`, `Media` und `File`. Eine Tabelle, die nicht existiert, zählt als leer.
- Alle Handles (Datenbank, Sicht, Datensatz) werden in jedem Fall geschlossen.

### Ergebnis (`MsiMetadata`)

| Feld | Quelle | Bedeutung |
|---|---|---|
| `ProductName`, `Manufacturer` | Property-Tabelle | Vorschläge für Softwarename und Hersteller; `null`, wenn die MSI sie nicht enthält |
| `ProductVersion` | Property-Tabelle | Pflicht; Vorschlag für die Zielversion |
| `ProductCode` | Property-Tabelle | Pflicht, in geschweiften Klammern im GUID-Format; wird für Erkennung und Deinstallation verwendet |
| `UpgradeCode` | Property-Tabelle | optional |
| `AllUsers` | Property `ALLUSERS` | `null`, wenn die MSI es nicht setzt (siehe unten) |
| `Properties` | Property-Tabelle | alle Einträge, für die erweiterte Ansicht der Metadaten |
| `ExternalCabinets` | Tabelle `Media` | Cabinet-Dateien, die neben der MSI liegen (Name ohne `#` am Anfang) |
| `RequiresSourceFolder` | `Media` und `File` | siehe unten |

### Fehler (`MsiReadException.Problem`)

Die Oberfläche macht aus dem Code später einen lokalisierten Text; `NativeError` trägt die Windows-Installer-Fehlernummer für das Log.

| Problem | Ursache |
|---|---|
| `FileNotFound` | Die Datei existiert nicht |
| `NotAnInstallerDatabase` | Datei ist keine Installer-Datenbank (zum Beispiel umbenannte Textdatei) |
| `MissingProductCode`, `InvalidProductCode` | ProductCode fehlt oder ist kein GUID in Klammern |
| `MissingProductVersion` | ProductVersion fehlt |
| `ReadFailed` | Sonstiger Fehler beim Lesen |

Bei einem fehlgeschlagenen Lesen bleibt ein zuvor vorhandener Entwurf erhalten, weil der Reader keinen Zustand verändert (Spec §7.1).

### `RequiresSourceFolder` (Heuristik)

Eine einzelne MSI-Datei genügt nicht, wenn sie Dateien aus ihrem Quellordner braucht. Der Reader meldet `true`, wenn

1. mindestens ein Cabinet außerhalb der MSI liegt (`Media.Cabinet` ohne führendes `#`), oder
2. die Tabelle `File` Zeilen enthält, aber kein Eintrag in `Media` ein Cabinet nennt (unkomprimierte Dateien neben der MSI).

Der Fall „eingebettete und externe Cabinets gemischt“ liefert `true`, wenn ein externes vorhanden ist. Eine MSI ohne Dateien (zum Beispiel nur Registry) braucht keinen Quellordner. Das ist eine Ableitung aus den Tabellen und keine vollständige Prüfung aller Dateipfade; die Oberfläche soll bei `true` den Import des ganzen Herstellerordners verlangen oder anbieten.

### ALLUSERS und die Installation als LocalSystem (offene Entscheidung für M5)

Setzt eine MSI `ALLUSERS` nicht, installiert sie standardmäßig pro Benutzer. Unter LocalSystem wäre das „pro Benutzer des SYSTEM-Kontos“ und für normale Benutzer unsichtbar. Spec §3 schließt benutzerspezifische Installationen aus. Der Reader stellt deshalb `AllUsers` bereit; ob der Wrapper `ALLUSERS=1` standardmäßig mitgibt oder die Oberfläche warnt, wird in M5 mit den „sicheren MSI-Vorgaben“ entschieden und in `docs/PLANUNG.md` §8 festgehalten.

### Tests

`IntunePackageBuilder.Analysis.Tests` erzeugt kontrollierte MSI-Datenbanken mit einem Test-Helfer (`TestMsi`), der nur Tabellen schreibt (`Property`, optional `Media`, `File`, `CustomAction`); es wird nie ein Herstellerinstaller verwendet. Geprüft werden unter anderem: Identitätswerte, Sonderzeichen und lange Werte, unveränderte Datei, kein Ausführen (eine `CustomAction`-Tabelle mit einem Programmstart verändert nichts, es entsteht keine Markerdatei und kein Uninstall-Eintrag), alle Fehlerfälle und die Quellordner-Erkennung. Die Tests laufen nur auf Windows (CI).

## EXE (M2b)

Umsetzung: `IntunePackageBuilder.Analysis` (`Exe/ExeReader`, `Exe/ExeMetadata`).

### Vorgehen

- Es wird **nur das Versionsresource** der Datei gelesen (`FileVersionInfo`); die Datei wird nie gestartet.
- Als ausführbar gilt eine Datei mit dem DOS-Kopf `MZ` und mindestens 64 Byte. Alles andere (Textdatei mit Endung `.exe`, zu kurze Datei) wird mit `NotAnExecutable` abgelehnt, eine fehlende Datei mit `FileNotFound`.
- Eine EXE ohne Versionsinformation ist gültig: `HasVersionInfo` ist `false` und es gibt keine Vorschläge; Name, Hersteller und Version muss der Benutzer dann selbst eintragen.

### Ergebnis (`ExeMetadata`)

| Feld | Bedeutung |
|---|---|
| `ProductName`, `CompanyName`, `FileDescription`, `OriginalFilename` | Textwerte aus dem Versionsresource, `null` wenn leer |
| `ProductVersion` | Produktversion als Text, unverändert (kann nichtnumerische Teile enthalten) |
| `FileVersion` | Numerische Dateiversion mit bis zu vier Teilen, `null` wenn die Datei keine hat |
| `SuggestedSoftwareName` | `ProductName`, sonst `FileDescription` |
| `SuggestedManufacturer` | `CompanyName` |
| `SuggestedVersion` | Numerische Version als Vorschlag: `FileVersion`, sonst numerische `ProductVersion`; nachgestellte Nullteile entfallen, mindestens zwei Teile bleiben (`12.0.0.0` wird `12.0`), `null` ohne numerische Version |

**Alles davon sind Vorschläge.** Die Version des Setup-Programms kann von der Version der Datei abweichen, die später installiert wird. Bei einer EXE muss die Zielversion zur Erkennung passen (Erkennungsdatei mit Versionsinformation); die Oberfläche markiert die Werte als Vorschlag und lässt den Benutzer die Zielversion korrigieren. Silent-Parameter, Deinstallationsprogramm und Erkennungsdatei werden **nie** aus der Datei abgeleitet (Spec §5.2).

## Quellenmanifest (M2b)

Umsetzung: `IntunePackageBuilder.Core` (`Sources/SourceManifest`, `SourceManifestBuilder`, `SourceManifestStore`). Das Format steht in `docs/DATENFORMAT.md`.

- **Aufbau:** `SourceManifestBuilder.Build` durchläuft den gespeicherten Quellordner rekursiv und erfasst je Datei den relativen Pfad (mit `/`), die Größe und den SHA-256 (hexadezimal, Kleinbuchstaben), sortiert nach Pfad (ordinal). Junctions und Symlinks werden nie verfolgt; findet der Aufbau eine, bricht er mit `UnsafeSourceException` ab.
- **Prüfung:** `Compare` vergleicht den Ordner mit dem Manifest und liefert jede Abweichung: `Missing` (im Manifest, nicht mehr im Ordner), `Added` (im Ordner, nicht im Manifest) und `Modified` (Größe oder Prüfsumme verschieden). Eine Umbenennung erscheint als `Missing` plus `Added`. `RequireUnchanged` wirft `SourceChangedException` mit der Liste. Ein erneuter Build **muss** vor der Verwendung der Quelle prüfen, damit nie still mit einer veränderten Quelle gebaut wird (Abnahme A11).
- **Unveränderlich:** Ein vorhandenes `source-manifest.json` wird nie überschrieben (`WriteNew` verweigert es). Eine geänderte Setup-Datei erfordert eine neue Version (Spec §6.4).
- **Ablage:** `VersionStore.SourceDirectory` und `VersionStore.SourceManifestPath` liefern `versions/<version>/source` und `versions/<version>/source-manifest.json`.
- **Grenze:** Die Prüfung vergleicht Dateien und Inhalte, keine Zeitstempel oder Attribute. Leere Ordner werden nicht erfasst.

## Sicherer Import (M2c)

Umsetzung: `IntunePackageBuilder.Core` (`Sources/SourceImporter`, `Sources/PathSafety`, `VersionStore.ImportSource`).

**Leitgedanke:** Alles wird geprüft, **bevor** das erste Byte kopiert wird. Eine abgelehnte Quelle hinterlässt keine Spur (Spec §7.3, Abnahme A12). Originaldateien werden nie verändert oder gelöscht.

### Auswahl prüfen (`Classify`, Abnahme A04 Logik)

Die Auswahl über Dateidialog und Drag-and-drop läuft durch dieselbe Prüfung, damit beide Wege gleich reagieren.

| Eingabe | Ergebnis |
|---|---|
| Nichts, `null` oder leerer Pfad | `NothingDropped` |
| Mehr als ein Eintrag | `MultipleItems` (auch Datei plus Ordner) |
| Pfad existiert nicht | `NotFound` |
| Datei mit anderer Endung als `.msi` oder `.exe` (Groß-/Kleinschreibung egal) | `UnsupportedFileType` |
| Ordner im Standardmodus | `FolderNotAllowed` (die Oberfläche verweist auf den erweiterten Modus) |
| Ordner im erweiterten Modus | `DroppedKind.Folder` |
| eine MSI oder EXE | `DroppedKind.Msi` oder `DroppedKind.Exe` |

### Import einer Einzeldatei (`ImportFile`) und eines Ordners (`ImportFolder`)

Vor dem Kopieren werden geprüft, in dieser Reihenfolge:

1. **Rekursion (A12):** Liegt das Ziel im Quellordner (zum Beispiel wird das Elternverzeichnis des Projekts oder der ganze Grundordner abgelegt), wäre das ein Kopieren in sich selbst: `TargetInsideSource`. Liegt umgekehrt die Quelle im Ziel: `SourceInsideTarget`. Gleiche Ordner zählen als Ziel im Quellordner. Der Vergleich ignoriert Groß-/Kleinschreibung und abschließende Trenner.
2. **Installer:** Der Pfad muss relativ bleiben und innerhalb des Quellordners liegen (kein `..`, kein Laufwerk, kein UNC-Pfad: `InstallerOutsideSource`), die Datei muss existieren (`InstallerNotFound`) und `.msi` oder `.exe` sein (`UnsupportedFileType`).
3. **Junctions und Symlinks (A12):** Der Quellordner selbst und alles darunter muss frei von Verknüpfungen sein; nichts wird verfolgt. Fund: `ReparsePointFound` mit dem Pfad.
4. **Pfadlänge:** Der längste entstehende Zielpfad (einschließlich des temporären Ordners) darf 259 Zeichen nicht überschreiten, weil Windows-Anwendungen ohne Langpfad-Unterstützung sonst mitten im Kopieren mit unverständlichen Fehlern scheitern: `PathTooLong` mit dem betroffenen Pfad.
5. **Ziel:** Ein Zielordner mit Inhalt wird nie überschrieben (`TargetNotEmpty`); ein vorhandener leerer Ordner ist erlaubt.

Eine einzelne Datei wird allein kopiert, nicht ihre Nachbarn. Leere Unterordner werden nicht übernommen (wie im Manifest).

### Kopieren

- Die Dateien gehen zuerst in einen **temporären Nachbarordner** (`<ziel>.importing-<Zufallskennung>`), nicht direkt ins Ziel.
- Danach wird jede kopierte Datei gegen das Original geprüft (Größe und SHA-256) und das Manifest aus der Kopie gebildet.
- Erst wenn alles stimmt, wird der Ordner in einem Schritt in `source` umbenannt. Ein Fehler beim Kopieren (zum Beispiel eine gesperrte Datei) entfernt **nur den eigenen temporären Ordner**; ein vorhandener Zielordner und die Originale bleiben unberührt.
- `VersionStore.ImportSource` nimmt dabei die Sperre der Version, schreibt anschließend `source-manifest.json` und entfernt die gerade angelegte Kopie wieder, wenn das Manifest nicht geschrieben werden kann. Eine Version behält ihre gespeicherte Quelle für immer: ein zweiter Import wird mit `SourceAlreadyStored` abgelehnt; eine geänderte Setup-Datei braucht eine neue Version (Spec §6.4).

### Grenzen

- Pfade werden als Text verglichen; kurze 8.3-Namen oder andere Aliase desselben Ordners werden nicht aufgelöst.
- Dateisymlinks lassen sich im Test nur mit Berechtigung erzeugen; getestet ist die Ablehnung echter Junctions (Ordnerverknüpfungen). Der Code prüft dasselbe Attribut (`ReparsePoint`) für Dateien und Ordner.
