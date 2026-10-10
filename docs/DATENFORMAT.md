# Datenformat und Migrationsstrategie

Beschreibt die Dateien, die der Core liest und schreibt (Spec §6, Liefergegenstand 4 in §13). Das Dokument wird zusammen mit dem Code gepflegt; beim Ändern eines Formats gehören Schema-Version, Migration, Tests und dieses Dokument in denselben PR.

Stand: M4b (Projekt, Versionskonfiguration, Versionsordner, Sperre, Einstellungen des Autorentools, Quellenmanifest, Build-Snapshot, Wrapper-Konfiguration, Build-Manifest).

## Ablage

```text
<Grundordner>/
  <projekt-id>/
    project.json
    versions/<zielversion>/
      configuration.json
      source-manifest.json
      source/...
      builds/<build-id>/...       (siehe docs/BUILD.md)
```

Alle JSON-Dateien: UTF-8 ohne BOM, eingerückt, Namen in camelCase, Enumerationen als Text. Zeitstempel sind UTC im ISO-8601-Format. Datumsähnliche Texte (zum Beispiel in Notizen) bleiben beim Lesen unverändert. **Ausnahme:** `Deployment.config.json` (unten) ist UTF-8 **mit** BOM, weil Windows PowerShell 5.1 BOM-lose Dateien als ANSI liest.

## Gemeinsame Regeln

- **`schemaVersion`** (ganze Zahl ab 1) steht in jeder Datei. Fehlt sie oder ist sie keine positive ganze Zahl, ist die Datei nicht lesbar.
- **Schreiben ist atomar:** Inhalt wird in eine temporäre Datei im selben Ordner geschrieben, auf den Datenträger geflusht und dann in einem Schritt an die Stelle der Zieldatei gesetzt. Ein Absturz hinterlässt keine halb geschriebene Datei. Schlägt das Ersetzen fehl (zum Beispiel weil die Zieldatei gesperrt ist), bleibt das Original unverändert und die temporäre Datei wird entfernt.
- **Neuere Formate werden nie überschrieben:** Hat eine Datei eine höhere `schemaVersion` als das Programm kennt, wird sie nicht gelesen und `Save` verweigert das Überschreiben (`UnsupportedSchemaException`). Der Benutzer soll das Programm aktualisieren.
- **Listen werden beim Lesen ersetzt, nicht ergänzt:** Die Standardwerte aus dem Programm (zum Beispiel Erfolgscode 0) werden durch die gespeicherte Liste ersetzt; wiederholtes Laden und Speichern verändert eine Liste nicht. (Ein Test hat das als Fehler aufgedeckt: Newtonsoft ergänzt standardmäßig.)
- **Unbekannte Felder** gleicher Schema-Version werden beim Lesen ignoriert und beim erneuten Speichern nicht erhalten. Das ist eine bekannte Grenze; Felder werden nur mit einer neuen Schema-Version hinzugefügt.

## Migration

Eine Migration besteht aus Schritten `IMigrationStep` (`FromVersion` nach `FromVersion + 1`), die im Speicher auf das JSON-Dokument angewendet werden. Ablauf beim Laden:

1. Version lesen. Neuer als unterstützt: abbrechen (siehe oben). Gleich: nichts tun.
2. Älter: die Schritte lückenlos der Reihe nach anwenden und `schemaVersion` nach jedem Schritt hochsetzen. Fehlt ein Schritt, bricht die Migration mit `MigrationException` ab; die Datei bleibt unverändert.
3. Wurde migriert: **zuerst** die Originaldatei als `<datei>.v<alte-version>.bak` sichern (ein vorhandenes Backup wird nicht überschrieben), **dann** die migrierte Datei atomar schreiben. Damit ist die Wiederherstellung möglich: Backup zurückkopieren und eine ältere Programmversion verwenden.

Aktuell existiert nur Schema-Version 1; es gibt noch keinen echten Migrationsschritt. Der Mechanismus ist mit Test-Schritten abgesichert.

## `project.json` (Schema 1)

| Feld | Typ | Bedeutung |
|---|---|---|
| `schemaVersion` | Zahl | 1 |
| `projectId` | Text | Unveränderliche ID, muss dem Ordnernamen entsprechen |
| `displayName` | Text | Anzeigename (beim Anlegen getrimmt) |
| `createdUtc` | Zeitstempel | Erstellungszeitpunkt (UTC) |
| `notes` | Text | Freitext-Notizen des Projekts, unabhängig von einer Softwareversion; beliebiger Inhalt, Zeilenumbrüche und Sonderzeichen bleiben erhalten |

### Projekt-ID

Kleinbuchstaben a-z, Ziffern und Bindestrich, 1 bis 64 Zeichen, nicht mit Bindestrich beginnend oder endend, kein reservierter Windows-Gerätename (`con`, `prn`, `aux`, `nul`, `com1`-`com9`, `lpt1`-`lpt9`). Punkte, Leerzeichen, Pfadtrenner und `..` sind ausgeschlossen. Aus einem Anzeigenamen wird ein gültiger Vorschlag erzeugt (Umlaute werden zu `ae`, `oe`, `ue`, `ss`, Akzente entfernt, Sonderzeichen zu Bindestrichen; Rückfall `project`).

### Projektliste

Die Liste durchsucht alle Unterordner des Grundordners, die eine `project.json` enthalten. Ein defektes Projekt (ungültiges JSON, neueres Schema, ID passt nicht zum Ordner, ungültiger Ordnername) erscheint mit seinem Fehler in der Liste, beeinträchtigt aber weder die anderen Projekte noch deren Dateien.

## `configuration.json` (Schema 1)

Konfiguration einer Softwareversion (Spec §6.3). Werte, die das Tool nicht kennen kann (EXE-Schalter, Deinstallationspfad, Erkennungsdatei), bleiben leer und werden vom Validator gemeldet; sie werden nie mit Annahmen gefüllt.

| Bereich | Feld | Bedeutung |
|---|---|---|
| (Wurzel) | `schemaVersion`, `projectId` | Schema und zugehöriges Projekt |
| `identity` | `softwareName`, `manufacturer`, `targetVersion` | Zielzustand auf dem Endgerät; die Zielversion ist weder Build-ID noch Version dieses Tools |
| `source` | `installerType` (`Msi` oder `Exe`), `installerRelativePath`, `importKind` (`SingleFile` oder `Folder`) | Installer relativ zum gespeicherten Quellordner |
| `install` | `arguments`, `productCode`, `targetArchitecture` (`X64` oder `X86`) | Stille Parameter (EXE: vom Hersteller), ProductCode (MSI) |
| `uninstall` | `productCode`, `executablePath`, `arguments` | MSI über ProductCode, EXE über Programm und Parameter |
| `detection` | `method` (`MsiProductCode` oder `FileVersion`), `path`, `minimumVersion` | Erkennung des tatsächlichen Zustands; `path` nur für EXE |
| `interaction` | `processesToClose`, `installMessage`, `uninstallMessage`, `detailMessage` | Benutzerhinweise |
| `postInstall` | `sharedShortcutsToRemove` (`root`: `PublicDesktop` oder `CommonStartMenu`, `relativePath`) | Ausdrücklich benannte gemeinsame Verknüpfungen |
| `runtime` | `timeoutMinutes`, `successCodes`, `rebootCodes`, `retryCodes` | Standard: 60 Minuten, Erfolg 0, Neustart 3010, Wiederholung 1618 |
| `deployment` | `engine` (`Native` oder `Psadt`), `psadt` | Laufzeit auf dem Gerät. **Additiv:** Dateien ohne diesen Bereich gelten als `Native`; das Schema bleibt 1. Die Toolkit-Optionen bleiben beim Zurückschalten auf `Native` erhalten |

`deployment.psadt` (nur wirksam bei `engine` = `Psadt`; Standardwerte in Klammern):

| Feld | Bedeutung |
|---|---|
| `dialogStyle` | `Fluent` (Standard) oder `Classic` |
| `accentColor` | Akzentfarbe der Fluent-Dialoge als `#RRGGBB`, oder fehlt (Farbe von Windows) |
| `companyName` | Firmenname in den Dialogen, oder fehlt (Hersteller der Software) |
| `uiLanguage` | fehlt (Sprache des Builds), `auto` (Sprache des Benutzers auf dem Gerät) oder ein Sprachcode des Toolkits (`de`, `en`, `fr`, …) |
| `logoFile`, `logoDarkFile`, `bannerFile` | Dateiname eines Bildes im Ordner `psadt-assets` der Version (PNG oder JPEG), oder fehlt (Bilder des Toolkits) |
| `balloonNotifications` (`true`), `showProgress` (`true`) | Hinweise am Bildschirmrand, Fortschrittsfenster während der Installation |
| `allowDefer` (`false`), `deferTimes` (3) | Benutzer darf verschieben, wenn Programme offen sind; ein Verschieben endet mit dem Wiederholungscode |
| `checkDiskSpace` (`false`), `requiredDiskSpaceMb` (0) | Prüfung des freien Speichers; 0 lässt das Toolkit rechnen |

Die Bilder liegen im Versionsordner unter `psadt-assets/` (`logo.png`, `logo-dark.png`, `banner.png`, jeweils auch `.jpg`); die Konfiguration enthält nur Dateinamen, nie Pfade des Benutzers. Ein Bild darf höchstens 2 MB groß sein, und die ersten Bytes müssen PNG oder JPEG sein (die Endung der Quelle zählt nicht). Eine neue Version aus einer Vorlage übernimmt die Bilder der Vorlage, ohne etwas zu überschreiben.

### Validierungsregeln

Der Validator liefert eine Liste aus `Field` (JSON-Pfad) und `Code`; die Oberfläche macht daraus später lokalisierte Texte. Ein Paket ist nur baubar, wenn die Liste leer ist.

- **Immer:** `identity.softwareName`, `identity.manufacturer`, `identity.targetVersion` (numerisch, 1 bis 4 Teile) und `source.installerRelativePath` sind Pflicht. Der Installerpfad muss relativ bleiben: keine Laufwerks- oder UNC-Angabe, keine leeren, `.`- oder `..`-Segmente, keine Zeichen `< > " | ? *` (Code `UnsafePath`).
- **MSI:** `install.productCode` ist Pflicht und eine Zeichenfolge in geschweiften Klammern im GUID-Format; `detection.method` muss `MsiProductCode` sein; `detection.minimumVersion` ist Pflicht. Stille Parameter und Erkennungspfad sind **nicht** erforderlich.
- **EXE:** `install.arguments`, `uninstall.executablePath`, `uninstall.arguments` und `detection.path` sind Pflicht; die Pfade müssen absolut sein (Laufwerk oder beginnend mit einer Umgebungsvariablen); `detection.method` muss `FileVersion` sein; `detection.minimumVersion` ist Pflicht. Das deckt Abnahme A03 auf Modellebene ab.
- **Laufzeit:** `timeoutMinutes` zwischen 1 und 1440; `successCodes` darf nicht `1641` enthalten (bereits ausgelöster Neustart ist kein stiller Erfolg); Erfolgs-, Neustart- und Wiederholungscodes dürfen sich nicht überschneiden.
- **Verknüpfungen:** jede muss ein einzelner Pfad unterhalb der Wurzel sein, der auf `.lnk` endet, ohne Traversal.
- **Toolkit** (nur bei `deployment.engine` = `Psadt`): `accentColor` ist `#RRGGBB`; `uiLanguage` ist leer, `auto` oder ein Sprachcode des Toolkits; die Bilddateinamen sind einfache Namen (Buchstaben, Ziffern, Leerzeichen, `.`, `-`, `_`) mit der Endung `.png`, `.jpg` oder `.jpeg`; `deferTimes` liegt zwischen 0 und 99 (mit `allowDefer` mindestens 1); `requiredDiskSpaceMb` zwischen 0 und 1048576; es muss mindestens ein Wiederholungscode in `runtime.retryCodes` stehen (ein verschobener oder abgelaufener Dialog endet mit diesem Code); `interaction.*Message` und `companyName` enthalten keine geschweiften Klammern (das Toolkit liest `{Name}` in seinen Texten als Nachschlagen in der eigenen Konfiguration).

### Versionsordner und Versionsspeicher

Jede Softwareversion liegt in `versions/<version>/` unterhalb des Projekts. Der Ordnername ist die Zielversion, wie sie beim Anlegen angegeben wurde (zum Beispiel `4.2.1`).

- **Identität einer Version ist ihre numerische Zielversion.** `1.0` und `1.0.0` sind dieselbe Version: Anlegen einer numerisch gleichen Version wird abgelehnt (`VersionExistsException`), Laden und Speichern finden den Ordner über numerische Gleichheit (`1.0` findet `1.0.0`).
- **Entwürfe sind erlaubt:** Eine unvollständige Konfiguration (zum Beispiel eine EXE ohne Herstellerwerte) kann gespeichert werden. Baubar ist sie erst, wenn der Validator keine Probleme meldet.
- **Die Zielversion einer gespeicherten Version ändert sich nicht.** `Save` verlangt, dass die Konfiguration noch dieselbe Zielversion beschreibt; eine andere Zielversion ist eine neue Version (Update-Ablauf, Spec §6.5). Damit kann kein Speichern versehentlich eine andere Version überschreiben.
- **Beim Laden werden Konsistenz und Zugehörigkeit geprüft:** Die Konfiguration muss zum Projekt und zum Ordnernamen passen, sonst gilt sie als defekt (`StorageFormatException`).
- **Versionsliste:** neueste zuerst nach numerischer Version (nicht nach Text und nicht nach Build-Zeitpunkt). Ordner ohne `configuration.json` werden übersprungen; Ordner, deren Name keine Version ist, stehen am Ende mit Fehler. Ein defekter Eintrag (ungültiges JSON, neueres Schema, falsche Zugehörigkeit) beeinträchtigt die anderen nicht.

### Sperre je Projektversion

`Create` und `Save` nehmen die Sperre der Version, bevor sie schreiben. Die Sperre ist eine exklusiv geöffnete Datei `versions/<version>/.lock`:

- Die Sperre **ist das offene Dateihandle**, nicht die Existenz der Datei. Stürzt ein Prozess ab, gibt das Betriebssystem das Handle frei; eine übrig gebliebene `.lock`-Datei blockiert niemanden (verwaiste Sperren lösen sich von selbst und werden beim nächsten Erwerb überschrieben).
- Der Halter schreibt `processId`, `machineName`, `userName` und `acquiredUtc` in die Datei. Ein zweiter Prozess, dem der Erwerb verweigert wird (`LockHeldException`), kann den Halter lesen und verständlich melden.
- Beim Freigeben wird die Datei gelöscht. Die `.lock`-Datei gehört nie zu Quellen, Konfiguration oder Build-Ergebnissen.
- Schreiben in eine Version, die ein anderer Prozess hält, schlägt fehl und lässt die Dateien unverändert.

### Versionsnummern

Numerisch, 1 bis 4 Teile aus je höchstens 9 Ziffern. Der Vergleich ist numerisch und ignoriert nachgestellte Nullen (`1.0` = `1.0.0`, `1.10` > `1.9`). Die Versionsliste einer Software wird nach dieser Ordnung sortiert, nicht nach Text und nicht nach Build-Zeitpunkt.

## `source-manifest.json` (Schema 1)

Liegt in `versions/<version>/` neben dem Ordner `source/` und beschreibt jede Datei der gespeicherten Installationsquelle. Siehe `docs/QUELLENANALYSE.md` für Aufbau und Prüfung.

| Feld | Typ | Bedeutung |
|---|---|---|
| `schemaVersion` | Zahl | 1 |
| `createdUtc` | Zeitstempel | Zeitpunkt der Erfassung (UTC) |
| `files` | Liste | je Datei `path` (relativ, `/` als Trenner), `size` (Byte), `sha256` (64 Zeichen, Kleinbuchstaben); sortiert nach `path` (ordinal) |

Das Manifest ist ein unveränderlicher Nachweis der Quelle: Es wird nie überschrieben, und eine Datei ohne `path` oder `sha256` gilt als defekt (`StorageFormatException`). Eine neuere Schema-Version wird nicht gelesen.

## `configuration.snapshot.json` (Schema 1)

Liegt in `versions/<version>/builds/<build-id>/` neben dem Build-Ergebnis. Der Snapshot hält fest, was die Ausgabe dieses Builds bestimmt, und wird **einmal geschrieben und nie ersetzt** (`BuildSnapshotStore.WriteNew` verweigert es). So bleiben frühere Ergebnisse nachvollziehbar, auch wenn die Konfiguration der Version später geändert wird (Spec §6.4). Siehe `docs/GENERATOREN.md`.

| Feld | Typ | Bedeutung |
|---|---|---|
| `schemaVersion` | Zahl | 1 |
| `buildId` | Text | `yyyyMMdd-HHmmss-xxxx` (UTC, vier Hexzeichen in Kleinbuchstaben); dateisystemsicher |
| `createdUtc` | Zeitstempel | Zeitpunkt des Builds (UTC) |
| `language` | Text | `de` oder `en`: Sprache der erzeugten Texte, beim Build festgelegt |
| `toolVersion` | Text | Version des Autorentools |
| `sourceFingerprint` | Text | SHA-256 über Pfad, Größe und Prüfsumme aller Quelldateien (64 Kleinbuchstaben-Hexzeichen), ohne Zeitstempel |
| `configuration` | Objekt | private Kopie der `configuration.json` zum Zeitpunkt des Builds |

Eine Datei ohne gültige Build-ID oder ohne Konfiguration gilt als defekt (`StorageFormatException`); eine neuere Schema-Version wird nicht gelesen.

## `settings.json` des Autorentools (Schema 1)

Liegt **nicht** im Grundordner, sondern unter `%LOCALAPPDATA%\Intune Package Builder\settings.json` (ohne Administratorrechte beschreibbar, getrennt von austauschbaren Programmdateien und von den Projekten).

| Feld | Typ | Bedeutung |
|---|---|---|
| `schemaVersion` | Zahl | 1 |
| `baseFolder` | Text oder fehlt | Gewählter Grundordner; fehlt, solange der Benutzer noch keinen gewählt hat |
| `contentPrepToolPath` | Text oder fehlt | Pfad zu `IntuneWinAppUtil.exe`; fehlt, solange keiner gewählt ist. Das Programm liefert das Werkzeug nicht mit (`docs/PLANUNG.md` §8 Nr. 7). Eine unbekannte Version wird nur nach ausdrücklicher Bestätigung im Einzelfall verwendet; diese Bestätigung wird **nicht** gespeichert |
| `psadtPackagePath` | Text oder fehlt | Pfad zur ZIP-Datei einer PSAppDeployToolkit-Veröffentlichung (`PSAppDeployToolkit_Template_v4.zip`); fehlt, solange keine gewählt ist. Das Programm liefert das Toolkit nicht mit (`docs/PLANUNG.md` §8 Nr. 12). Eine unbekannte Version wird nur nach ausdrücklicher Bestätigung im Einzelfall verwendet; diese Bestätigung wird **nicht** gespeichert |
| `recentProjects` | Liste aus `projectId` und `lastOpenedUtc` | Zuletzt geöffnete Projekte, neueste zuerst, höchstens 10, ohne Duplikate |

- **Der Programmmodus (Standard oder Erweitert) wird nicht gespeichert.** Der Standardmodus ist bei jedem Start aktiv (Spec §5.2).
- **Die Einstellungen verhindern nie den Programmstart.** Fehlt die Datei, ist sie beschädigt oder neuer als bekannt, liefert `Load` Standardwerte und den Fehler zur Anzeige. Eine beschädigte Datei wird vor dem Ersetzen als `settings.json.invalid-<Zeitstempel>.bak` gesichert; eine neuere Datei wird nie überschrieben (`UnsupportedSchemaException`).
- **Ungültige oder doppelte Einträge** in `recentProjects` (zum Beispiel aus einer von Hand bearbeiteten Datei) werden beim Laden verworfen.

### Grundordner

`BaseFolderResolver.Resolve` bewertet den konfigurierten Grundordner:

| Status | Bedeutung | `Path` |
|---|---|---|
| `NotConfigured` | Noch nichts gewählt (Erststart) | Vorschlag `Dokumente\Intune-Paketprojekte`, wird nicht angelegt |
| `Ok` | Ordner existiert und ist beschreibbar (Schreibprobe mit einer temporären Datei) | der konfigurierte Ordner |
| `Unreachable` | Ordner fehlt, ist kein Ordner, ist nicht absolut oder nicht erreichbar (zum Beispiel Netzlaufwerk offline) | der konfigurierte Ordner |
| `NotWritable` | Ordner existiert, aber der Benutzer darf dort nicht schreiben | der konfigurierte Ordner |

**Ein bereits konfigurierter Grundordner wird nie unbemerkt durch einen anderen ersetzt**, auch wenn er nicht erreichbar ist: der Status wird gemeldet, der Pfad bleibt. Ein relativer Pfad wird nicht angenommen, weil er vom Arbeitsverzeichnis abhinge.

## `Deployment.config.json` (Schema 1)

Konfiguration der Client-Laufzeit. Liegt im **Wurzelordner des Pakets** neben `Install.cmd` (nicht im Projekt), wird je Build aus Snapshot und `IntuneSettings` erzeugt und vom Wrapper mit Windows PowerShell 5.1 gelesen (UTF-8 mit BOM). Sie ist keine Eingabe des Benutzers und wird nicht bearbeitet.

| Feld | Bedeutung |
|---|---|
| `schemaVersion`, `buildId`, `language`, `projectId` | wie im Snapshot; `language` bestimmt die Sprache der Hinweise auf dem Gerät |
| `softwareName`, `manufacturer`, `targetVersion` | Identität der Software |
| `install` | `installerType` (`Msi`/`Exe`), `installerPath` relativ zur Paketwurzel (`Files\<relativer Installerpfad>`), `arguments` (nur was der Benutzer angegeben hat), `productCode`, `targetArchitecture` |
| `uninstall` | `productCode` oder `executablePath` mit `arguments` |
| `detection` | dieselbe Regel wie im Erkennungsskript (`method`, `productCode` oder `path`, `minimumVersion`); der Wrapper prüft damit den Zielzustand nach der Installation |
| `engine` | `Native` (eigener Wrapper) oder `Psadt` (PSAppDeployToolkit); bestimmt, welches Skript `Install.cmd` startet |
| `psadt` | nur bei `engine` = `Psadt`: `showProgress`, `allowDefer`, `deferTimes`, `checkDiskSpace`, `requiredDiskSpaceMb`. Aussehen (Farben, Bilder, Stil) steht **nicht** hier, sondern in `Config\config.psd1` |
| `timeoutMinutes`, `returnCodes` | identisch mit den Intune-Werten (`code`, `type`); 1641 steht immer als `HardReboot` |
| `processesToClose`, `installMessage`, `uninstallMessage`, `detailMessage` | Benutzerinteraktion; leere Texte entfallen |
| `sharedShortcutsToRemove` | Liste aus `root` (`PublicDesktop`/`CommonStartMenu`) und `relativePath` |
| `logs` | `directory`, `deploymentLog`, `msiInstallLog`, `msiUninstallLog` |

Felder ohne Wert fehlen. Der Wrapper (M5) liest nur diese Datei und erfindet keine Werte.

## `build-manifest.json` (Schema 1)

Liegt in `versions/<version>/builds/<build-id>/`. Wird **einmal** nach den Dateien geschrieben, die es aufführt, und nie ersetzt (`BuildManifestStore.WriteNew`).

| Feld | Bedeutung |
|---|---|
| `schemaVersion`, `buildId`, `createdUtc` | wie im Snapshot |
| `projectId`, `softwareVersion` | Projekt und **Zielversion der Software** (nicht die Build-ID) |
| `language`, `toolVersion` | Sprache der erzeugten Texte, Version des Autorentools |
| `sourceFingerprint` | Fingerabdruck der verwendeten Quelle (`source-manifest.json`) |
| `contentPrepToolVersion`, `contentPrepToolSha256` | Version (nur wenn die SHA-256 einer bekannten Version entspricht) und SHA-256 des verwendeten Content Prep Tools |
| `toolkitVersion`, `toolkitSha256`, `toolkitAssets` | nur bei Paketen mit dem PSAppDeployToolkit: bekannte Version (fehlt bei bestätigter unbekannter Version), SHA-256 der verwendeten ZIP und die Bilder (`path` unter `psadt-assets/`, `size`, `sha256`) |
| `package` | `path`, `size`, `sha256` der `.intunewin`-Datei |
| `files` | alle weiteren Dateien des Build-Ordners außer dem Manifest selbst und `build.log`: `path` (relativ, `/`), `size`, `sha256` |

Ein Manifest ohne gültige Build-ID oder ohne Paketeintrag gilt als defekt (`StorageFormatException`).

## Pakete mit dem PSAppDeployToolkit

Wählt eine Version `deployment.engine` = `Psadt`, entsteht dieses Paket (Quellen der Vorlagen: `deploy/psadt/template/` und `deploy/template/`):

| Datei im Paket | Herkunft |
|---|---|
| `Install.cmd`, `Invoke-AppDeployToolkit.ps1` | Vorlage der Toolkit-Engine; ersetzt `Deploy-Wrapper.ps1` |
| `DeployCore.psm1`, `Messages.*.psd1` | gemeinsame Laufzeit (Installer starten und abwarten, Rückgabecodes, Prüfung des Zielzustands, Nacharbeiten) |
| `PSAppDeployToolkit\` | der Ordner `PSAppDeployToolkit` aus der ZIP des Benutzers, **unverändert** (ohne `Frontend` und Debugsymbole); enthält `COPYING.Lesser` |
| `Config\config.psd1` | **Überlagerung**: nur die Werte, die dieses Paket ändert (Firmenname, Logpfad, Dialogstil, Akzentfarbe, Wiederholungscode für Abbruch und Verschieben, Zeitlimit der Dialoge, Sprache, Bilder). Das Toolkit legt sie über seine Standardwerte; leere Werte lassen den Standard stehen |
| `Strings\strings.psd1`, `Strings\de\strings.psd1` | Überlagerung der Texte: Das Toolkit kündigt standardmäßig an, Programme „automatisch zu schließen“; dieses Paket beendet nie ein Programm, deshalb sagen die Texte, dass der Benutzer sie schließt. Dazu kommen der Detailhinweis und die Fortschrittstexte aus der Konfiguration |
| `Assets\` | die gewählten Bilder |

Das Zeitlimit der Dialoge ist wie beim eigenen Wrapper ein Drittel des Intune-Zeitlimits, höchstens 30 Minuten; danach endet der Lauf mit dem Wiederholungscode.

### `deploy/psadt/psadt.json` (Schema 1)

Die bekannten Toolkit-Versionen. Der Workflow `update-psadt.yml` trägt neue Veröffentlichungen ein (Pull Request, nie selbst gemerged). Feld `versions` (neueste zuerst): `version`, `asset`, `sha256` (der ZIP), `commit`, `bundledLibraries` (Dateinamen im Ordner `lib`, um neue Bibliotheken zu bemerken); dazu `license` und `licenseSha256` (der Datei `COPYING.Lesser`).

## Geplante Formate

- Keine weiteren Dateiformate sind geplant, die noch nicht beschrieben sind. `Einstellungen.json` und `Einstellungen.csv` sind Ausgaben (Format in `docs/GENERATOREN.md`).
