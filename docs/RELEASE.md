# Release

So entsteht eine Veröffentlichung. Der Ablauf steckt im Workflow `.github/workflows/release.yml`.

## Auslöser

- **Tag:** Wird ein Tag `v<major>.<minor>.<patch>[-label]` gepusht, baut der Workflow genau diesen Stand und hängt die ZIP an die GitHub-Veröffentlichung des Tags.
- **Manuell:** "Actions → Release → Run workflow" mit der Version (zum Beispiel `0.0.1`, ohne `v`). Der Workflow legt den Tag `v<version>` auf dem gewählten Stand an und veröffentlicht dann genauso. Ein vorhandener Tag wird nie überschrieben.

## Was der Workflow tut

1. Version bestimmen und prüfen; "Vorabversion" ist gesetzt, wenn die Version ein Label hat oder mit `0.` beginnt (beim manuellen Lauf nach der Auswahl).
2. **Grüne CI verlangen:** Auf dem Stand müssen alle fünf Prüfungen der CI erfolgreich sein (Bau auf `windows-latest` und `windows-2022`, Pester, UI-Tests, Distribution). Sonst wird nichts veröffentlicht.
3. Lösung bauen (Release, Version aus dem Tag, daher stimmt die Programmversion mit dem Tag überein) und alle xUnit-Tests ausführen.
4. `tools/New-Distribution.ps1` baut die ZIP und führt die Prüfung A18 darauf aus; danach entsteht `<ZIP>.sha256`.
5. ZIP und Prüfsumme werden als Artefakt des Laufs und als Dateien der GitHub-Veröffentlichung abgelegt. Die Hinweise stammen aus `docs/release-notes/v<version>.md`, sonst aus einem englischen Standardtext.

## Grenzen

- Die ZIP ist **nicht signiert**; das steht im `distribution-manifest.json` und in den Hinweisen.
- Eine Veröffentlichung sagt nichts über Geräte- und Bedienungstests (`docs/PRUEFPROTOKOLL.md`). Solange sie offen sind, bleibt jede Fassung eine Vorabversion.
- Das Content Prep Tool ist nie Teil einer Veröffentlichung.
