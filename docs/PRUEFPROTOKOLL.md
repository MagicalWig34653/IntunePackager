# Prüfprotokoll

Protokoll der Geräte- und Bedienungstests nach Spec §12.2. **Stand: keiner dieser Tests ist durchgeführt.** Sie lassen sich in der Cloud-Sitzung, in der die Software entwickelt wurde, nicht ausführen (kein Gerät, keine Benutzersitzung, kein Intune-Mandant). Lokale Paketiertests und die automatisierten CI-Prüfungen gelten nicht als Nachweis einer erfolgreichen Intune-Verteilung.

Die automatisierten Prüfungen A01 bis A18 stehen mit CI-Läufen in [ABNAHME-MATRIX.md](ABNAHME-MATRIX.md).

## Zu erfassende Angaben je Durchlauf

| Angabe | Eintrag |
|---|---|
| Programmversion und Prüfsumme der ZIP | |
| Betriebssystem und Build des Arbeitsplatzes | |
| Betriebssystem und Build des Zielgeräts | |
| Hersteller, Produkt und Version des geprüften Pakets (MSI oder EXE) | |
| Version des Content Prep Tools | |
| Prüfer und Datum | |

## Tests

| Nr. | Test (Spec §12.2) | Ergebnis | Anmerkung |
|---|---|---|---|
| T01 | Start und Standardablauf unter Windows 11 | offen | |
| T02 | Start und Standardablauf unter Windows Server 2019 (Desktop Experience) | offen | kein CI-Runner vorhanden |
| T03 | Start und Standardablauf unter Windows Server 2022 oder neuer | offen | |
| T04 | Bedienung bei kleiner Fenstergröße und 100 %, 125 %, 150 %, 200 % Skalierung; keine unerreichbare Hauptaktion | offen | |
| T05 | Kompakte Standard-MSI-Ansicht auf 1366×768 ohne unnötiges Scrollen | offen | |
| T06 | Tastaturbedienung, sichtbarer Fokus, Tab-Reihenfolge, verständliche Beschriftungen | offen | |
| T07 | Eine IT-Person paketiert eine gewöhnliche MSI ohne Studium der Dokumentation | offen | |
| T08 | Reale MSI: Installation, Erkennung, Deinstallation, Wiederholung im SYSTEM-Kontext | offen | |
| T09 | Reale EXE: Installation, Erkennung, Deinstallation, Wiederholung im SYSTEM-Kontext | offen | |
| T10 | Benutzerfenster in erreichbarer Sitzung; offene Prozesse; Verschieben | offen | Fortschrittsfenster fehlt (BEKANNTE-GRENZEN.md) |
| T11 | Mehrere und fehlende Benutzersitzungen | offen | |
| T12 | Parallelinstallation | offen | |
| T13 | Rückgabecodes und Neustartbedarf; keine unbeabsichtigten Prozessenden oder erzwungenen Neustarts | offen | |
| T14 | Anleitung in eine Intune-Pilot-App übernehmen und auf einem Zielgerät prüfen | offen | |

## Offene Punkte

Alle Tests oben. Zusätzlich: Entscheidung über das Fortschrittsfenster (PLANUNG.md §8) und die Signierung (Zertifikat vorhanden?).
