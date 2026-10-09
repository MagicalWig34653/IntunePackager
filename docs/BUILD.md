# Build-Pipeline

Beschreibt, wie aus einer Softwareversion ein Paket entsteht (Spec §7.2). Das Dokument wächst mit den Scheiben von M4 (M4a Packwerkzeug und Prozessstart, M4b Arbeitsverzeichnis und Pipeline, M4c Veröffentlichung, Fehlerpfade und Abnahme A09, A10, A11, A13) und wird im selben PR wie der Code gepflegt.

## Externe Prozesse (M4a)

Externe Programme werden **nie** über eine Shell-Zeichenkette gestartet (Spec §10):

- `ArgumentQuoter` baut aus einer Argumentliste die Befehlszeile nach den Regeln von `CommandLineToArgvW`, sodass jedes Argument unverändert beim Kindprozess ankommt (Leerzeichen, Anführungszeichen, Backslashes vor Anführungszeichen, abschließende Backslashes). Ein Test liest die erzeugten Befehlszeilen mit dem Windows-Parser selbst zurück.
- `ProcessRunner` startet ohne Shell, mit umgeleiteter und abgesaugter Ausgabe, **geschlossener Standardeingabe** (ein Programm, das etwas erfragen will, endet, statt zu warten) und Zeitlimit; nach Ablauf wird der Prozess beendet. Die erfasste Ausgabe ist auf etwa 1 MB je Strom begrenzt.

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
