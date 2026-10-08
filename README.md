# Intune Package Builder

Windows-Desktopanwendung, die aus einer MSI, einer EXE oder einem Herstellerordner ein vollständiges Microsoft-Intune-Win32-Paket erstellt: `.intunewin`, eigenständiges Erkennungsskript und eine konkrete Einrichtungsanleitung.

**Status:** Planung. Es gibt noch keine Implementierung und keine Abnahme.

## Dokumente

| Datei | Inhalt |
|---|---|
| [docs/SPEC.md](docs/SPEC.md) | Produktspezifikation (Version 1.0) |
| [docs/PLANUNG.md](docs/PLANUNG.md) | Architektur, Meilensteine, Teststrategie, offene Entscheidungen |
| [docs/ABNAHME-MATRIX.md](docs/ABNAHME-MATRIX.md) | Zuordnung der Abnahmeprüfungen A01–A18 zu Meilensteinen |

## Geplante Struktur

```text
src/      Autorentool (C# / WPF, .NET Framework 4.8) in getrennten Modulen
deploy/   Client-Laufzeit: Wrapper-Vorlagen und gepinnte Drittanbieter-Bibliothek
tools/    Win32 Content Prep Tool samt Herkunfts- und Prüfsummenmanifest
tests/    xUnit, Pester, UI-Tests
docs/     Spezifikation, Planung, später Bedienungsanleitung und Prüfprotokoll
```

Benutzerprojekte und Herstellerinstaller gehören nicht in dieses Repository (siehe `.gitignore`).

## Lizenz

Siehe [LICENSE](LICENSE).
