# Client-Laufzeit (Zielgerät)

Beschreibt, was im Paket liegt und wie es auf dem Zielgerät abläuft (Spec §8). Quellen: `deploy/template/`. Die Pipeline (`docs/BUILD.md`) kopiert diesen Ordner in die Paketwurzel und ergänzt `Deployment.config.json` und `Files\`.

## Dateien im Paket

| Datei | Aufgabe |
|---|---|
| `Install.cmd` | Eintrittspunkt für Intune. Startet den Wrapper in der **nativen 64-Bit-Windows-PowerShell** (`Sysnative`, falls Intune die Datei als 32-Bit-Prozess startet), reicht alle Argumente weiter (`-DeploymentType Uninstall`) und gibt den Exitcode zurück. Reines ASCII |
| `Deploy-Wrapper.ps1` | Dünner Einstieg: lädt `DeployCore.psm1`, ruft `Invoke-Deployment` und beendet mit dessen Ergebnis. Zusätzlicher Parameter `-LogDirectory` nur für Tests und Diagnose |
| `DeployCore.psm1` | Die Logik (Konfiguration lesen, Befehle bauen, Installer starten und abwarten, Zielzustand prüfen, Nacharbeiten, Benutzerinteraktion, Rückgabecodes) |
| `Messages.en.psd1`, `Messages.de.psd1` | Texte für den Benutzer, gleiche Schlüssel (ein Test prüft das); die Sprache steht in `Deployment.config.json` (`language`) |
| `Deployment.config.json` | Konfiguration dieses Builds (`docs/DATENFORMAT.md`) |
| `Files\` | Installer und Herstellerdateien |

Alle Skripte: Windows PowerShell 5.1, UTF-8 **mit BOM**, keine PowerShell-7-Syntax (Pester prüft das für `deploy/`).

## Ablauf

1. **64-Bit-Prüfung.** Läuft der Wrapper trotz 64-Bit-Windows in einem 32-Bit-Prozess, endet er mit 60006.
2. **Konfiguration lesen** (`Read-DeployConfig`); Fehler enden mit 60002. Das Log liegt im Ordner der Konfiguration (`logs.directory`), eingeschränkt auf SYSTEM und Administratoren; bei einem Fehler vor Kenntnis dieses Ordners schreibt der Wrapper `Wrapper-Startup.log` in `C:\Windows\Logs\Intune\PackageDeploy`.
3. **Programme schließen** (nur wenn `processesToClose` gesetzt ist), siehe unten.
4. **Befehl bauen** (`New-ProcessCommand`) und starten, ohne Shell, mit einer Argumentliste:
   - MSI installieren: `msiexec.exe /i <msi> /qn /norestart /l*v <Log>` plus die in der Konfiguration eingetragenen Zusatzangaben (zum Beispiel Eigenschaften). **`ALLUSERS` wird nicht ergänzt** (offene Entscheidung, `docs/PLANUNG.md` §8); wer es braucht, trägt es in den Installationsparametern ein.
   - MSI deinstallieren: `msiexec.exe /x <ProductCode> /qn /norestart /l*v <Log>`; der ProductCode kommt aus der Deinstallation oder der Installation.
   - EXE: genau das Programm und die Parameter aus der Konfiguration (Installer aus `Files\`, Deinstallationsprogramm mit seinen Parametern). Es wird **nichts erraten**.
5. **Abwarten bis zum tatsächlichen Ende.** Der Wrapper wartet auf den Prozess **und auf alle Prozesse, die er gestartet hat** (auch wenn der Starter sich früh beendet). Nichts wird zwangsweise beendet. Das Zeitlimit des Wrappers liegt zwei Minuten unter dem Intune-Wert; wird es erreicht, endet der Wrapper mit 60004 und lässt den Prozess laufen.
6. **Rückgabecode** (siehe Tabelle) und, bei Erfolg, **Prüfung des Zielzustands** mit derselben Regel wie das Erkennungsskript (`Test-TargetState`; ein Test vergleicht beide). Nach einer Installation muss der Zielzustand erreicht, nach einer Deinstallation verschwunden sein, sonst 60001.
7. **Nacharbeiten** nach einer verifizierten Installation (Spec §8.5): Die konfigurierten gemeinsamen Verknüpfungen werden entfernt.

## Rückgabecodes

Der Wrapper gibt den Exitcode des Installers **unverändert** weiter, damit Intune ihn mit der Tabelle der Anleitung einordnet (`docs/GENERATOREN.md`).

| Situation | Exitcode |
|---|---|
| Installer-Code Erfolg (Standard 0) und Zielzustand verifiziert | der Code (0) |
| Installer-Code Soft Reboot (Standard 3010) und Zielzustand verifiziert | der Code (3010); der Wrapper startet nie neu |
| Installer-Code Retry (Standard 1618) | der Code; es wird nichts verifiziert |
| **1641** (Installer hat selbst neu gestartet) | 1641, in Intune als Hard Reboot eingetragen; **nie ein stiller Erfolg** |
| Code, der nicht in der Tabelle steht | der Code (Intune wertet ihn als Fehler) |
| MSI-Deinstallation meldet 1605 (nicht installiert) | 0, im Log vermerkt |
| Zielzustand nach „Erfolg“ nicht erreicht (Installation) oder noch vorhanden (Deinstallation) | **60001** |
| Konfiguration fehlt oder ist unlesbar/ungültig | **60002** |
| Installer oder Deinstallationsprogramm fehlt | **60003** |
| Zeitlimit erreicht | **60004** |
| Wrapper nicht in 64-Bit-Prozess | **60006** |
| Unerwarteter Fehler | **60099** |
| Programme zum Schließen offen und niemand erreichbar oder nicht rechtzeitig geschlossen | **1618** (Retry) |

Die 6xxxx-Codes stehen nicht in der Intune-Tabelle und gelten dort als Fehler; die Ursache steht im Log.

## Benutzerinteraktion (Umfang in M5)

Entschieden und umgesetzt ist nur das, was sich mit Bordmitteln und ohne Fremdbibliothek sicher lösen lässt (`docs/PLANUNG.md` §8):

- **Programme schließen.** Sind konfigurierte Programme geöffnet, zeigt der Wrapper dem Benutzer an der Konsole über die Windows-Funktion `WTSSendMessage` ein Hinweisfenster in dessen Sitzung (Titel mit Softwarename, Liste der Programme, optionaler Detailhinweis aus der Konfiguration, Sprache aus der Konfiguration). Danach wartet er und wiederholt den Hinweis in Abständen, höchstens 30 Minuten (und höchstens ein Drittel des Zeitlimits). Es wird **nichts erzwungen**.
- **Verhalten ohne Benutzer, mit mehreren Sitzungen und parallel** (Spec §8.2, ausdrücklich festgelegt):

| Zustand | Verhalten |
|---|---|
| Programme laufen nicht | keine Meldung, Installation läuft sofort |
| Programme laufen, **niemand an der Konsole angemeldet** (Anmeldebildschirm, nur Dienste) | Ende mit **1618** (Retry); Intune versucht es später erneut |
| Programme laufen, Benutzer an der Konsole | Hinweis an diese Sitzung; nach Ablauf der Wartezeit ohne Schließen **1618** |
| Mehrere Sitzungen | Nur die Konsolensitzung wird angesprochen. Programme in anderen Sitzungen zählen beim Prüfen mit; sind sie nicht geschlossen, endet der Wrapper mit 1618 |
| Hinweis lässt sich nicht anzeigen | **1618** |
| Zwei Installationen gleichzeitig | werden von Intune und Windows Installer serialisiert; der Wrapper selbst hält keine Sperre. **Nicht getestet** |

### Noch nicht umgesetzt

- **Fortschrittsfenster** (Software, Aktion, Status, Laufzeit; Spec §8.2). Dafür muss aus SYSTEM ein eigener Prozess in der Benutzersitzung gestartet werden (`CreateProcessAsUser`) oder eine geprüfte Bibliothek wie PSAppDeployToolkit eingebunden werden. Beides lässt sich in der Cloud-Sitzung nicht prüfen und bleibt offen; die Hinweise der Konfiguration (`installMessage`, `uninstallMessage`) werden bisher nicht angezeigt.
- Die Sitzungsvermittlung ist nur mit Pester-Mocks getestet; **kein Test lief als SYSTEM mit einer echten Benutzersitzung**. Das gehört in die Gerätetests (M8).

## Tests

`tests/pester/Wrapper.Tests.ps1` (Windows PowerShell 5.1, Pester 5, echte Prozesse, Registry-Schlüssel und Dateien; Benutzerinteraktion gemockt): Vorlagen, Argumentquoting gegen den Windows-Parser, Konfigurationsfehler, Befehle, Prozessende einschließlich gestarteter Kindprozesse und Zeitlimit, Zielzustand verglichen mit dem erzeugten Erkennungsskript, Nacharbeiten (Whitelist, `..`, absolute Pfade, fremde Dateitypen, Junctions), alle Zeilen der Tabelle oben, vollständige Läufe über `Install.cmd`, Zugriffsrechte des Logordners.
`tests/IntunePackageBuilder.Build.Tests/WrapperChainTests.cs` (xUnit): das von der Pipeline gestaltete Paket (Vorlagen, Konfiguration des Generators, `Files\`) läuft über `Install.cmd` und liefert die erwarteten Exitcodes.
