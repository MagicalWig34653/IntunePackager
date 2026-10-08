# Rückverfolgbarkeit: Abnahme → Meilenstein

Zuordnung der verbindlichen automatisierten Prüfungen aus [SPEC.md](SPEC.md) §12.1 zu Meilensteinen aus [PLANUNG.md](PLANUNG.md). Status wird beim Umsetzen gepflegt; „offen“ heißt: weder implementiert noch geprüft.

| ID | Szenario | Meilenstein(e) | Testart | Status |
|---|---|---|---|---|
| A01 | Erststart ohne Einstellungen und Projekte | M6 | UI | offen |
| A02 | MSI-Metadaten lesen, ohne Ausführung | M2 | xUnit | offen |
| A03 | EXE ohne Silent-/Erkennungsangaben sperrt Build | M1 (Modell), M6 (UI) | xUnit, UI | Modell bestanden (CI 37825409326), UI offen |
| A04 | Auswahl und Drag-and-drop gleichwertig; Fehltypen/Mehrfach abgelehnt | M2 (Logik), M6 (UI) | xUnit, UI | offen |
| A05 | Projekt mehrfach öffnen/wechseln ohne Kollision | M1, M7 | xUnit, UI | Speicherschicht bestanden (CI 37825409326), UI offen |
| A06 | Notizen bleiben erhalten | M1, M7 | xUnit, UI | Speicherschicht bestanden (CI 37825409326), UI offen |
| A07 | Update aus Version | M7 | xUnit, UI | offen |
| A08 | Moduswechsel verliert keine Werte | M6, M7 | UI | offen |
| A09 | Build blockiert UI nicht, Eingaben gesperrt | M4, M6 | xUnit, UI | offen |
| A10 | Gleiche Version erneut bauen | M4 | xUnit | offen |
| A11 | Veränderte Quelle wird erkannt | M2, M4 | xUnit | offen |
| A12 | Rekursiver Ordner / Junction abgelehnt | M2 | xUnit | offen |
| A13 | Rechte-/Platz-/Packwerkzeugfehler | M4 | xUnit | offen |
| A14 | Zweiter Prozess, gleiche Version | M1 | xUnit (Mehrprozess) | offen |
| A15 | Erkennung älter / passend / neuer / fremd | M3, M5 | Pester | offen |
| A16 | Erkennung ohne Paketcache | M3, M5 | Pester | offen |
| A17 | Sonderzeichen in HTML/JSON/CSV/PowerShell | M3 | xUnit, Pester | offen |
| A18 | Distributionsinhalt und Prüfsummen | M8 | Skript in CI | offen |

Geräte- und Bedienungstests (§12.2) laufen manuell in M8 und werden im Prüfprotokoll (`docs/PRUEFPROTOKOLL.md`, entsteht in M8) festgehalten.
