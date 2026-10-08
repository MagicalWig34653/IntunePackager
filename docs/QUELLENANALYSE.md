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

## EXE und Quellenmanifest (M2b)

Noch nicht umgesetzt.

## Sicherer Import (M2c)

Noch nicht umgesetzt.
