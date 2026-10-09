# Drittanbieter-Komponenten

Verbindliches Abhängigkeitsmanifest (SPEC §4, §11). Jede Komponente mit Version, Herkunft, Lizenz und SHA-256. Einträge werden beim Einbinden ergänzt; nicht aufgeführte Komponenten dürfen nicht ausgeliefert werden.

| Komponente | Version | Herkunft | Lizenz | SHA-256 | Verwendung | Status |
|---|---|---|---|---|---|---|
| Microsoft Win32 Content Prep Tool (`IntuneWinAppUtil.exe`) | 1.8.7 (Tag `v1.8.7`, Commit `1d6cfcbdf8c28edc596337031f74df951f38f718`) | github.com/microsoft/Microsoft-Win32-Content-Prep-Tool | Microsoft Software License Terms for Win32 Content Prep Tool (**nicht MIT**; Abschnitt 4e verbietet, die Software zu teilen oder zu veröffentlichen) | `c1ba45b5cb939e84af064bb7ff4b38fb3dfe33c8dc1078fd9b157672eae671f6` (`IntuneWinAppUtil.exe` im Tag `v1.8.7`) | Erzeugt `.intunewin`. **Wird nicht in dieses Repository und nicht in die Distribution aufgenommen**; der Benutzer beschafft es selbst, das Autorentool prüft die SHA-256 (siehe `docs/PLANUNG.md` §8) | geplant (M4) |
| PSAppDeployToolkit | offen (M5) | psappdeploytoolkit.com | LGPL-3.0 | offen | Benutzerinteraktion auf dem Zielgerät | geplant |
| Newtonsoft.Json | 13.0.3 | NuGet (www.nuget.org/packages/Newtonsoft.Json) | MIT | Paket `872fc189e638ab1056555b03aaa38f68bcb54286e221aa646eb1129babf63c77`; `lib/net45/Newtonsoft.Json.dll` `e1e27af7b07eeedf5ce71a9255f0422816a6fc5849a483c6714e1b472044fa9d` (aus dem Paket berechnet; der Distributionsbau in M8 prüft die ausgelieferte DLL erneut) | JSON lesen und schreiben im Core; wird mit dem Autorentool ausgeliefert | aktiv |
| xUnit | 2.9.2 | NuGet | Apache-2.0 | n/a (NuGet-Paket) | Nur Tests, nicht in der Distribution | aktiv |
| Microsoft.NET.Test.Sdk | 17.11.1 | NuGet | MIT | n/a (NuGet-Paket) | Nur Tests, nicht in der Distribution | aktiv |
| xunit.runner.visualstudio | 2.8.2 | NuGet | Apache-2.0 | n/a (NuGet-Paket) | Nur Tests, nicht in der Distribution | aktiv |
| Pester | mindestens 5.5.0 (PowerShell Gallery, bei jedem CI-Lauf neu installiert) | powershellgallery.com/packages/Pester | Apache-2.0 | n/a (Modul aus der PowerShell Gallery) | Nur Tests der CI, nicht in der Distribution | aktiv |

Reine Test-NuGet-Pakete werden beim Restore von NuGet geladen (Paket-Hash und Signatur prüft NuGet) und nie ausgeliefert; daher keine eigene Prüfsumme. Für ausgelieferte Komponenten ist die SHA-256 Pflicht.

Signaturstatus der Auslieferung: **nicht signiert**.
