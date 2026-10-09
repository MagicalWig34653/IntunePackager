# Rückverfolgbarkeit: Abnahme → Meilenstein

Zuordnung der verbindlichen automatisierten Prüfungen aus [SPEC.md](SPEC.md) §12.1 zu Meilensteinen aus [PLANUNG.md](PLANUNG.md). Status wird beim Umsetzen gepflegt; „offen“ heißt: weder implementiert noch geprüft.

| ID | Szenario | Meilenstein(e) | Testart | Status |
|---|---|---|---|---|
| A01 | Erststart ohne Einstellungen und Projekte | M6 | UI | bestanden (CI 37948001793): FlaUI startet das echte Programm mit leeren Einstellungen, Startseite mit Ablegefläche, Auswahl, Modusschalter, Grundordner und leerer Projektliste |
| A02 | MSI-Metadaten lesen, ohne Ausführung | M2 | xUnit | bestanden (CI 37832133738) |
| A03 | EXE ohne Silent-/Erkennungsangaben sperrt Build | M1 (Modell), M6 (UI) | xUnit, UI | Modell bestanden (CI 37825409326); UI bestanden (CI 37948001793): EXE ohne Herstellerangaben wird am Formular beanstandet, es entsteht weder Fortschritt noch Ergebnisseite |
| A04 | Auswahl und Drag-and-drop gleichwertig; Fehltypen/Mehrfach abgelehnt | M2 (Logik), M6 (UI) | xUnit, UI | Logik bestanden (CI 37833609876); UI bestanden (CI 37948001793): Datei falschen Typs wird auf der Startseite abgelehnt (der Testweg `--open` und das Ablegen laufen durch dieselbe Prüfung `HandleDrop`; das echte Ablegen per Maus ist nicht automatisiert, gleichwertige Auswahl über den Dialog ebenso nicht) |
| A05 | Projekt mehrfach öffnen/wechseln ohne Kollision | M1, M7 | xUnit, UI | Speicherschicht bestanden (CI 37825409326); Oberfläche bestanden (CI 37949478226): zwei Projekte nacheinander öffnen und wieder verlassen, jedes behält seine Daten; ViewModel-Tests (Wechsel, Ordner öffnen) |
| A06 | Notizen bleiben erhalten | M1, M7 | xUnit, UI | Speicherschicht bestanden (CI 37825409326); Oberfläche bestanden (CI 37949478226): Notizen speichern, beim Verlassen speichern, beim Schließen speichern, Schreibfehler bleibt sichtbar und hält die Ansicht offen |
| A07 | Update aus Version | M7 | xUnit, UI | xUnit bestanden (CI 37983555527): Einstellungen werden übernommen, Ausgangsversion (Konfiguration und Quelle) bleibt byteweise unverändert, Typwechsel wird abgelehnt, vorhandene Versionsnummer wird abgelehnt, erneutes Bauen mit neuer Build-ID, neue Version ohne Vorlage; die Dateidialoge des Update-Ablaufs sind nicht per UI-Automation geprüft |
| A08 | Moduswechsel verliert keine Werte | M6, M7 | UI | bestanden für die Oberfläche (CI 37948001793): Moduswechsel hin und zurück ändert keinen eingegebenen Wert; der Hinweis auf übernommene erweiterte Einstellungen und der Weg in den erweiterten Modus sind in xUnit geprüft (CI 37983555527) |
| A09 | Build blockiert UI nicht, Eingaben gesperrt | M4, M6 | xUnit, UI | Logik bestanden (CI 37887554732); UI bestanden (CI 37948001793): während des Baus ist das Formular gesperrt und die Fortschrittsanzeige sichtbar, danach erscheint die Ergebnisseite mit Paket und Anleitung (Bau mit dem echten Content Prep Tool) |
| A10 | Gleiche Version erneut bauen | M4 | xUnit | bestanden (CI 37887554732): neue Build-ID auch bei gleichem Zeitpunkt, früherer Build byteweise unverändert |
| A11 | Veränderte Quelle wird erkannt | M2, M4 | xUnit | Manifestprüfung bestanden (CI 37832133738); Verhalten im Build bestanden (CI 37887554732): geänderte, hinzugefügte oder fehlende Quelldatei und fehlendes Manifest werden vor jeder Verwendung abgewiesen, kein Build entsteht |
| A12 | Rekursiver Ordner / Junction abgelehnt | M2 | xUnit | bestanden (CI 37833609876), mit echten Junctions |
| A13 | Rechte-/Platz-/Packwerkzeugfehler | M4 | xUnit | bestanden (CI 37887554732; Werkzeug: CI 37886853214): nicht beschreibbarer Build-Ordner, zu wenig Platz (über austauschbare Messung), zu lange Pfade, Werkzeugfehler, Zeitüberschreitung, ungültiges Paket, fehlende Vorlagen; nichts Halbes veröffentlicht, Aufräumen nur mit Marker. Nicht real provoziert: ein wirklich volles Laufwerk |
| A14 | Zweiter Prozess, gleiche Version | M1 | xUnit (Mehrprozess) | Sperre auf Speicherebene bestanden mit echtem Zweitprozess (CI 37827032442); Verhalten in der UI offen |
| A15 | Erkennung älter / passend / neuer / fremd | M3, M5 | Pester | Erzeugtes Skript gegen echte Registry-Schlüssel und Dateien bestanden (CI 37884553567); die Zielzustandsprüfung des Wrappers liefert für dieselben Fälle dasselbe Ergebnis und der Wrapper reicht den Installer-Code durch (CI 37888755850); unter LocalSystem auf einem Gerät (M8) offen |
| A16 | Erkennung ohne Paketcache | M3, M5 | Pester | Skript allein in leerem Ordner, anderer Arbeitsordner, ohne Bezug zu Paket oder Cache bestanden (CI 37884553567); die Wrapper-Prüfung liest den Zustand direkt, nicht aus dem Cache (CI 37888755850) |
| A17 | Sonderzeichen in HTML/JSON/CSV/PowerShell | M3 | xUnit, Pester | bestanden über alle Formate: JSON/CSV (CI 37835593601), PowerShell (CI 37884553567), HTML, Wrapper-Konfiguration und Gesamterzeugung mit feindlichen Werten (CI 37885815101) |
| A18 | Distributionsinhalt und Prüfsummen | M8 | Skript in CI | offen |

Geräte- und Bedienungstests (§12.2) laufen manuell in M8 und werden im Prüfprotokoll (`docs/PRUEFPROTOKOLL.md`, entsteht in M8) festgehalten.
