# Bekannte Grenzen

Stand: Version 0.0.1 und der Entwicklungsstand danach (die Einträge zum PSAppDeployToolkit betreffen nur Stände nach 0.0.1; der Release v0.0.1 enthält die Toolkit-Engine nicht). Was hier steht, ist **nicht** umgesetzt oder **nicht geprüft**. Es ist kein Mangel, der "später kommt", sondern der ehrliche Stand.

## Nicht umgesetzt

- **Fortschrittsfenster auf dem Zielgerät** (Spec §8.2: Software, Aktion, Status, Laufzeit). Der Wrapper zeigt nur das Hinweisfenster zum Schließen von Programmen (`WTSSendMessage`). Für ein Fortschrittsfenster müsste aus SYSTEM ein Prozess in der Benutzersitzung gestartet oder eine geprüfte Bibliothek (zum Beispiel PSAppDeployToolkit) eingebunden werden; die Entscheidung steht in `PLANUNG.md` §8.
- **Installations- und Deinstallationstext für Benutzer** (Spec §5.3, §6.3): Die Texte werden eingegeben und gespeichert, der Wrapper zeigt sie aber nicht an; nur der Detailhinweis erscheint im Hinweisfenster zum Schließen von Programmen. Sie brauchen das Fortschrittsfenster. Die Beschriftungen im Formular sagen das.
- **"Verschieben"** (Spec §8.2): Das Hinweisfenster hat eine Schaltfläche. Der Benutzer kann die Programme schließen oder warten; ein Verschieben geschieht nur, indem der Wrapper nach der Wartezeit (höchstens 30 Minuten, höchstens ein Drittel des Zeitlimits) mit 1618 endet und Intune es später erneut versucht.
- **Zielarchitektur** (Spec §4, §6.3): `install.targetArchitecture` wird gespeichert und in die Wrapper-Konfiguration geschrieben, aber weder abgefragt noch geprüft; die Anleitung nennt immer x64. Es gibt keine Prüfung, ob der Erkennungspfad einer EXE zur Architektur des Programms passt.
- **Direkter Upload nach Intune** (Microsoft Graph) ist kein Teil von Version 1 (Modul M9 nur vorgemerkt). Das Programm hat keinen Zugriff auf einen Mandanten.
- **Signierung:** Programm und Skripte sind nicht signiert. `distribution-manifest.json` weist das aus.
- **x86-Zielgeräte:** Die Anforderung lautet 64-Bit-Windows. Ein 32-Bit-Herstellerprogramm läuft darauf weiter; reine 32-Bit-Windows-Geräte werden nicht unterstützt.
- **Das Content Prep Tool** wird nicht mitgeliefert (Lizenz). Wer es nicht beschaffen kann, kann keine `.intunewin`-Datei bauen.
- **Das PSAppDeployToolkit** wird nicht mitgeliefert: Die Veröffentlichung enthält `iNKORE.UI.WPF.Modern` mit Zusatzbedingungen (keine kommerzielle Nutzung ohne schriftliche Erlaubnis des Autors, der nutzende Code muss vollständig Open Source sein, kein Verkauf, sichtbare Namensnennung) und `iNKORE.UI.WPF` (LGPL-2.1). Ob das für Ihre Nutzung zulässig ist, ist eine Rechtsfrage und nicht geklärt; das Programm trifft sie nicht für Sie. Das fertige Paket enthält das Toolkit samt dieser Bibliotheken.

## Nicht geprüft

- **Die Toolkit-Engine (PSAppDeployToolkit) auf Geräten:** Dialoge in der Benutzersitzung, Logo, Banner, Farbe, Dialogstil, Verschieben, Ablauf des Zeitlimits, Programme schließen, Windows 11 und Windows Server 2019 und neuer. Die CI prüft nur mit dem echten Toolkit die Parametersätze, die Überlagerung und Läufe im stillen Modus (Prüfprotokoll T15 bis T18).
- **Der Update-Workflow für das Toolkit** (`update-psadt.yml`) ist noch nie gelaufen. Damit er Pull Requests anlegen darf, muss in den Repository-Einstellungen „Allow GitHub Actions to create and approve pull requests“ aktiviert sein. Eine neue Toolkit-Version wird nur auf bekannte Dateien und Lizenzdatei geprüft, nicht auf Verhalten.
- **Texte des Toolkits mit geschweiften Klammern** werden abgelehnt (das Toolkit liest `{Name}` als Nachschlagen).

- **Geräte- und Pilot-Tests (Spec §12.2)** sind nicht durchgeführt: reale MSI- und EXE-Pakete im SYSTEM-Kontext auf einem Gerät, Benutzerfenster in echten Sitzungen (mehrere Sitzungen, fehlende Sitzung, Parallelinstallation), Rückgabecodes und Neustartbedarf auf einem Gerät, Übernahme der Anleitung in eine Intune-Pilot-App. Der Wrapper ist nur mit Pester gegen echte Prozesse, Registry, Dateien und `msiexec` getestet.
- **Windows Server 2019:** Es gibt dafür keinen CI-Runner; die Unterstützung ist festgelegt, aber nicht auf einem Gerät geprüft.
- **Bedienungstests (Spec §12.2):** Skalierung 100 bis 200 %, kleine Fenstergröße, 1366×768-Ansicht, Tastaturbedienung, sichtbarer Fokus und Tab-Reihenfolge sind nicht geprüft; **das Erscheinungsbild der Fenster wurde nie begutachtet** (es gibt keine Screenshots eines laufenden Programms; die Bilder in der README sind Entwurfs-Mockups). Ebenso offen: ob eine IT-Person eine gewöhnliche MSI ohne Studium der Dokumentation paketieren kann.
- **Echtes Ablegen mit der Maus** ist nicht automatisiert; die UI-Tests starten das Programm mit `--open`, das dieselbe Prüfung ausführt. Die Dateidialoge ("Datei auswählen", "Neuen Installer wählen") sind nicht per UI-Automation geprüft.
- **Ein tatsächlich volles Laufwerk** und **Pfade über 259 Zeichen mit aktivierter Langpfad-Unterstützung** sind nicht real getestet.
- **Abbruch mitten im Content Prep Tool** ist nicht möglich; der Abbruch wird zwischen den Schritten geprüft.
- **Zwei gleichzeitige Installationen** auf einem Gerät: nicht getestet.
- **Fenster schließen während eines Baus:** Das Programm fragt nach; bestätigen Sie, wird der Bau nicht sauber abgebrochen. Ein Arbeitsordner (`%TEMP%\IntunePackageBuilder\<Build-ID>`, mit Markerdatei `.ipb-workspace`) kann liegen bleiben und enthält eine Kopie der Herstellerdateien; er darf von Hand gelöscht werden. Ein bereits gestartetes Content Prep Tool kann weiterlaufen. Ein automatisches Aufräumen alter Arbeitsordner gibt es nicht.
- **`build.log`** wird erst nach der Veröffentlichung geschrieben; scheitert das Schreiben, zeigt die Ergebnisseite trotzdem den Pfad. Die Datei steht nicht im `build-manifest.json`.
- **Rückgabecodes:** Intune legt bei einer neuen Win32-App den Code 1707 als Erfolg vor; die Anleitung nennt das nicht (nicht gegen Intune geprüft). Nicht aufgeführte Codes gibt der Wrapper unverändert weiter.
- **DPI:** Das Programm hat kein Manifest für Per-Monitor-DPI; WPF fällt auf die System-DPI zurück. Die Skalierung ist nicht geprüft (T04).
- **Die A18-Prüfung** vergleicht die Prüfsummen mit der Datei `SHA256SUMS.txt` aus derselben ZIP (selbstbezüglich); die Prüfsumme der ausgelieferten `Newtonsoft.Json.dll` wird zusätzlich gegen `THIRD-PARTY.md` geprüft. Eine Prüfsumme außerhalb der ZIP steht seit dem Release 0.0.1 in der Datei `.sha256` neben der ZIP (vom selben Workflow erzeugt).

## Bewusste Entscheidungen

- Der Wrapper ergänzt `ALLUSERS` bei einer MSI nicht; wer es braucht, gibt es in den Installationsparametern an. Hinweis dazu erscheint bei MSI ohne `ALLUSERS`.
- Das Programm rät keine EXE-Parameter.
- Ein Windows Server als Paketier-Arbeitsplatz ist nicht dasselbe wie ein von Intune verwaltbares Zielgerät.
