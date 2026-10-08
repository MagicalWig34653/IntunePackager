# Drittanbieter-Komponenten

Verbindliches Abhängigkeitsmanifest (SPEC §4, §11). Jede Komponente mit Version, Herkunft, Lizenz und SHA-256. Einträge werden beim Einbinden ergänzt; nicht aufgeführte Komponenten dürfen nicht ausgeliefert werden.

| Komponente | Version | Herkunft | Lizenz | SHA-256 | Verwendung | Status |
|---|---|---|---|---|---|---|
| Microsoft Win32 Content Prep Tool (`IntuneWinAppUtil.exe`) | offen (M4) | github.com/microsoft/Microsoft-Win32-Content-Prep-Tool | MIT | offen | Erzeugt `.intunewin` | geplant |
| PSAppDeployToolkit | offen (M5) | psappdeploytoolkit.com | LGPL-3.0 | offen | Benutzerinteraktion auf dem Zielgerät | geplant |
| xUnit | 2.9.2 | NuGet | Apache-2.0 | n/a (NuGet-Paket) | Nur Tests, nicht in der Distribution | aktiv |
| Microsoft.NET.Test.Sdk | 17.11.1 | NuGet | MIT | n/a (NuGet-Paket) | Nur Tests, nicht in der Distribution | aktiv |
| xunit.runner.visualstudio | 2.8.2 | NuGet | Apache-2.0 | n/a (NuGet-Paket) | Nur Tests, nicht in der Distribution | aktiv |

Reine Test-NuGet-Pakete werden beim Restore von NuGet geladen (Paket-Hash und Signatur prüft NuGet) und nie ausgeliefert; daher keine eigene Prüfsumme. Für ausgelieferte Komponenten ist die SHA-256 Pflicht.

Signaturstatus der Auslieferung: **nicht signiert**.
