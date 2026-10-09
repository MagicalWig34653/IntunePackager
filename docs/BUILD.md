# Build-Pipeline

Beschreibt, wie aus einer Softwareversion ein Paket entsteht (Spec §7.2). Das Dokument wächst mit den Scheiben von M4 (M4a Packwerkzeug und Prozessstart, M4b Arbeitsverzeichnis und Pipeline, M4c Veröffentlichung, Fehlerpfade und Abnahme A09, A10, A11, A13) und wird im selben PR wie der Code gepflegt.

## Externe Prozesse (M4a)

Externe Programme werden **nie** über eine Shell-Zeichenkette gestartet (Spec §10):

- `ArgumentQuoter` baut aus einer Argumentliste die Befehlszeile nach den Regeln von `CommandLineToArgvW`, sodass jedes Argument unverändert beim Kindprozess ankommt (Leerzeichen, Anführungszeichen, Backslashes vor Anführungszeichen, abschließende Backslashes). Ein Test liest die erzeugten Befehlszeilen mit dem Windows-Parser selbst zurück.
- `ProcessRunner` startet ohne Shell, mit umgeleiteter und abgesaugter Ausgabe, **geschlossener Standardeingabe** (ein Programm, das etwas erfragen will, endet, statt zu warten) und Zeitlimit; nach Ablauf wird der Prozess **samt seinen Kindprozessen** beendet (`taskkill /T /F`), weil ein Startprogramm oder eine Batchdatei sonst Kinder zurücklässt, die die Ausgabeleitungen offen halten; das Warten darauf ist danach begrenzt (CI-Fund, siehe `docs/ARBEITSSTAND.md`). Die erfasste Ausgabe ist auf etwa 1 MB je Strom begrenzt.

## Content Prep Tool (M4a)

Das Win32 Content Prep Tool (`IntuneWinAppUtil.exe`) wird **nicht mitgeliefert**: Seine Lizenz verbietet das Teilen und Veröffentlichen (Abschnitt 4e); Herkunft, Version und SHA-256 stehen in `THIRD-PARTY.md`, die Entscheidung in `docs/PLANUNG.md` §8 Nr. 7. Das Autorentool erhält den Pfad zum Tool.

`ContentPrepTool.Pack` (hinter der Schnittstelle `IContentPrepRunner`, damit die Pipeline ohne das echte Werkzeug testbar ist):

1. Prüft Werkzeugpfad, Setup-Ordner und Setup-Datei (Fehlercodes `ToolMissing`, `InputMissing`).
2. Berechnet die **SHA-256** des Werkzeugs und vergleicht sie mit den bekannten Versionen (`ContentPrepTool.KnownVersions`, derzeit 1.8.7). Eine unbekannte Datei wird abgewiesen (`ToolNotRecognized`, die Meldung nennt die SHA-256), außer der Benutzer hat die Verwendung ausdrücklich bestätigt (`AllowUnknownTool`).
3. Startet `IntuneWinAppUtil.exe -c <Ordner> -s <Datei> -o <Ausgabe> -q` mit einer Argumentliste (`-q`: keine Rückfragen). Zeitlimit standardmäßig 30 Minuten (`ToolTimedOut`), Exitcode ungleich 0 (`ToolFailed`, mit Ausgabe), Programm nicht startbar (`ToolNotStartable`).
4. Prüft die erzeugte Datei `<Setup-Dateiname ohne Endung>.intunewin` mit `IntunewinVerifier`: vorhanden (`OutputMissing`), nicht leer, ZIP-Container mit den Einträgen `IntuneWinPackage/Contents/IntunePackage.intunewin` und `IntuneWinPackage/Metadata/Detection.xml`, beide nicht leer (`OutputInvalid`). Der verschlüsselte Inhalt selbst lässt sich nicht prüfen.

Fehler sind `ContentPrepException` mit Code und technischem Detail; Anzeigetexte entstehen später in der Oberfläche aus Ressourcen.

### Tests und Lücken

- xUnit (`IntunePackageBuilder.Build.Tests`): Anführungsregeln, Prozessstart (Exitcode, Ausgabe, Zeitlimit, geschlossene Eingabe), Verifizierer, Packwerkzeug gegen ein **Fake-Werkzeug** (Batchdatei mit den Argumenten des echten Werkzeugs).
- Integrationstests gegen das **echte Werkzeug** laufen nur, wenn `IPB_CONTENT_PREP_TOOL` auf `IntuneWinAppUtil.exe` zeigt; die CI lädt es dafür vom Microsoft-Repository (festgelegter Commit) und bricht ab, wenn die SHA-256 nicht stimmt. Ohne die Variable werden sie als übersprungen gemeldet, nicht als bestanden.
- Nicht geprüft: ob Intune das erzeugte Paket annimmt (Gerätetest, M8).

## Pipeline (M4b)

`BuildPipeline.Run(request, progress, cancellation)` (und `RunAsync` für die Oberfläche: läuft auf einem Arbeitsthread, die Oberfläche bleibt bedienbar) baut ein Paket aus einer gespeicherten Version. Die Schritte laufen in dieser Reihenfolge und werden als `BuildPhase` gemeldet; **einen Prozentwert gibt es nicht**, weil keiner der Schritte verlässlich messbar ist (Spec §7.2):

| Phase | Was geschieht | Fehlercode (`BuildProblem`) |
|---|---|---|
| `ValidateConfiguration` | Anfrage, Konfiguration (`ConfigurationValidator`) und Laufzeitvorlagen (`Install.cmd`) prüfen | `RequestInvalid`, `ConfigurationInvalid`, `TemplateMissing` |
| `AcquireLock` | Sperre der Version (`VersionLock`), Build-Ordner anlegen und per Schreibprobe prüfen, unfertige Ordner früherer Abbrüche entfernen, Build-ID wählen | `VersionLocked`, `NotWritable` |
| `VerifySource` | gespeicherte Quelle gegen `source-manifest.json` prüfen (nichts fehlt, nichts neu, nichts verändert), Installer muss Teil des Manifests sein, freien Platz prüfen | `SourceMissing`, `SourceChanged`, `NotEnoughSpace` |
| `PrepareWorkspace` | kurzes Arbeitsverzeichnis `<WorkRoot>\<build-id>` mit Markerdatei anlegen | `NotWritable` |
| `StagePackage` | Laufzeitvorlagen in die Paketwurzel, die gespeicherten Quelldateien (genau die Dateien des Manifests) nach `Files\`; Pfadlänge vor jedem Kopieren prüfen | `PathTooLong` |
| `GenerateArtifacts` | `BuildSnapshot` bilden, alle Textdateien daraus erzeugen (`BuildArtifacts`): Wrapper-Konfiguration ins Paket, die vier `intune`-Dateien daneben | `Unexpected` |
| `PackContent` | Content Prep Tool über `IContentPrepRunner`; Setup-Datei ist `Install.cmd` | `PackagingFailed` |
| `VerifyPackage` | Ergebnis in `<projekt-id>.intunewin` umbenennen und die Struktur erneut prüfen | `PackagingFailed` |
| `Publish` | im Ordner `builds` zuerst `<build-id>.partial` füllen (Paket, `intune`, Snapshot, `build-manifest.json`), jede Kopie mit SHA-256 gegen das Original prüfen, dann in **einem Schritt** in `<build-id>` umbenennen | `PublishFailed` |
| `Cleanup` | Arbeitsverzeichnis entfernen, `build.log` schreiben | |

Ein Abbruch (`CancellationToken`) wird zwischen den Schritten geprüft (`Cancelled`); mitten im Content Prep Tool bricht er nicht ab (Spec §7.2: nur sauber abgegrenzte Schritte).

### Zusagen

- **Nichts Halbes wird veröffentlicht.** Das Ergebnis erscheint erst mit dem Umbenennen von `.partial`. Bei jedem Fehler wird der eigene `.partial`-Ordner entfernt; Ordner aus abgebrochenen früheren Läufen räumt der nächste Build unter der Sperre auf, aber **nur mit Markerdatei** `.ipb-partial`.
- **Aufräumen löscht nur, was der Build selbst angelegt hat.** Das Arbeitsverzeichnis trägt die Markerdatei `.ipb-workspace` mit der Build-ID und wird nur bei passendem Inhalt gelöscht; fremde Ordner im Arbeitsordner, Benutzerdateien und die gespeicherte Quelle bleiben unberührt (Spec §7.3).
- **Frühere Builds bleiben unverändert.** Jeder Build hat eine neue Build-ID (auch bei gleicher Uhrzeit, mit Prüfung auf Kollision); ein erneuter Build derselben Version überschreibt nichts (A10).
- **Eine veränderte Quelle wird erkannt** und nie stillschweigend verwendet (A11); die Prüfung läuft vor jeder Verwendung.
- **Gleichzeitige Builds derselben Version sind ausgeschlossen** (Sperre); andere Versionen laufen unabhängig (A09, Logik).
- **Fehler enthalten Phase, Code und Detail**, nie Anzeigetext. Zu jedem fehlgeschlagenen Build mit Build-ID entsteht `builds/<build-id>.failed.log`; erfolgreiche Builds haben `build.log` im Build-Ordner (Phase, Zeit, Build-ID, Werkzeugversion und SHA-256).

### Ablage eines Builds

```text
builds/<build-id>/
  <projekt-id>.intunewin
  intune/  Detect-App.ps1, Einrichtung.html, Einstellungen.json, Einstellungen.csv
  configuration.snapshot.json
  build-manifest.json
  build.log
```

`build-manifest.json`: siehe `docs/DATENFORMAT.md`. Es listet das Paket und jede weitere Datei des Build-Ordners (außer sich selbst und `build.log`) mit Größe und SHA-256 sowie die Quell-Fingerabdruck, Softwareversion, Toolversion und die SHA-256 des verwendeten Content Prep Tools.

### Tests und Lücken

`BuildPipelineTests` (xUnit, mit einem Fake-Packwerkzeug, das die Staging-Inhalte aufzeichnet): Erfolgspfad und Ablage, Inhalt des Pakets, Manifest gegen die Dateien, Snapshot, A10 (zweiter Build, gleicher Zeitpunkt), A11 (verändert, hinzugefügt, fehlend, kein Manifest), A09-Logik (Phasenreihenfolge, zweiter Build derselben Version gesperrt, `RunAsync` kehrt sofort zurück, andere Version läuft), Abbruch, A13 (Werkzeugfehler, ungültiges Paket, nicht beschreibbarer Build-Ordner, zu wenig Platz, zu lange Pfade, fehlende Vorlagen), Aufräumen nur mit Marker. Ein Test baut mit dem **echten** Werkzeug, wenn `IPB_CONTENT_PREP_TOOL` gesetzt ist.

**Nicht geprüft:** Die Sperre der Oberfläche während des Builds (M6), Abbruch mitten im Werkzeug, ein tatsächlich volles Laufwerk (der Platz wird über eine austauschbare Messung geprüft; `ERROR_DISK_FULL` wird auf `NotEnoughSpace` abgebildet, aber nicht real provoziert) und Pfade über 259 Zeichen mit aktivierter Langpfad-Unterstützung.

