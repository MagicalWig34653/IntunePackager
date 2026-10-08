# Arbeitsstand (Übergabe-Datei)

**Regel: Sessions are disposable.** Diese Datei muss es jeder neuen Session ermöglichen, ohne Rückfrage weiterzuarbeiten. Sie wird in jedem PR aktualisiert. Zuerst lesen: diese Datei, dann `CLAUDE.md`, dann `docs/PLANUNG.md`.

Stand: 2026-10-08

## Aktueller Meilenstein

**M1 - Core** (Projektmodell, Schema, atomares Speichern, Migration, Projekt-IDs, Sperren, Grundordner). Abnahme: A03 (Modell), A05, A06, A14.

Aufteilung in einzeln mergbare Scheiben:

| Scheibe | Inhalt | Stand |
|---|---|---|
| M1a | Projekt-ID, Versionsnummer, atomares Schreiben, Projekt- und Versionsmodell, Validierung, Schema-Migration, Projektspeicher (Anlegen, Laden, Notizen, Liste) | fertig, CI grün (Lauf 37825409326, 135 xUnit-Tests bestanden), PR 6 zum Merge bereit |
| M1b | Sperre je Projektversion (inkl. Zwei-Prozess-Test), Versionsspeicher (Anlegen, Laden, Speichern, Liste nach numerischer Version), gemeinsamer Helfer für versioniertes JSON | fertig, CI grün (Lauf 37827032442, 156 xUnit-Tests bestanden, darunter der echte Zwei-Prozess-Test), PR 7 zum Merge bereit |
| M1c | Einstellungen des Autorentools, Grundordner (nicht unbemerkt ersetzen, Schreibrechte prüfen), zuletzt geöffnete Projekte | Code und Tests geschrieben, PR offen, CI-Nachweis steht aus |

## Erledigt und verifiziert (CI-Nachweis)

- M1b: Sperre je Projektversion (mit echtem Zwei-Prozess-Test), Versionsspeicher, gemeinsamer Helfer für versioniertes JSON. CI-Lauf 37827032442 (windows-latest, windows-2022, Pester): 156 xUnit-Tests und 10 Pester-Tests bestanden. Der erste Lauf fand ein Testproblem (Kindprozess fand Newtonsoft.Json nicht wegen Shadow-Copy), behoben.

- M1a: Projekt-ID, Versionsnummer, atomares Schreiben, Schema-Migration, Projektspeicher, Versionsmodell mit Validierung. CI-Lauf 37825409326 (windows-latest, windows-2022, Pester): 135 xUnit-Tests und 10 Pester-Tests bestanden. Ein Fehler wurde im ersten Lauf gefunden und behoben (Listen wurden beim Laden verdoppelt).

- M0 komplett: Solution, Windows-CI (windows-latest und windows-2022), `FileLogger` mit Tests (.NET 8/8), Pester-Gerüst (10/10), Format-Prüfung. Nachweis: CI-Lauf 37810006107.
- Planung, Spezifikation, README (de/en), Icon, Entwurfs-Mockups, Pages-Webseite, Claude-Code-Umgebung (`CLAUDE.md`, Agents, Skills, Hook).

## In Arbeit

- M1a ist durch CI verifiziert und gemergt (PR 6).
- M1b ist durch CI verifiziert und gemergt (PR 7).
- M1c (Branch `claude/great-feynman-hi1xfp`): Code unverifiziert bis zum CI-Lauf (kein .NET-SDK in der Cloud-Sitzung).
  - Neu: `Settings/AppSettings.cs`, `Settings/SettingsStore.cs`, `Settings/BaseFolderResolver.cs`.
  - Tests: `AppSettingsTests`, `SettingsStoreTests`, `BaseFolderResolverTests`.
  - Doku: `docs/DATENFORMAT.md` (settings.json, Grundordner-Status).
  - Neu: `Storage/VersionedJsonFile.cs` (gemeinsamer Helfer, `ProjectStore` nutzt ihn jetzt), `Storage/VersionLock.cs`, `Versions/VersionStore.cs`, `Versions/VersionExceptions.cs`.
  - Tests: `VersionLockTests` (inkl. echter Zwei-Prozess-Test mit Windows PowerShell 5.1, deckt A14 ab), `VersionStoreTests`.
  - Doku: `docs/DATENFORMAT.md` (Versionsordner, Sperre).
  - CI-Lauf 1 von PR 7: Build fehlerfrei (0 Warnungen), 155 von 156 Tests grün. Der eine Fehlschlag war ein Testproblem: Der Kindprozess fand Newtonsoft.Json nicht, weil xUnit Assemblies in einen Shadow-Copy-Ordner kopiert und `Assembly.Location` dorthin zeigt. Behoben, indem der Test die DLLs aus dem Originalordner (CodeBase) in einen eigenen Ordner für den Kindprozess kopiert.
  - Neu in `src/IntunePackageBuilder.Core`: `Projects/` (ProjectId, Project, ProjectStore, Ausnahmen), `Storage/` (AtomicFile, JsonFormat, SchemaMigrator, Ausnahmen), `Versions/` (VersionNumber, PackageVersionConfig, ConfigurationValidator).
  - Tests in `tests/IntunePackageBuilder.Core.Tests` (ProjectId, VersionNumber, AtomicFile, SchemaMigrator, ProjectStore, ConfigurationValidator, JSON-Roundtrip).
  - Doku dazu: `docs/DATENFORMAT.md`, `THIRD-PARTY.md` (Newtonsoft.Json), Entscheidungen in `docs/PLANUNG.md` §8.
  - CI-Lauf 1 von PR 6: Build fehlerfrei (0 Warnungen), 132 von 133 Tests grün. Fehler war echt: Newtonsoft hängt Listen beim Laden an Konstruktor-Standards an (`[0]` wurde `[0, 0]`). Behoben mit `ObjectCreationHandling.Replace` plus Regressionstests.
  - Lehre: Der Edit/Write-Hook greift nicht bei Dateien, die per Bash-Heredoc entstehen. Nach solchen Schreibvorgängen immer `check_i18n.py` laufen lassen (hat hier echte Umlaute in drei C#-Dateien gefunden; Umlaute im Code als `\u00e4`-Escapes schreiben).

## Nächste Schritte (in Reihenfolge)

1. M1a fertigstellen, CI grün, mergen.
2. M1c mergen. Danach M1 abschließen (Skill `milestone`): Roadmaps in READMEs und Webseite auf M1 fertig und M2 als Nächstes, Matrix prüfen.
3. M1 abschließen: `docs/ABNAHME-MATRIX.md` (A03 Modell, A05, A06, A14 mit Nachweis; A14 ist zunächst nur im selben Prozess geprüft, ein Mehrprozess-Test fehlt noch), Roadmaps in READMEs und Webseite (Skill `milestone`).
4. M2 beginnen (Quellenanalyse: MSI-Reader, EXE-Metadaten, Quellenmanifest, sicherer Import).

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
