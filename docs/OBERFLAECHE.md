# Oberfläche (WPF)

Stand: M7a. Die Oberfläche steckt im Projekt `src/IntunePackageBuilder.App`; die Logik liegt in ViewModels ohne WPF-Abhängigkeit, damit sie ohne Fenster testbar ist (`tests/IntunePackageBuilder.App.Tests`). Das Erscheinungsbild ist **nie begutachtet** worden (keine Screenshots in der Cloud-Sitzung).

## Aufbau

| Teil | Aufgabe |
|---|---|
| `MainWindow` | Kopfzeile mit Modusschalter, darunter die aktuelle Seite (`MainViewModel.Current`); Schließen während eines Baus fragt nach, ungespeicherte Projektnotizen werden vor dem Schließen gespeichert |
| `StartView` / `StartViewModel` | Ablegefläche, Dateiauswahl, zuletzt geöffnete Projekte, Liste aller Projekte mit Suche, Grundordner ändern, Projekt öffnen, Projektordner öffnen |
| `FormView` / `FormViewModel` | Angaben zum Paket; zeigt nur die passenden Felder; Probleme direkt am Feld, Fokus auf dem ersten; Bau mit Phase und verstrichener Zeit, Eingaben währenddessen gesperrt |
| `ResultView` / `ResultViewModel` | Ergebnis des Baus (nie nach einem Fehler) |
| `ProjectView` / `ProjectViewModel` | Projektansicht: Name, Versionen (nach Versionsnummer sortiert, Builds nur als Zusatz), Notizen |

Alle sichtbaren Texte stehen in `Resources/Strings.resx` und `Strings.de.resx`. Die Programmsprache folgt der Windows-UI-Sprache.

## Regeln, die der Code einhält

- Der Standardmodus ist bei jedem Start aktiv; der Modus wird nicht gespeichert; ein Moduswechsel ändert keinen Wert (A08).
- Ein konfigurierter, aber nicht erreichbarer Grundordner wird nie durch einen anderen ersetzt. Ein Projektordner außerhalb des Grundordners wird erst nach Bestätigung geöffnet; dann wird sein übergeordneter Ordner zum Grundordner.
- Notizen werden ausdrücklich und beim Verlassen der Projektansicht gespeichert; ein Schreibfehler bleibt sichtbar und hält die Ansicht offen. Ein nicht lesbares Projekt wird gemeldet und seine Notizen werden nie geschrieben.
- Für die UI-Automation tragen nur Steuerelemente mit Automation-Peer (Texte, Schaltflächen, Listen, Eingabefelder, Fortschrittsbalken) eine `AutomationId`; Panels und Rahmen erscheinen nicht im Baum.

## Schalter für Tests und Support

`IntunePackageBuilder.exe --language de|en --settings-dir <Ordner> --open <Installer>`

`--open` führt dieselbe Prüfung aus wie ein Ablegen auf die Ablagefläche (`StartViewModel.HandleDrop`). Das echte Ablegen mit der Maus ist nicht automatisiert.

## Tests

- `App.Tests` (xUnit): ViewModels, Textschlüssel in beiden Sprachen.
- `UiTests` (FlaUI, eigener CI-Job `UI tests (FlaUI)`, nicht in der Projektmappe): startet das echte Programm mit eigenem Einstellungsordner.
