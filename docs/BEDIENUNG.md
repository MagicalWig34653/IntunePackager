# Bedienungsanleitung

Diese Anleitung beschreibt, was die Version 0.0.1 des Intune Package Builders heute kann. Sie behauptet nichts, was nicht umgesetzt ist; was fehlt, steht in [BEKANNTE-GRENZEN.md](BEKANNTE-GRENZEN.md). **Das Programm lädt nichts nach Intune hoch und weist nichts zu.** Es erzeugt ein Paket und eine Anleitung; die Werte tragen Sie selbst in Intune ein.

## Voraussetzungen

- Windows 11 oder Windows Server 2019 und neuer mit Desktop Experience (Server Core ist kein Ziel). Auf Server 2019 muss zuvor .NET Framework 4.8 installiert werden.
- Das Programm läuft ohne Administratorrechte und ohne Installation: ZIP entpacken, `IntunePackageBuilder.exe` starten. Es schreibt nur in Ihren Grundordner, in `%LOCALAPPDATA%\Intune Package Builder` (Einstellungen und Logs) und in ein kurzes Arbeitsverzeichnis unter dem Temp-Ordner.
- Das **Microsoft Win32 Content Prep Tool** (`IntuneWinAppUtil.exe`) gehört nicht zum Lieferumfang (seine Lizenz verbietet die Weitergabe). Laden Sie es von der Seite, die das Programm anzeigt (Schaltfläche "Downloadseite des Werkzeugs"), und wählen Sie die Datei beim ersten Bau aus. Der Pfad wird in den Einstellungen gespeichert. Ist die Datei nicht die bekannte Version 1.8.7, fragt das Programm bei jedem Bau ausdrücklich nach, ob sie trotzdem verwendet werden soll; diese Bestätigung wird nicht gespeichert.
- Das Programm ist **nicht signiert** (der Auslieferungsstand sagt das in `distribution-manifest.json`).

## Erster Start

Beim ersten Start sehen Sie die Startseite mit einem Leerzustand. Sie brauchen kein Projekt anzulegen: legen Sie eine MSI- oder EXE-Datei auf die große Fläche, oder wählen Sie "Installationsdatei auswählen". Ein Grundordner (Standard: `Dokumente\Intune-Paketprojekte`) wird beim ersten Bau angelegt und gespeichert. Ist ein früher gewählter Grundordner nicht erreichbar (zum Beispiel eine getrennte Freigabe), ersetzt das Programm ihn **nie** stillschweigend durch einen anderen; es meldet das Problem, und Sie wählen einen neuen Ordner.

## Standardmodus: ein Paket bauen

Der Standardmodus ist bei jedem Programmstart aktiv.

1. **Datei ablegen oder auswählen.** Es wird genau eine `.msi` oder `.exe` angenommen. Andere Dateitypen, mehrere Dateien und Ordner (im Standardmodus) werden mit einer Meldung abgelehnt. Das Programm **führt die Datei nie aus**; es liest nur Metadaten (MSI-Datenbank, EXE-Versionsangaben).
2. **Angaben prüfen.** Softwarename, Hersteller und Zielversion sind vorbelegt, soweit die Datei sie verrät, und änderbar. Die Zielversion ist der Zustand, der danach auf dem Gerät installiert sein soll.
   - **MSI:** Normalerweise ist nichts weiter nötig. Der ProductCode wird gelesen und ist nicht änderbar. Hinweise (zum Beispiel "installiert ohne ALLUSERS pro Benutzer" oder "braucht ihren Quellordner") erscheinen über dem Formular.
   - **EXE:** Das Programm **rät keine Schalter**. Sie müssen die stillen Installationsparameter, das Deinstallationsprogramm samt Parametern und eine Datei mit Versionsinformation für die Erkennung (voller Pfad auf dem Gerät) selbst angeben, so wie der Hersteller sie dokumentiert. Beispiele im Formular sind nur Beispiele. **Geben Sie keine Lizenzschlüssel oder Kennwörter in Parametern an:** Parameter stehen im Klartext in der Konfiguration, in der Anleitung, in den JSON- und CSV-Dateien und in den Logs.
3. **Paket erstellen.** Fehlende oder ungültige Angaben werden direkt am Feld erklärt, die Schreibmarke springt zum ersten Problem. Ein neues Projekt entsteht erst, wenn die Angaben gültig sind und der Bau startet.
4. **Während des Baus** sind alle Eingaben gesperrt; die Phase und die verstrichene Zeit werden angezeigt. Das Fenster bleibt bedienbar. Schließen Sie das Fenster, fragt das Programm vorher nach.
5. **Ergebnis.** Nur nach einem erfolgreichen Bau erscheint die Ergebnisseite mit Software, Zielversion, Build-ID, Ausgabeordner, Installations- und Deinstallationsbefehl, Installationskontext, Erkennungsskript und Log. Schaltflächen: "Paketordner öffnen", "Intune-Anleitung öffnen", "Intune-Werte kopieren". Scheitert der Bau, bleibt das Formular mit der Fehlermeldung stehen; Details stehen im Log (`builds\<build-id>.failed.log` neben den Builds der Version, sonst im Log-Ordner des Programms).

## Das Ergebnis in Intune verwenden

Im Ordner `builds\<build-id>\` liegt:

| Datei | Zweck |
|---|---|
| `<projekt-id>.intunewin` | das Paket für Intune |
| `intune\Einrichtung.html` | die Anleitung: Werte für App-Typ, App-Informationen, Programm, Anforderungen, Rückgabecodes, Erkennung, Zuweisung und Fehleranalyse (lokal lesbar und druckbar, Deutsch oder Englisch je nach Programmsprache) |
| `intune\Detect-App.ps1` | das eigenständige Erkennungsskript; es prüft den tatsächlichen Installationszustand und braucht keine Paketdateien |
| `intune\Einstellungen.json`, `intune\Einstellungen.csv` | dieselben Werte maschinenlesbar |
| `configuration.snapshot.json`, `build-manifest.json`, `build.log` | unveränderlicher Stand dieses Baus mit Prüfsummen |

Tragen Sie die Werte in Intune ein, laden Sie `.intunewin` und Erkennungsskript **desselben Builds** hoch (die Anleitung warnt davor, Dateien verschiedener Builds zu mischen). Die Mindest-Windows-Version der Anforderung ist `Windows 10 1809` (Basis von Windows Server 2019). **Ob Intune ein Paket annimmt und es auf einem Gerät funktioniert, hat das Programm nicht geprüft**; dafür gibt es das [Prüfprotokoll](PRUEFPROTOKOLL.md).

## Projekte, Versionen, Notizen

Ein **Projekt** ist ein Ordner im Grundordner. Es enthält Versionen, jede mit ihrer Konfiguration, ihrer gespeicherten Quelle (samt Prüfsummen) und ihren Builds. Die **Softwareversion** ist der Zielzustand auf dem Gerät; die **Build-ID** bezeichnet genau einen Bau. Ein neuer Bau derselben Version ist kein Anlass, die Software erneut installieren zu lassen.

- Die Startseite zeigt die zuletzt geöffneten Projekte, mit "Alle Projekte im Grundordner anzeigen" die ganze Liste, und eine Suche nach Name oder Projekt-ID. Ein Projekt öffnen Sie per Doppelklick oder "Projekt öffnen". "Projektordner öffnen…" öffnet einen vorhandenen Projektordner; liegt er außerhalb des Grundordners, fragt das Programm, ob dessen übergeordneter Ordner der Grundordner werden soll.
- Die **Projektansicht** zeigt Name, Projekt-ID, die Versionen (nach Versionsnummer sortiert, mit Installertyp und Anzahl der Builds) und Notizen. Notizen werden mit "Notizen speichern", beim Verlassen der Ansicht und beim Schließen des Fensters gespeichert; ein Schreibfehler bleibt sichtbar, und die Ansicht bleibt dann offen.
- **Update erstellen:** Version wählen, neuen Installer wählen. Parameter, Deinstallation, Erkennungspfad, Hinweise, Verknüpfungen und Laufzeit der gewählten Version werden übernommen; Version und ProductCode kommen aus dem neuen Installer. Haben Sie erweiterte Einstellungen übernommen, weist der Standardmodus darauf hin. Hat der neue Installer einen anderen Typ (MSI statt EXE oder umgekehrt), wird nichts übernommen; wählen Sie dann "Neue Version ohne Vorlage". Die gewählte Ausgangsversion bleibt unverändert. Eine Versionsnummer, die es im Projekt schon gibt, wird abgelehnt.
- **Version laden:** baut dieselbe Version aus ihrer gespeicherten Quelle noch einmal (neue Build-ID, Versionsnummer gesperrt). Frühere Builds bleiben unverändert.
- **Neue Version ohne Vorlage:** neue Version im selben Projekt, nichts übernommen.
- Eine veränderte gespeicherte Quelle wird erkannt und nie stillschweigend benutzt; gespeicherte Quellen werden nie überschrieben.

## Erweiterter Modus

Der Schalter oben rechts öffnet zusätzliche Einstellungen; ein Moduswechsel ändert keinen eingegebenen Wert, und der Modus wird nicht gespeichert (beim nächsten Start gilt wieder der Standardmodus).

- Ganzen **Herstellerordner** ablegen (mit Unterordnern); danach wählen Sie die Installationsdatei darin. Links und Reparse-Punkte (Junctions) im Ordner werden abgelehnt.
- Zusätzliche MSI-Eigenschaften bzw. Parameter, **Projekt-ID** (nur bei einem neuen Projekt), Programme, die vorher geschlossen sein müssen (eine Zeile je Programm), gemeinsame Verknüpfungen, die nach der Installation entfernt werden (`PublicDesktop|Name.lnk` oder `CommonStartMenu|Ordner\Name.lnk`), Texte für Benutzer, Zeitlimit, Erfolgs-, Neustart- und Wiederholungscodes.
- **Ausgelesene Metadaten** und **Quelle prüfen** (Dateianzahl, Größe, Fingerabdruck).
- Der Code 1641 gilt nie als stiller Erfolg und wird als Eingabe abgelehnt.
- **Laufzeit auf dem Gerät:** Statt der eingebauten Laufzeit können Sie das **PSAppDeployToolkit** wählen. Es zeigt die Dialoge (zu schließende Programme, Fortschritt, Hinweise) und lässt sich gestalten: Logo (auch für das dunkle Farbschema), Banner, Akzentfarbe, Dialogstil (Fluent oder klassisch), Firmenname, Sprache, Hinweise am Bildschirmrand, Fortschrittsfenster, Verschieben (mit Anzahl), Prüfung des freien Speichers und Sperren der zu schließenden Programme. Die Texte „Installation/Deinstallation läuft“ und der Detailhinweis erscheinen in den Dialogen. Das Toolkit gehört **nicht** zum Lieferumfang: Laden Sie die Datei `PSAppDeployToolkit_Template_v4.zip` einer Veröffentlichung herunter und wählen Sie sie im Programm; das Programm prüft die Prüfsumme gegen die bekannten Versionen und fragt bei einer unbekannten Version vor der Verwendung. Beachten Sie die Lizenzbedingungen der Bibliotheken des Toolkits (`THIRD-PARTY.md`), insbesondere von iNKORE.UI.WPF.Modern. Installation, Rückgabecodes und Prüfung des Ergebnisses bleiben gleich. **Auf Geräten nicht erprobt** (`docs/BEKANNTE-GRENZEN.md`).

## Verhalten auf dem Zielgerät

Das Paket startet über `Install.cmd` Windows PowerShell 5.1 im 64-Bit-Prozess (LocalSystem). Der Wrapper führt die Installation aus, prüft danach den tatsächlichen Zielzustand und gibt Rückgabecodes weiter (Standard: 0 Erfolg, 3010 Neustart, 1618 Wiederholung; eigene Codes 60001 bis 60099 für Wrapper-Fehler, siehe [CLIENT.md](CLIENT.md)). Es gibt **keine erzwungenen Prozessbeendigungen und keinen erzwungenen Neustart**. Sind zu schließende Programme offen, zeigt der Wrapper dem Benutzer an der Konsole ein Hinweisfenster; ohne angemeldeten Benutzer oder bei Nichtschließen endet er mit 1618, und Intune versucht es später erneut. Ein **Fortschrittsfenster** gibt es noch nicht.

## Fehleranalyse

| Wo | Was |
|---|---|
| `%LOCALAPPDATA%\Intune Package Builder\Logs` | Log des Programms (Starts, unerwartete Fehler) |
| `<Version>\builds\<build-id>\build.log` | Log eines erfolgreichen Baus |
| `<Version>\builds\<build-id>.failed.log` | Log eines fehlgeschlagenen Baus |
| Gerät: Protokolle des Wrappers | siehe [CLIENT.md](CLIENT.md) |
| `Einrichtung.html`, Abschnitt Fehleranalyse | Hilfe für die Fehlersuche in Intune |

Häufige Meldungen: "Das Packwerkzeug wird benötigt" (Content Prep Tool auswählen), "… ist nicht erreichbar. Er wurde nicht ersetzt." (Freigabe wieder verbinden oder anderen Ordner wählen), "Ein anderes Programm oder Fenster arbeitet an dieser Version" (ein anderer Bau oder eine andere Programminstanz), "Die gespeicherte Quelle wurde nach dem Speichern verändert" (Quelle wiederherstellen oder neue Version anlegen).
