# Bekannte Grenzen

Stand: Version 0.0.1. Was hier steht, ist **nicht** umgesetzt oder **nicht geprüft**. Es ist kein Mangel, der "später kommt", sondern der ehrliche Stand.

## Nicht umgesetzt

- **Fortschrittsfenster auf dem Zielgerät** (Spec §8.2: Software, Aktion, Status, Laufzeit). Der Wrapper zeigt nur das Hinweisfenster zum Schließen von Programmen (`WTSSendMessage`). Für ein Fortschrittsfenster müsste aus SYSTEM ein Prozess in der Benutzersitzung gestartet oder eine geprüfte Bibliothek (zum Beispiel PSAppDeployToolkit) eingebunden werden; die Entscheidung steht in `PLANUNG.md` §8.
- **Direkter Upload nach Intune** (Microsoft Graph) ist kein Teil von Version 1 (Modul M9 nur vorgemerkt). Das Programm hat keinen Zugriff auf einen Mandanten.
- **Signierung:** Programm und Skripte sind nicht signiert. `distribution-manifest.json` weist das aus.
- **x86-Zielgeräte:** Die Anforderung lautet 64-Bit-Windows. Ein 32-Bit-Herstellerprogramm läuft darauf weiter; reine 32-Bit-Windows-Geräte werden nicht unterstützt.
- **Das Content Prep Tool** wird nicht mitgeliefert (Lizenz). Wer es nicht beschaffen kann, kann keine `.intunewin`-Datei bauen.

## Nicht geprüft

- **Geräte- und Pilot-Tests (Spec §12.2)** sind nicht durchgeführt: reale MSI- und EXE-Pakete im SYSTEM-Kontext auf einem Gerät, Benutzerfenster in echten Sitzungen (mehrere Sitzungen, fehlende Sitzung, Parallelinstallation), Rückgabecodes und Neustartbedarf auf einem Gerät, Übernahme der Anleitung in eine Intune-Pilot-App. Der Wrapper ist nur mit Pester gegen echte Prozesse, Registry, Dateien und `msiexec` getestet.
- **Windows Server 2019:** Es gibt dafür keinen CI-Runner; die Unterstützung ist festgelegt, aber nicht auf einem Gerät geprüft.
- **Bedienungstests (Spec §12.2):** Skalierung 100 bis 200 %, kleine Fenstergröße, 1366×768-Ansicht, Tastaturbedienung, sichtbarer Fokus und Tab-Reihenfolge sind nicht geprüft; **das Erscheinungsbild der Fenster wurde nie begutachtet** (es gibt keine Screenshots eines laufenden Programms; die Bilder in der README sind Entwurfs-Mockups). Ebenso offen: ob eine IT-Person eine gewöhnliche MSI ohne Studium der Dokumentation paketieren kann.
- **Echtes Ablegen mit der Maus** ist nicht automatisiert; die UI-Tests starten das Programm mit `--open`, das dieselbe Prüfung ausführt. Die Dateidialoge ("Datei auswählen", "Neuen Installer wählen") sind nicht per UI-Automation geprüft.
- **Ein tatsächlich volles Laufwerk** und **Pfade über 259 Zeichen mit aktivierter Langpfad-Unterstützung** sind nicht real getestet.
- **Abbruch mitten im Content Prep Tool** ist nicht möglich; der Abbruch wird zwischen den Schritten geprüft.
- **Zwei gleichzeitige Installationen** auf einem Gerät: nicht getestet.

## Bewusste Entscheidungen

- Der Wrapper ergänzt `ALLUSERS` bei einer MSI nicht; wer es braucht, gibt es in den Installationsparametern an. Hinweis dazu erscheint bei MSI ohne `ALLUSERS`.
- Das Programm rät keine EXE-Parameter.
- Ein Windows Server als Paketier-Arbeitsplatz ist nicht dasselbe wie ein von Intune verwaltbares Zielgerät.
