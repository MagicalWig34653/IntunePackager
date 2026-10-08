# Arbeitsstand (Übergabe-Datei)

**Regel: Sessions are disposable.** Diese Datei muss es jeder neuen Session ermöglichen, ohne Rückfrage weiterzuarbeiten. Sie wird in jedem PR aktualisiert. Zuerst lesen: diese Datei, dann `CLAUDE.md`, dann `docs/PLANUNG.md`.

Stand: 2026-10-08

## Aktueller Meilenstein

**M2 - Quellenanalyse** (MSI-Reader, EXE-Metadaten, Quellenmanifest, sicherer Import). Abnahme: A02, A04 (Logik), A11, A12. M1 (Core) ist abgeschlossen.

Aufteilung in einzeln mergbare Scheiben:

| Scheibe | Inhalt | Stand |
|---|---|---|
| M2a | MSI-Reader: ProductName, ProductVersion, ProductCode, Manufacturer lesend über `msi.dll` (nur Datenbank öffnen, nie installieren oder Custom Actions ausführen); Test-Helfer, der kontrollierte MSI-Datenbanken erzeugt | Code und Tests geschrieben, PR offen, CI-Nachweis steht aus |
| M2b | EXE-Metadaten über `FileVersionInfo` (nur Dateiinformation); Quellenmanifest `source-manifest.json` (relative Pfade, Größen, SHA-256 aller Dateien) und Erkennung nachträglich veränderter Quellen (A11) | offen |
| M2c | Sicherer Import (Einzeldatei und Ordner): Pfade bleiben im Quellordner, Junctions und Symlinks abgelehnt, kein rekursives Kopieren (Quelle enthält Projekt, Ziel oder Arbeitsordner), Pfadlängen vorab geprüft, mehrere oder falsche Dateitypen abgelehnt (A04 Logik, A12) | offen |

Hinweise für M2a: `msi.dll` per P/Invoke (`MsiOpenDatabase` mit Schreibschutz, `MsiDatabaseOpenView` auf die Tabelle `Property`, `MsiViewExecute`, `MsiViewFetch`, `MsiRecordGetString`, `MsiCloseHandle` für jedes Handle). Der Reader gehört nach `IntunePackageBuilder.Analysis`. Test-MSIs entstehen im Test per `MsiOpenDatabase` im Erzeugungsmodus, `CREATE TABLE` für `Property`, `INSERT` und `MsiDatabaseCommit`; keine Herstellerinstaller, keine Ausführung (A02). Erkennbare externe Quelldateien (Tabellen `File` und `Media`, externe Cabinets) sind zu berücksichtigen oder als erforderlicher Ordnerimport kenntlich zu machen.

M1 (abgeschlossen): M1a Projekt-ID, Versionsnummer, atomares Schreiben, Schema-Migration, Projektspeicher, Versionsmodell mit Validierung; M1b Sperre je Projektversion, Versionsspeicher; M1c Einstellungen, Grundordner, zuletzt geöffnete Projekte.

## Erledigt und verifiziert (CI-Nachweis)

Alle Läufe: Windows-CI mit `windows-latest`, `windows-2022` und Pester unter Windows PowerShell 5.1.

| Stand | CI-Lauf | Ergebnis |
|---|---|---|
| M0: Gerüst, `FileLogger`, Pester-Gerüst, Format-Prüfung | 37810006107 | 8 xUnit, 10 Pester bestanden |
| M1a: Projekt-ID, Versionsnummer, atomares Schreiben, Migration, Projektspeicher, Versionsmodell mit Validierung | 37825409326 | 135 xUnit, 10 Pester bestanden |
| M1b: Sperre je Version (echter Zwei-Prozess-Test), Versionsspeicher, gemeinsamer JSON-Helfer | 37827032442 | 156 xUnit, 10 Pester bestanden |
| M1c: Einstellungen, Grundordner, zuletzt geöffnete Projekte | 37828209771 | 187 xUnit, 10 Pester bestanden |

Außerdem fertig: Planung, Spezifikation, README (de/en), Icon, Entwurfs-Mockups, Pages-Webseite (deployt), Claude-Code-Umgebung (`CLAUDE.md`, Agents, Skills, Hook). Neue Dateien und Formate: siehe `docs/DATENFORMAT.md`.

## In Arbeit

- M2a (Branch `claude/great-feynman-hi1xfp`): Code unverifiziert bis zum CI-Lauf (kein .NET-SDK und keine Windows-Installer-API in der Cloud-Sitzung).
  - Neu: `Analysis/Msi/{NativeMsi,MsiMetadata,MsiReader}.cs`, neues Testprojekt `tests/IntunePackageBuilder.Analysis.Tests` (in der Solution) mit `TestMsi` und `MsiReaderTests`.
  - Doku: neue Datei `docs/QUELLENANALYSE.md` (wächst mit M2b und M2c).

## Lehren (für künftige Sessions)

- **Die CI findet echte Fehler.** M1a: Newtonsoft hängt Listen beim Laden an Konstruktor-Standards an (`[0]` wurde `[0, 0]`); behoben mit `ObjectCreationHandling.Replace`. M1b: der Kindprozess im Sperr-Test fand Newtonsoft.Json nicht, weil xUnit Assemblies in einen Shadow-Copy-Ordner kopiert; der Test kopiert die DLLs jetzt aus dem Originalordner (`CodeBase`).
- **Der Edit/Write-Hook greift nicht bei Dateien, die per Bash-Heredoc entstehen.** Danach immer `python3 -I .claude/skills/check-i18n/check_i18n.py .` laufen lassen. Umlaute im C#-Code als `\u00e4`-Escapes schreiben; mein Schreibwerkzeug löst Escapes manchmal schon beim Schreiben zu echten Zeichen auf.
- **`Assert.True(false, ...)` löst xUnit2020 aus** und bricht den Build (Warnungen sind Fehler); `Assert.Fail(...)` verwenden.
- **`JObject.Parse` formatiert datumsähnliche Texte um**; deshalb `JsonFormat.ParseObject` benutzen (Notizen bleiben unverändert).
- **Die Sperrdatei hat der Halter schreibend offen**; zum Lesen `FileShare.ReadWrite | FileShare.Delete` angeben.
- Chromium (Render-Skript) stürzt bei langem `TMPDIR` ab; das Skript gibt dem Browser ein kurzes Temp-Verzeichnis.
- Der Abruf der Pages-Seite aus der Cloud-Sitzung ist durch die Netzwerkrichtlinie gesperrt (Host `magicalwig34653.github.io`); der Deploy-Status ist über die Actions-Läufe prüfbar.

## Nächste Schritte (in Reihenfolge)

1. PR 8 mergen (CI ist grün).
2. M2a mergen (CI grün), danach M2b und M2c.
3. Nach jeder Scheibe: Doku im selben PR (`docs/DATENFORMAT.md` für `source-manifest.json`, Matrix A02, A04, A11, A12 erst mit CI-Nachweis), `docs/ARBEITSSTAND.md` aktualisieren.
4. Nach M2: M3 (Generatoren: Erkennungsskript, `IntuneSettings`, HTML/JSON/CSV); M4 und M5 können danach parallel laufen (siehe `docs/PLANUNG.md` §4).

## Entscheidungen (Kurzfassung, Details in `docs/PLANUNG.md` §8)

- Name bleibt Intune Package Builder; UI Deutsch und Englisch nach Programmsprache; Quellcode komplett Englisch; Doku unter `docs/` Deutsch.
- JSON-Bibliothek für das Autorentool: Newtonsoft.Json (läuft ohne Zusatzprobleme auf .NET Framework 4.8; `System.Text.Json` zieht auf net48 viele Abhängigkeiten nach).
- Fehler werden im Core als Codes/Ausnahmen gemeldet, nie als Anzeigetext; Anzeigetexte kommen später aus Ressourcen.
- Optionales Upload-Modul (M9) ist vorgemerkt, nicht beschlossen (`docs/PLANUNG.md` §9).

## Offene Fragen

- PSADT-Version, Mindest-Windows-Build, Signierung, Zielarchitektur, Test-Infrastruktur (siehe `docs/PLANUNG.md` §8). Keine davon blockiert M1 oder M2.
- Pages-Seite ist deployt; ihr Abruf aus der Cloud-Sitzung ist durch die Netzwerkrichtlinie gesperrt, daher nicht von hier verifizierbar.

## Wiederaufnahme

```bash
git fetch origin && git checkout -B claude/great-feynman-hi1xfp origin/main   # falls der PR gemergt ist
python3 -I .claude/skills/check-i18n/check_i18n.py .                          # lokale Konventionsprüfung
```

Bauen und testen geht nur auf Windows (CI): `dotnet build IntunePackageBuilder.sln -c Release` und `dotnet test IntunePackageBuilder.sln -c Release`. PR-Ablauf: Skill `pr-flow`; die PR-Freigabe des Repo-Eigentümers steht in `CLAUDE.md`.
