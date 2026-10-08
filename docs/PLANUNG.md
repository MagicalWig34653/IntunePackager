# Planung: Intune Package Builder

Grundlage: [SPEC.md](SPEC.md) (Version 1.0). Dieses Dokument übersetzt die Spezifikation in Architektur, Meilensteine und Teststrategie. Es ist ein Entwurf und wird mit jeder Entscheidung fortgeschrieben.

## 1. Eckpunkte

| Thema | Entscheidung (Vorschlag) | Begründung |
|---|---|---|
| Autorentool | C# / WPF auf .NET Framework 4.8 | Referenzarchitektur der Spec; auf Windows 11 und Server 2022 vorinstalliert, kein Runtime-Setup, kein WinUI 3 |
| UI-Muster | MVVM, ohne schweres Framework (nur `INotifyPropertyChanged`, eigene `RelayCommand`) | Wenige Abhängigkeiten, Spec §11 verlangt strikte Trennung UI / Fachmodell |
| MSI lesen | P/Invoke auf `msi.dll` (`MsiOpenDatabase` im Read-only-Modus, Tabelle `Property`) | Liest nur die Datenbank, führt keine Custom Actions aus (A02) |
| EXE-Metadaten | `FileVersionInfo` | Reine Dateiinformation (§7.1) |
| Pakettool | Microsoft Win32 Content Prep Tool (`IntuneWinAppUtil.exe`, MIT) | Pflicht laut §3; Version und SHA-256 im Abhängigkeitsmanifest |
| Client-Wrapper | Windows PowerShell 5.1, 64-Bit, UTF-8 mit BOM | §4; Vorlagen liegen als Dateien in `deploy/` |
| Benutzerinteraktion | PSAppDeployToolkit, feste Version, unverändert (Vendor-Ordner) | §4/§8.2; Version wird in M5 anhand Server 2022 / Win 11 festgelegt |
| Konfiguration | JSON mit `schemaVersion`; atomares Schreiben (Temp-Datei + `File.Replace`) | §6.3, kein halbgeschriebenes `project.json` |
| Code language | English only: identifiers, comments, log messages, commit messages, config and workflow files | Project decision; documentation under `docs/` stays German |
| UI language | German and English via .resx resources, follows the program language (default: Windows UI language, switchable in settings) | Project decision; no user-visible string is hard-coded in code |
| Generated output language | Intune guide, deployment texts and user notices follow the program language active at build time and are stored in the build snapshot | Keeps a build reproducible regardless of later language changes |
| Tests | xUnit (C#), Pester 5 (PowerShell), FlaUI (UI-Abläufe) | A01–A18, §12.1 |
| CI | GitHub Actions auf `windows-latest` (+ `windows-2022`) | WPF und Pester lassen sich hier nicht bauen; siehe §6 |

## 2. Lösungsstruktur

```text
IntunePackager/
  docs/                      SPEC, Planung, später Bedienungsanleitung, Datenformat, Prüfprotokoll
  src/
    IntunePackageBuilder.App/          WPF-Oberfläche, ViewModels, Navigation, Moduswechsel
    IntunePackageBuilder.Core/         Domänenmodell, Projektspeicher, Schema, Migration, Sperren, Logging
    IntunePackageBuilder.Analysis/     MSI-Reader (msi.dll), EXE-Metadaten, Quellenmanifest
    IntunePackageBuilder.Build/        Build-Dienst, Staging, Content-Prep-Runner, Veröffentlichung
    IntunePackageBuilder.Generation/   Detection-Generator, HTML/JSON/CSV-Generator, Wrapper-Konfiguration
  deploy/
    template/                          Install.cmd, Deploy-Wrapper.ps1, Hilfsmodule (Client-Laufzeit)
    vendor/                            PSAppDeployToolkit (unverändert, mit Herkunft/Prüfsumme)
  tools/                               IntuneWinAppUtil.exe + Manifest (Version, Quelle, SHA-256, Lizenz)
  tests/
    *.Tests/                           xUnit je Modul
    pester/                            Pester-Tests für Wrapper und Detection
    ui/                                FlaUI-Abläufe
```

Abhängigkeitsrichtung: `App → Build, Generation, Core`; `Build → Analysis, Generation, Core`; `Analysis, Generation → Core`. Core kennt weder WPF noch Dateiformate der Generatoren. Das entspricht der Trennung in Spec §11.

## 3. Kernentwürfe

### 3.1 Domänenmodell
- `Project` (`project.json`): `schemaVersion`, `projectId`, `displayName`, `createdUtc`, `notes`.
- `PackageVersion` (`configuration.json`): Identität, Quelle, Installation, Deinstallation, Erkennung, Interaktion, Nacharbeiten, Laufzeit (Tabelle Spec §6.3). Der Installertyp (MSI/EXE) ist ein Diskriminator; EXE-Pflichtfelder sind im Typ als „fehlt“ modelliert, nicht mit Defaults gefüllt (A03).
- `BuildRecord`: `buildId`, Zeitstempel, Werkzeugversionen, Hash des Konfigurationssnapshots, Hash des Quellenmanifests. Snapshot und Manifest sind nach dem Schreiben unveränderlich.
- Softwareversion (Zielzustand) und Build-ID sind getrennte Felder und werden nie vermischt (§6.4). Versionen werden numerisch verglichen, nicht als Zeichenkette.

### 3.2 Projektspeicher und Sperren
- Schreiben immer atomar. Unbekannte höhere `schemaVersion` → Schreibschutz mit Meldung, nie Überschreiben. Migration legt vorher eine Sicherung ab.
- Sperre je Projektversion: `.lock`-Datei mit exklusivem `FileStream` (`FileShare.None`) plus Metadaten (PID, Rechner, Zeit) für die Anzeige. Eine verwaiste Sperre (Prozess existiert nicht mehr) ist kontrolliert auflösbar (A14).
- Pfadlängen: kurzes Arbeitsverzeichnis (z. B. `%LOCALAPPDATA%\Intune Package Builder\work\<kurz-id>`); Pfade mit `\\?\`-Präfix, wo nötig. Vor dem Kopieren wird die längste Zielpfadlänge geprüft.

### 3.3 Build-Pipeline
Zehn Schritte aus Spec §7.2 als Folge einzelner, testbarer `IBuildStep`s mit gemeinsamem `BuildContext`. Ausführung im Hintergrund (`Task`), Fortschritt über `IProgress<BuildPhase>`, Ergebnis als `BuildResult` mit Build-ID und Snapshot. Veröffentlichung (Schritt 9) in ein `…\builds\<id>.tmp`-Verzeichnis und anschließend per Umbenennung — so entsteht nie ein halbfertiges Ergebnis. Aufräumen löscht nur Verzeichnisse mit einem vom Build geschriebenen Markerfile.

### 3.4 Sichere Dateiverarbeitung
Kanonisierung aller Pfade, Prüfung auf Verbleib im Quellordner, Reparse-Point-Erkennung (Junction/Symlink) beim Import, Zyklusprüfung (Quelle enthält Projekt/Ziel/Arbeitsordner). Alle Prüfungen laufen vor dem ersten Kopiervorgang (A12). Prozessstart immer mit `ProcessStartInfo.ArgumentList`-äquivalenter, sauber maskierter Argumentliste, nie über `cmd /c`.

### 3.5 Generatoren
Alle Ausgaben entstehen aus dem Snapshot, nicht aus UI-Zustand. Je Format ein eigener Escaper (JSON-Serializer, PowerShell-Single-Quote-Literale, HTML-Encoding, CSV-Quoting). Die Befehle in Anleitung, JSON, CSV und Ergebnisseite stammen aus **einer** Quelle (`IntuneSettings`), damit sie nicht auseinanderlaufen. Das Detection-Skript ist vollständig eigenständig (keine Datei aus dem Intune-Cache).

### 3.6 Client-Laufzeit (Zielgerät)
`Install.cmd` startet den nativen 64-Bit-PowerShell-Prozess (`%SystemRoot%\Sysnative` falls 32-Bit-Einstieg). Der Wrapper liest die mitgelieferte Konfiguration, ruft Installer mit überprüften Parametern auf, verfolgt den Prozessbaum bis zum echten Abschluss, prüft den Zielzustand und mappt Rückgabecodes (0 / 3010 / 1618 / konfigurierbar; 1641 nie als stiller Erfolg). Benutzerhinweise laufen über die Sitzungsvermittlung des Toolkits. Fehlende Benutzersitzung, mehrere Sitzungen und Parallelinstallation werden in M5 als Tabelle spezifiziert und mit Pester bzw. Gerätetests belegt.

### 3.7 Oberfläche
Fenster mit Navigation Start → Eingabe → Build → Ergebnis / Projektansicht. Hauptaktion „Paket erstellen“ bleibt fixiert sichtbar (1366×768, Skalierung bis 200 %). Validierung am Feld mit Fokussprung. Während des Builds sind Eingaben und Projektwechsel gesperrt; das Schließen fragt nach. Steuerelementreferenzen halten nie Fachwerte (§11) — Zustand lebt ausschließlich im ViewModel.

## 4. Meilensteine

Jeder Meilenstein ist erst abgeschlossen, wenn seine automatisierten Tests grün sind. Die Abnahme-IDs verweisen auf Spec §12.1.

| M | Inhalt | Abnahme | Ergebnis |
|---|---|---|---|
| **M0** | Repo-Grundlage: Solution, CI auf Windows, Lint/Format, Abhängigkeitsmanifest-Gerüst, Logging-Grundlage | – | Leeres, baubares Gerüst; CI grün |
| **M1** | Core: Modelle, Schema, atomares Speichern, Migration, Projekt-IDs, Sperren, Grundordner | A03 (Modell), A05, A06, A14 | Projekte lassen sich anlegen, laden, sperren |
| **M2** | Analysis: MSI-Reader, EXE-Metadaten, Quellenmanifest, Importprüfungen | A02, A04 (Logik), A11, A12 | Quellen werden sicher analysiert und gesichert |
| **M3** | Generation: Detection, `IntuneSettings`, HTML/JSON/CSV, Wrapper-Konfiguration | A15, A16, A17 | Alle Artefakte eines Builds aus einem Snapshot erzeugbar |
| **M4** | Build: Pipeline, Content-Prep-Runner, Veröffentlichung, Fehlerpfade | A09 (Logik), A10, A11, A13 | Echte `.intunewin` aus Testquelle |
| **M5** | Client-Laufzeit: Wrapper, Rückgabecodes, Interaktion, Nacharbeiten, Pester-Tests | A15, A16 | Wrapper auf Test-VM lauffähig |
| **M6** | App-UI: Start, Standardmodus MSI/EXE, Drag-and-drop, Validierung, Ergebnisseite | A01, A03, A04, A08, A09 | Vollständiger Standardablauf |
| **M7** | Erweiterter Modus, Projektansicht, Update-Ablauf, Notizen | A05–A08 | Updates und Versionsverwaltung |
| **M8** | Distribution und Abnahme: ZIP-Bau, Prüfsummen, Doku, Prüfprotokoll, Gerätetests | A18, §12.2 | Abnahmefähiges Release |

Reihenfolge: M0 → M1 → M2 → M3 sind sequenziell; M4 und M5 können nach M3 parallel laufen; M6 beginnt, sobald M1/M2 Schnittstellen stehen (gegen Fakes), und wird nach M4 integriert.

## 5. Teststrategie

- **Unit/Integration (xUnit):** Schema, Migration, Pfadprüfungen, Versionsvergleich, Escaping, Pipeline-Schritte mit Fake-Dateisystem wo möglich, sonst Temp-Verzeichnisse.
- **Kontrollierte MSI-Datenbanken:** Testhelfer erzeugt minimale MSI-Dateien per `msi.dll` (nur Property-Tabelle) — keine Herstellerinstaller, keine Custom Actions (A02).
- **Packwerkzeug:** Ein Test nutzt das echte Content Prep Tool mit inerter Datei; Fehlerfälle (Exitcode ≠ 0, fehlende Datei) über einen Fake-Prozess.
- **Pester 5:** Detection-Skript (älter / passend / neuer / fremd / ohne Cache), Rückgabecodemapping, Nacharbeiten-Whitelist.
- **UI (FlaUI):** Echte Abläufe A01, A03–A09; bei 100/125/150/200 % und 1366×768.
- **Gerätetests (manuell, M8):** Spec §12.2; Ergebnisse im Prüfprotokoll. Lokale Tests gelten nie als Nachweis einer Intune-Verteilung.

## 6. Entwicklungsumgebung und Einschränkungen

Diese Cloud-Sitzung läuft unter Linux ohne .NET-SDK, Mono oder PowerShell. WPF, Windows-Installer-API, Content Prep Tool und Pester sind hier nicht ausführbar. Folgen:

- Bau und Tests laufen ausschließlich in der **CI auf Windows-Runnern**; lokal verifizierbar sind hier nur Textdateien (Docs, JSON-Schemas, Skripte per Syntaxprüfung, falls ein Parser verfügbar ist).
- Code, der in dieser Sitzung entsteht, ist bis zum ersten CI-Lauf **unverifiziert** und wird so gekennzeichnet.
- Empfehlung: M0 zuerst, damit CI früh Rückmeldung gibt.

## 7. Risiken

| Risiko | Gegenmaßnahme |
|---|---|
| Sitzungsvermittlung aus SYSTEM (Session 0) ist fehleranfällig | PSADT einsetzen statt Eigenbau; Matrix der Sitzungszustände früh in M5 testen |
| Bootstrapper-EXE beendet sich früh, Kindprozess installiert weiter | Prozessbaum-/Job-Object-Überwachung, Abschluss nur über Zielzustandsprüfung |
| Windows-Pfadlängen im Herstellerordner | Kurzes Arbeitsverzeichnis, Vorabprüfung, `\\?\`-Pfade |
| Nicht signierte Ausgabe wirkt signiert | Signaturstatus wird real geprüft und in Doku/Manifest ausgewiesen |
| Fremd-/Neuerprodukt wird bei Deinstallation entfernt | Deinstallation nur über ProductCode bzw. konfigurierten Pfad, keine Namenssuche |
| Spec-Umfang größer als eine Iteration | Meilensteine liefern jeweils testbare Teilergebnisse; M6/M7 notfalls schneiden, nicht M2–M5 |

## 8. Entscheidungen und offene Punkte

Entschieden:

1. **Produktname:** „Intune Package Builder“ bleibt.
2. **UI-Sprache:** Deutsch und Englisch, je nach Programmsprache (.resx-Ressourcen).
3. **Sprache der erzeugten Anleitung und Benutzerhinweise:** folgt der Programmsprache beim Build; die Sprache wird im Snapshot festgehalten.
4. **Quellcode:** vollständig Englisch (Bezeichner, Kommentare, Logmeldungen, Konfiguration). Sichtbare Texte nur über Ressourcen.

Offen (vor M5 bzw. M8 zu klären):

1. **PSADT-Version:** 4.x (aktuell, PS-5.1-kompatibel) oder 3.x – Festlegung nach Test auf Server 2022 und Win 11.
2. **Zielgeräte-Betriebssystem in der Intune-Anforderung:** Mindest-Windows-Build – die Spec verlangt „für das konkrete Paket festgelegte OS-Anforderungen“.
3. **Signierung:** Gibt es ein Code-Signing-Zertifikat für Autorentool und/oder Skripte? Sonst bleibt der Status „nicht signiert“.
4. **Zielarchitektur:** Nur x64-Zielgeräte, oder auch x86-Intune-Geräte?
5. **Test-Infrastruktur:** Stehen Windows-11- und Server-2022-VMs sowie ein Intune-Pilot-Tenant für M8 zur Verfügung?

## 9. Nächste Schritte

Erledigt: M0-Gerüst mit Windows-CI (PR 2), README, Icon, Entwurfs-Mockups, Pages-Seite und Claude-Code-Umgebung (PR 3).

1. M0 abschließen: Test-Gerüst für Pester, Lint/Format und Logging-Grundlage ergänzen.
2. M1 beginnen (Core + Tests), danach M2.
