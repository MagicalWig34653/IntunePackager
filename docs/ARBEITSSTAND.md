# Arbeitsstand (Übergabe-Datei)

**Regel: Sessions are disposable.** Diese Datei muss es jeder neuen Session ermöglichen, ohne Rückfrage weiterzuarbeiten. Sie wird in jedem PR aktualisiert. Zuerst lesen: diese Datei, dann `CLAUDE.md`, dann `docs/PLANUNG.md`.

Stand: 2026-10-08

## Aktueller Meilenstein

**M3 - Generatoren** (Erkennungsskript, `IntuneSettings`, HTML, JSON, CSV, Wrapper-Konfiguration). Abnahme: A15, A16, A17. M0 bis M2, M3a und M3b sind abgeschlossen.

Alle Ausgaben entstehen aus einem **Snapshot** (Konfiguration, Build-ID, Sprache, Quellenmanifest), nie aus UI-Zustand. Die Befehle in Anleitung, JSON, CSV und Ergebnisseite stammen aus **einer** Quelle (`IntuneSettings`), damit sie nicht auseinanderlaufen. Sprache der Anleitung folgt der Programmsprache beim Build und wird im Snapshot festgehalten (`docs/PLANUNG.md` §8).

Aufteilung in einzeln mergbare Scheiben (Projekt `IntunePackageBuilder.Generation`, hängt nur von Core ab):

| Scheibe | Inhalt | Stand |
|---|---|---|
| M3a | `BuildSnapshot` (unveränderlicher Snapshot aus Konfiguration, Build-ID, Sprache, Quellen-Fingerabdruck), `IntuneSettings` (Installations- und Deinstallationsbefehl, Rückgabecodes mit Intune-Klassifizierung, Zeitlimit, Neustartverhalten, Erkennungsskript, Anforderungen), JSON- und CSV-Ausgabe mit korrekter Maskierung. Die ersten `.resx`-Ressourcen (neutral Englisch plus `.de.resx`) kommen mit der HTML-Anleitung in M3c, weil erst dort Beschriftungen entstehen | fertig, gemergt (PR 11), CI-Nachweis unten |
| M3b | Erkennungsskript-Generator: eigenständiges Windows-PowerShell-5.1-Skript, UTF-8 mit BOM; MSI: ProductCode in beiden Registry-Ansichten und `DisplayVersion` mindestens Zielversion; EXE: Datei vorhanden und `FileVersion` mindestens Zielversion; Intune-Vertrag (erkannt: Exitcode 0 und nicht leere Standardausgabe; sonst keine Ausgabe); keine Dateien aus dem Intune-Cache; Pester-Tests A15 und A16 (Skript wird im Test über die gebaute `Generation.dll` erzeugt; der Pester-Job in `ci.yml` baut dafür vorher) | fertig, CI-Nachweis unten (PR 12) |
| M3c | (in Arbeit, PR offen, CI-Nachweis steht aus) HTML-Anleitung (lokal lesbar, druckbar, Deutsch und Englisch) mit allen Abschnitten aus Spec §9 (App-Typ, Paketdatei, App-Informationen, Programm, Installationsverhalten, Anforderungen, Zeitlimit, Neustartverhalten, Rückgabecodes, Erkennung, Zuweisung, Abhängigkeiten, Fehleranalyse); Maskierung gegen aktive Inhalte (A17); Warnung vor dem Mischen von `.intunewin` und Erkennungsskript verschiedener Builds; behauptet nie eine erfolgreiche Zuweisung oder Installation | offen |
| M3d | Wrapper-Konfiguration für die Client-Laufzeit (maschinenlesbar, aus demselben Snapshot) und Abnahme von A17 über alle Formate | offen |

Entscheidungen, die M3 braucht: `docs/PLANUNG.md` §8 listet Mindest-Windows-Build (Anforderungen in Intune), Zielarchitektur und die `ALLUSERS`-Frage (MSI-Standardparameter). Ohne Antwort gelten bis dahin diese Annahmen, die im PR zu nennen sind: Anforderung nur Architektur x64 und Windows 10 Version 1607 oder neuer (Mindestwert für Win32-Apps, nur als Platzhalter markiert), Standardinstallation mit `msiexec /i "<msi>" /qn /norestart /l*v "<log>"`.

Abgeschlossen: M1 (Core) und M2 (Quellenanalyse: M2a MSI-Reader, M2b EXE-Reader und Quellenmanifest, M2c sicherer Import).

## Erledigt und verifiziert (CI-Nachweis)

Alle Läufe: Windows-CI mit `windows-latest`, `windows-2022` und Pester unter Windows PowerShell 5.1.

| Stand | CI-Lauf | Ergebnis |
|---|---|---|
| M0: Gerüst, `FileLogger`, Pester-Gerüst, Format-Prüfung | 37810006107 | 8 xUnit, 10 Pester bestanden |
| M1a: Projekt-ID, Versionsnummer, atomares Schreiben, Migration, Projektspeicher, Versionsmodell mit Validierung | 37825409326 | 135 xUnit, 10 Pester bestanden |
| M1b: Sperre je Version (echter Zwei-Prozess-Test), Versionsspeicher, gemeinsamer JSON-Helfer | 37827032442 | 156 xUnit, 10 Pester bestanden |
| M1c: Einstellungen, Grundordner, zuletzt geöffnete Projekte | 37828209771 | 187 xUnit, 10 Pester bestanden |
| M2a: MSI-Reader (echte `msi.dll`, kontrollierte Test-MSIs); M2b: EXE-Reader, Quellenmanifest mit Änderungserkennung (echte Junction) | 37832133738 | 203 xUnit (Core) und 31 xUnit (Analysis), 10 Pester bestanden |
| M2c: sicherer Import (Auswahl, Rekursion, echte Junctions, Pfadlänge, Aufräumen bei Fehler) | 37833609876 | 247 xUnit (Core) und 31 xUnit (Analysis), 10 Pester bestanden, im ersten Lauf ohne Fehler |
| M3a: Snapshot, Intune-Werte, JSON/CSV (nach zwei Korrekturen aus der CI: `InternalsVisibleTo`, Datumsparser im Test) | 37835593601 | 273 xUnit (Core), 31 (Analysis), 33 (Generation), Pester-Job bestanden |
| M3b: Erkennungsskript-Generator (Lauf 1 mit Build-Fehler durch rohe Zeilentrenner, behoben) | 37884553567 | 273 xUnit (Core), 31 (Analysis), 61 (Generation), 44 Pester (davon 34 neue Erkennungstests mit echter Registry und Dateien) bestanden |

Außerdem fertig: Planung, Spezifikation, README (de/en), Icon, Entwurfs-Mockups, Pages-Webseite (deployt), Claude-Code-Umgebung (`CLAUDE.md`, Agents, Skills, Hook). Neue Dateien und Formate: siehe `docs/DATENFORMAT.md`.

## In Arbeit

- M2 ist abgeschlossen und gemergt (PR 10).
- M3a ist gemergt (PR 11, CI-Lauf 37835593601 grün).
- M3b ist in PR 12 abgeschlossen (CI-Lauf 37884553567 grün; vorher Lauf 37884460677 mit Build-Fehler, siehe Lehren).
- M3c (Branch `claude/great-feynman-hi1xfp`): **unverifiziert bis zum CI-Lauf.**
  - Neu in `Generation`: `Guide/{GuideText,HtmlText,GuideGenerator}.cs`, `Resources/GuideStrings.resx` und `GuideStrings.de.resx` (79 Schlüssel); `RequirementsSection.MinimumWindowsVersionIsPlaceholder` und `IntuneSettingsOptions.MinimumWindowsVersionConfirmed`.
  - Neue Tests: `GuideGeneratorTests` (xUnit). Risiko, das die CI klärt: Die deutsche Satelliten-Assembly muss beim Bauen mit `dotnet build` entstehen.
  - Doku: `docs/GENERATOREN.md` Abschnitt „HTML-Anleitung (M3c)“.

## Lehren (für künftige Sessions)

- **Die CI findet echte Fehler.** M1a: Newtonsoft hängt Listen beim Laden an Konstruktor-Standards an (`[0]` wurde `[0, 0]`); behoben mit `ObjectCreationHandling.Replace`. M1b: der Kindprozess im Sperr-Test fand Newtonsoft.Json nicht, weil xUnit Assemblies in einen Shadow-Copy-Ordner kopiert; der Test kopiert die DLLs jetzt aus dem Originalordner (`CodeBase`).
- **Der Edit/Write-Hook greift nicht bei Dateien, die per Bash-Heredoc entstehen.** Danach immer `python3 -I .claude/skills/check-i18n/check_i18n.py .` laufen lassen. Umlaute im C#-Code als `\u00e4`-Escapes schreiben; mein Schreibwerkzeug löst Escapes manchmal schon beim Schreiben zu echten Zeichen auf.
- **Zeilentrenner (U+2028/U+2029) und Steuerzeichen im C#-Quelltext bauen den Compiler aus** (CS1010 in M3b-CI-Lauf 1): Das Schreibwerkzeug löst `\u2028`-Escapes zu echten Zeichen auf. `check_i18n.py` meldet solche Zeichen jetzt; nach dem Schreiben immer ausführen.
- **`Assert.True(false, ...)` löst xUnit2020 aus** und bricht den Build (Warnungen sind Fehler); `Assert.Fail(...)` verwenden.
- **`JObject.Parse` formatiert datumsähnliche Texte um**; deshalb `JsonFormat.ParseObject` benutzen (Notizen bleiben unverändert).
- **Die Sperrdatei hat der Halter schreibend offen**; zum Lesen `FileShare.ReadWrite | FileShare.Delete` angeben.
- Chromium (Render-Skript) stürzt bei langem `TMPDIR` ab; das Skript gibt dem Browser ein kurzes Temp-Verzeichnis.
- Der Abruf der Pages-Seite aus der Cloud-Sitzung ist durch die Netzwerkrichtlinie gesperrt (Host `magicalwig34653.github.io`); der Deploy-Status ist über die Actions-Läufe prüfbar.

## Nächste Schritte (in Reihenfolge)

1. M3c: CI auswerten, Nachweis eintragen, mergen. Danach M3d (siehe oben), jede Scheibe als eigener PR mit Doku im selben PR (`docs/GENERATOREN.md` erweitern: Erkennungsvertrag, Anleitung, Wrapper-Konfiguration).
3. Danach M4 (Build-Pipeline mit dem Content Prep Tool) und M5 (Client-Laufzeit); beide können nach M3 parallel laufen (siehe `docs/PLANUNG.md` §4). Das Content Prep Tool (`tools/`) braucht Herkunft und SHA-256 in `THIRD-PARTY.md`, bevor es verwendet wird.
4. M6 (Oberfläche), M7 (erweiterter Modus, Projektansicht, Update-Ablauf), M8 (Distribution, Prüfprotokoll). Geräte- und Pilot-Tests nach Spec §12.2 und eine Signierung lassen sich in der Cloud-Sitzung nicht durchführen und müssen am Ende ausdrücklich als offen ausgewiesen werden.

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
