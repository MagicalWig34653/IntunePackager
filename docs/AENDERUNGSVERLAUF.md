# Änderungsverlauf

Der vollständige Verlauf steht in der Git-Historie; hier die Stufen (Meilensteine) mit dem CI-Nachweis in `ARBEITSSTAND.md`.

## 0.0.1 (erste Vorabversion, soweit ohne Gerätetests möglich; abnahmefähig erst nach den Gerätetests)

- **M0** Projektgerüst, Windows-CI, Logging, Pester, Formatprüfung.
- **M1** Core: Projekte, Versionen, Sperren (auch zwischen Prozessen), Einstellungen, Grundordner, atomares Schreiben, Migration.
- **M2** Quellenanalyse: MSI-Metadaten über `msi.dll`, EXE-Versionsangaben, Quellenmanifest mit Änderungserkennung, sicherer Import (keine Rekursion, keine Junctions, Pfadlänge).
- **M3** Generatoren: Erkennungsskript, Intune-Anleitung (Deutsch und Englisch), JSON und CSV, Wrapper-Konfiguration, alles aus einem Snapshot.
- **M4** Build-Pipeline mit dem Content Prep Tool (vom Benutzer beschafft): atomare Veröffentlichung, Prüfsummen, Fehlerlogs.
- **M5** Client-Laufzeit: `Install.cmd`, Wrapper, Rückgabecodes, Hinweis zum Schließen von Programmen, Nacharbeiten.
- **M6** Oberfläche für den Standardmodus (WPF), Sprache Deutsch und Englisch, UI-Tests mit FlaUI.
- **M7** Erweiterter Modus, Projektansicht mit Notizen, Update-Ablauf, erneuter Bau einer Version, Metadaten- und Quellenprüfung.
- **M8** Portable Distribution mit Prüfsummen und Prüfung (A18), Bedienungsanleitung, bekannte Grenzen, Prüfprotokoll.

Unterstützt: Windows 11 und Windows Server 2019 und neuer (Server 2019 ungeprüft). Siehe [BEKANNTE-GRENZEN.md](BEKANNTE-GRENZEN.md).
