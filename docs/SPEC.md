# Produktspezifikation: Generisches Intune-Paketierungstool

**Dokumentversion:** 1.0  
**Stand:** 8. Oktober 2026  
**Dokumenttyp:** Eigenständige Spezifikation für eine Neuentwicklung  
**Status:** Soll-Anforderungen; kein Nachweis einer vorhandenen Implementierung oder erfolgten Abnahme

## 1. Produktidee und Ziel

Entwickelt werden soll eine Windows-Desktopanwendung, mit der IT-Mitarbeiter aus einer MSI, einer EXE oder einem Herstellerordner ein vollständiges Microsoft-Intune-Win32-Paket erstellen können. Die Anwendung soll ohne längere Einarbeitung bedienbar sein und optisch wie ein sorgfältig gestaltetes Windows-Programm wirken.

Der häufigste Ablauf lautet:

> Installationsdatei ablegen → notwendige Angaben prüfen → Paket erstellen → Datei und Einstellungen nach Intune übernehmen.

Das Ergebnis besteht aus einer `.intunewin`-Datei, einem eigenständigen Erkennungsskript und einer konkreten Einrichtungsanleitung für Intune. Ein zentraler Grundordner bündelt Softwareprojekte, deren Versionen, Installationsquellen, Notizen und Ergebnisse. Bei Updates lassen sich die Einstellungen einer vorhandenen Version übernehmen.

Diese Spezifikation enthält alle für den Projektstart notwendigen Produktanforderungen. Sie setzt weder ein bestehendes Repository noch eine bestimmte Organisation, Namenskonvention oder vorherige Unterhaltung voraus. Produktname und Branding sind frei wählbar. In Beispielen wird ausschließlich der neutrale Arbeitsname **Intune Package Builder** verwendet.

## 2. Zielgruppe und Leitprinzipien

Zielgruppe sind IT-Administratoren und Softwareverantwortliche, die grundlegende Intune-Kenntnisse besitzen, aber keine eigenen Deployment-Skripte entwickeln möchten.

- **Einfacher Einstieg:** Der Standardmodus zeigt nur Angaben, die für das ausgewählte Setup benötigt werden.
- **Erweiterung bei Bedarf:** Sonderfälle und technische Optionen liegen im erweiterten Modus.
- **Nachvollziehbare Automatik:** Sicher auslesbare Werte werden übernommen; Vermutungen werden als Vorschlag gekennzeichnet.
- **Keine erfundenen EXE-Parameter:** Fehlende Herstellerangaben werden abgefragt.
- **Erhalt bestehender Arbeit:** Projekte, Notizen und Versionen bleiben beim Programmupdate erhalten.
- **Klare Rückmeldung:** Fortschritt, fehlende Angaben, Fehler und das fertige Ergebnis sind direkt sichtbar.
- **Prüfbarer Paketinhalt:** Jeder Build ist mit seiner Konfiguration und seinen Quellen nachvollziehbar.

Die Begriffe **MUSS**, **SOLL** und **KANN** bedeuten verbindliche Anforderung, bevorzugte Ausgestaltung und optionale Erweiterung.

## 3. Umfang der ersten Version

### Verbindlicher Funktionsumfang

1. Portable Windows-Anwendung mit EXE-Start und grafischer Oberfläche.
2. Standardmodus mit Dateiauswahl und Drag-and-drop für MSI/EXE.
3. Erweiterter Modus für vollständige Herstellerordner und zusätzliche Deployment-Optionen.
4. Auslesen von MSI-Metadaten und verfügbaren EXE-Dateimetadaten.
5. Zentraler, frei wählbarer Projektordner; Projektliste, Suche und zuletzt geöffnete Projekte.
6. Mehrere Versionen je Softwareprojekt, Übernahme von Einstellungen und Freitext-Notizen.
7. Hintergrundbau einer Intunewin-Datei mit Microsofts Win32 Content Prep Tool.
8. Installation und Deinstallation auf dem Zielgerät als LocalSystem.
9. Benutzerhinweise und konfigurierbares Fortschrittsfenster während des Deployments.
10. Erkennung anhand des tatsächlichen Softwarezustands.
11. Generierte HTML-Anleitung und maschinenlesbare Intune-Einstellungen.
12. Protokollierung, Quellenprüfung, sichere Dateiverarbeitung und automatisierte Ablaufprüfungen.

### Nicht Bestandteil des Standardumfangs

Direkter Upload nach Intune, Microsoft-Graph-Anmeldung, Gruppenzuweisungen, automatische Freigabe für Produktion, Store/MSIX-Paketierung, Treiberpakete, benutzerspezifische Installationen, Lizenzaktivierung und beliebige mehrstufige Installationsskripte gehören nicht zur ersten Standardversion. Solche Fälle dürfen nicht als automatisch unterstützt dargestellt werden.

Das Autorentool führt die importierte Software weder zur Analyse noch als Paketierungstest auf dem Paketier-PC aus. Installationstests finden getrennt auf ausdrücklich vorgesehenen Testgeräten statt.

## 4. Plattform und Auslieferung

### Autorentool

- Unterstützung für Windows 11 x64 und **Windows Server 2019 x64 und neuer (2019, 2022, 2025) mit Desktop Experience**.
- Keine Abhängigkeit von ausschließlich unter Windows 11 verfügbaren Oberflächenbibliotheken.
- Moderne, native Desktopdarstellung mit verlässlicher Tastaturbedienung und DPI-Skalierung.
- Eine geeignete Referenzarchitektur ist **C# mit WPF auf .NET Framework 4.8**. Eine andere Architektur ist zulässig, wenn sie dieselben Plattform-, Bedienungs- und Auslieferungsanforderungen erfüllt.
- Die Verwendung des Programms darf keine separate Entwicklungsumgebung und keine manuelle Installation von PowerShell 7 erfordern.
- Für die Paketierung sind grundsätzlich keine Administratorrechte erforderlich. Fehlende Schreibrechte müssen verständlich angezeigt werden.
- Das Programm wird als vollständiges portables Verzeichnis beziehungsweise ZIP ausgeliefert. Ein EXE-Starter darf mitgelieferte Module verwenden; eine technisch erzwungene Einzeldatei ist nicht erforderlich.
- Benutzerprojekte und Programmeinstellungen liegen getrennt von austauschbaren Programmdateien.
- Der Auslieferungsstand dokumentiert Version, Abhängigkeiten, Lizenzen und Signaturstatus. Eine nicht signierte Ausgabe darf nicht als signiert erscheinen.

Server Core ist kein Ziel für die grafische Anwendung. Die Unterstützung eines Servers als Paketier-Arbeitsplatz ist von den unterstützten Intune-Zielgeräten zu unterscheiden; daraus darf keine automatische Intune-Verwaltbarkeit dieses Servers abgeleitet werden.

### Erzeugte Deployment-Pakete

- Native Windows PowerShell 5.1 im 64-Bit-Prozess auf x64-Zielgeräten.
- Eigene PowerShell-Dateien als UTF-8 mit BOM; keine PowerShell-7-only-Syntax.
- Herstellerprogramme dürfen je nach freigegebener Konfiguration x86 oder x64 sein. Architektur und Erkennung müssen zusammenpassen.
- Eine Bibliothek wie PSAppDeployToolkit darf für Benutzerinteraktion und Sitzungsvermittlung eingesetzt werden. Verwendete Versionen müssen festgelegt, auf den Zielplattformen geprüft und mit Herkunft, Lizenz und Prüfsumme dokumentiert werden. Vendor-Dateien werden nicht direkt verändert.

## 5. Bedienkonzept

### 5.1 Startseite

Die Startseite MUSS ohne vorheriges Anlegen eines Projekts benutzbar sein. Sie enthält:

- eine auffällige Ablagefläche für eine MSI oder EXE;
- eine Schaltfläche „Installationsdatei auswählen“;
- die zuletzt geöffneten Projekte;
- eine umschaltbare Liste aller Projekte im Grundordner;
- eine Suche nach Projektname beziehungsweise Projekt-ID;
- den gewählten Grundordner und eine Möglichkeit, ihn zu ändern;
- eine Möglichkeit, einen bestehenden Projektordner zu öffnen.

Beim Erststart erscheint ein hilfreicher Leerzustand. Ungültige oder nicht erreichbare Projektpfade dürfen den Programmstart nicht verhindern. Ein nicht erreichbarer, bereits konfigurierter Grundordner darf nicht unbemerkt durch eine andere Ablage ersetzt werden.

### 5.2 Standardmodus

Der Standardmodus ist bei jedem Programmstart aktiv. Nach der Dateiauswahl werden nur die passenden Pflichtangaben eingeblendet.

| Angabe | MSI | EXE |
|---|---|---|
| Softwarename | Auslesen, editierbar | Dateimetadaten als Vorschlag, editierbar |
| Hersteller | Auslesen, editierbar | Dateimetadaten als Vorschlag, sonst manuell |
| Zielversion | Auslesen, prüfen | Vorschlag prüfen; muss zur Erkennung passen |
| ProductCode | Auslesen; kein normales Eingabefeld | Nicht erforderlich |
| Stille Installationsparameter | Sichere MSI-Vorgaben | Herstellerangabe erforderlich |
| Deinstallationsprogramm | Automatisch über ProductCode | Zielpfad erforderlich |
| Stille Deinstallationsparameter | Sichere MSI-Vorgaben | Herstellerangabe erforderlich |
| Installierte Erkennungsdatei | Nicht erforderlich | Zielpfad zu einer Datei mit Versionsinformation |

Eine MSI soll im Normalfall ohne zusätzliche technische Eingaben paketierbar sein. Bei einer EXE dürfen Pflichtwerte nicht durch vermeintlich universelle Schalter ersetzt werden. Beispiele im Formular sind ausdrücklich Beispiele.

„Paket erstellen“ bleibt gut erreichbar. Fehlende Angaben werden direkt am Formular erklärt; der Fokus springt zum betroffenen Feld. Ein neues Projekt wird erst angelegt, wenn die notwendigen Angaben gültig sind und der Benutzer den Bau startet.

### 5.3 Erweiterter Modus

Eine klar sichtbare Umschaltung öffnet zusätzliche Einstellungen. Dazu gehören:

- Übernahme eines vollständigen Herstellerordners mit Unterordnern;
- Auswahl des MSI-/EXE-Einstiegs innerhalb dieses Ordners;
- zusätzliche MSI-Eigenschaften;
- Projekt-ID bei einem neuen Projekt;
- Programme, die vor Installation beziehungsweise Deinstallation geschlossen sein müssen;
- gezieltes Entfernen gemeinsamer Verknüpfungen;
- Fortschrittstexte und Benutzerhinweise;
- Zeitlimit, Erfolgs- und Neustartcodes;
- erweiterte Ansicht der ausgelesenen Metadaten und der Quellenprüfung.

Ein Moduswechsel verändert keine Konfigurationswerte. Sind erweiterte Einstellungen aus einer Version übernommen worden, weist der Standardmodus darauf hin und bietet einen direkten Weg zur Prüfung.

### 5.4 Projektansicht

Die Projektansicht zeigt Name, Versionsliste und Freitext-Notizen. Hauptaktionen sind „Update erstellen“, „Version laden“ und „Neue Version ohne Vorlage“. Die Versionsliste wird nach numerischer Version sortiert; Build-Zeitpunkte sind ergänzende Informationen.

### 5.5 Ergebnisseite

Nach einem erfolgreichen Build zeigt die Anwendung:

- Software, Zielversion, Build-ID und Ausgabepfad;
- „Paketordner öffnen“;
- „Intune-Anleitung öffnen“;
- „Intune-Werte kopieren“;
- Installations- und Deinstallationsbefehl;
- Installationskontext und Erkennungsdatei;
- den Abschlussstatus und den zugehörigen Build-Logpfad.

Ein fehlgeschlagener Build darf nicht auf eine Erfolgsseite führen. Ein Ergebnis einer früheren Version darf nicht als Ergebnis des aktuellen Baus erscheinen.

## 6. Projekte, Versionen und Datenhaltung

### 6.1 Zentraler Grundordner

Der Benutzer wählt einen Grundordner. Darin liegen alle Softwareprojekte einschließlich Quellen, Konfigurationen, Notizen, Builds und Logs. Der Standardvorschlag ist ein beschreibbarer Ordner unter „Dokumente“, beispielsweise `Intune-Paketprojekte`.

Ein mögliches, normatives Ablageschema ist:

```text
Intune-Paketprojekte/
  beispiel-anwendung/
    project.json
    versions/
      1.0.0/
        configuration.json
        source-manifest.json
        source/
          setup.msi
        builds/
          <build-id>/
            beispiel-anwendung.intunewin
            intune/
              Detect-App.ps1
              Einrichtung.html
              Einstellungen.json
              Einstellungen.csv
            build-manifest.json
            configuration.snapshot.json
            build.log
```

Technische Zwischenverzeichnisse dürfen außerhalb dieser Struktur in einem kurzen, beschreibbaren Arbeitsverzeichnis liegen. Nach Abschluss müssen die nutzbaren Ergebnisse im Projekt verfügbar sein. Die Umsetzung muss Windows-Pfadlängen berücksichtigen; verschachtelte Bibliotheksverzeichnisse dürfen nicht erst während des Kopierens zu unverständlichen Fehlern führen.

### 6.2 Projektmodell

`project.json` enthält mindestens Schemasversion, unveränderliche Projekt-ID, Anzeigename, Erstellungszeitpunkt und Freitext-Notizen. Projekt-IDs sind dateisystemsicher; reservierte Windows-Namen und Pfadbestandteile sind unzulässig.

Projektnotizen sind unabhängig von einer Softwareversion. Sie werden explizit sowie beim regulären Verlassen der Projektansicht gespeichert. Schreibfehler müssen sichtbar sein. Ein unerwarteter Programmabbruch darf keine teilweise geschriebene Projektdatei hinterlassen.

### 6.3 Versionsmodell

Eine Version enthält mindestens:

| Bereich | Daten |
|---|---|
| Identität | Schema-Version, Projekt-ID, Softwarename, Hersteller, Zielversion |
| Quelle | Installertyp, relativer Installerpfad, Einzeldatei-/Ordnerimport, Quellenmanifest |
| Installation | Parameter, MSI ProductCode, Zielarchitektur |
| Deinstallation | ProductCode oder Programmdatei und Parameter |
| Erkennung | Verfahren, Zielpfad beziehungsweise ProductCode, Mindestversion |
| Benutzerinteraktion | Prozessliste, Installationstext, Deinstallationstext, Detailhinweis |
| Nacharbeiten | Explizit benannte gemeinsame Verknüpfungen |
| Laufzeit | Zeitlimit, Erfolgs-, Neustart- und Retry-Behandlung |

Datenformate müssen versioniert sein. Unbekannte zukünftige Formate dürfen nicht stillschweigend überschrieben werden. Migrationen müssen nachvollziehbar sein und eine Wiederherstellung ermöglichen.

### 6.4 Versionen und Builds auseinanderhalten

Die Softwareversion beschreibt den Zielzustand auf dem Endgerät. Eine Build-ID bezeichnet genau einen Paketbau. Eine Änderung am Verpackungswerkzeug oder eine neue Build-ID ist allein kein Grund, dieselbe Software erneut installieren zu lassen.

Gespeicherte Installationsquellen werden nicht stillschweigend überschrieben. Eine geänderte Setup-Datei erfordert eine neue Version beziehungsweise eine ausdrücklich modellierte neue Quellenrevision. Jeder Build erhält einen unveränderlichen Konfigurationssnapshot und ein Quellenmanifest. So bleiben frühere Ergebnisse nachvollziehbar, auch wenn spätere Konfigurationsänderungen zulässig sind.

### 6.5 Update-Ablauf

1. Projekt und Ausgangsversion wählen.
2. „Update erstellen“ anklicken.
3. Parameter, Erkennungspfade und erweiterte Einstellungen übernehmen.
4. Neue Installationsdatei oder neuen Herstellerordner auswählen.
5. ProductCode und MSI-Version erneut auslesen; EXE-Zielversion prüfen.
6. Neue Version und relevante geänderte Angaben bestätigen.
7. Neues Paket bauen und Ergebnis getrennt speichern.

Ein Wechsel des Installertyps darf inkompatible Parameter nicht unbemerkt übernehmen. In Version 1 wird hierfür eine neue Version ohne Vorlage verlangt. Die Quelldaten und Konfiguration der Ausgangsversion bleiben erhalten.

## 7. Quellenanalyse und Paketbau

### 7.1 Quellenanalyse

MSI-Metadaten werden über einen lesenden Zugriff auf die Installer-Datenbank ermittelt, ohne Installation oder Custom Actions auszuführen. Relevante Eigenschaften sind ProductName, ProductVersion, ProductCode und Manufacturer. Erkennbare externe Quelldateien müssen berücksichtigt oder als erforderlicher Ordnerimport kenntlich gemacht werden.

EXE-Metadaten werden ausschließlich aus Dateiinformationen gelesen. Setup-Version und Version der später installierten Erkennungsdatei können voneinander abweichen. Der Benutzer muss diesen Unterschied erkennen und die Zielversion korrigieren können.

Bei fehlgeschlagener Analyse bleibt ein zuvor vorhandener Entwurf erhalten. Ungültige Dateien, nicht unterstützte Typen und mehrere gleichzeitig abgelegte Installer werden verständlich zurückgewiesen.

### 7.2 Build-Ablauf

Der Build MUSS außerhalb des UI-Threads laufen und folgende Schritte abbilden:

1. Konfiguration und Schreibrechte prüfen.
2. Projektbezogene Buildsperre erwerben.
3. Quelle sichern und Prüfsummen aller übernommenen Dateien erfassen.
4. Kurzes, isoliertes Arbeitsverzeichnis anlegen.
5. Deployment-Wrapper, Konfiguration und Herstellerdateien bereitstellen.
6. Eigenständige Erkennung und Intune-Einstellungen generieren.
7. Win32 Content Prep Tool aufrufen.
8. Exitcode, Existenz und Struktur der erzeugten Intunewin-Datei prüfen.
9. Ergebnisdateien, Konfigurationssnapshot und Build-Manifest gemeinsam veröffentlichen.
10. Buildsperre freigeben und technische Zwischenverzeichnisse kontrolliert bereinigen.

Die Oberfläche zeigt mindestens aktuelle Phase und vergangene Zeit. Ein Prozentwert darf nur erscheinen, wenn er auf einer belastbaren Messung beruht. Während eines Builds sind Änderungen an dessen Eingaben sowie Projektwechsel gesperrt. Das Schließen des Programms muss den aktiven Build berücksichtigen. Eine spätere Abbruchfunktion darf nur sauber abgegrenzte Schritte abbrechen und keine halbfertigen Ergebnisse als erfolgreich veröffentlichen.

Zwei Programmprozesse dürfen dieselbe Projektversion nicht gleichzeitig schreiben. Bei einem Konflikt erscheint eine verständliche Meldung; verwaiste Sperren müssen kontrolliert auflösbar sein.

### 7.3 Sichere Dateiverarbeitung

Relative Installerpfade müssen innerhalb des gewählten Quellordners bleiben. Pfadtraversal, Junctions und symbolische Verknüpfungen werden für den Standardimport zurückgewiesen. Ein Ordner, der das Projekt, den Zielordner oder das Arbeitsverzeichnis selbst enthält, darf nicht rekursiv in sich kopiert werden.

Bereinigungen betreffen ausschließlich nachweislich vom jeweiligen Build angelegte Zwischenverzeichnisse. Originaldateien des Benutzers werden niemals gelöscht. Ein erneuter Build prüft die gespeicherten Quellen vor ihrer Verwendung auf Änderungen.

## 8. Verhalten auf dem Zielgerät

### 8.1 Kontext und Prozesse

Installation und Deinstallation laufen als **LocalSystem**, der Wrapper in nativer 64-Bit-Windows-PowerShell. Ein 32-Bit-Einstieg wird kontrolliert in den nativen Prozess überführt oder verständlich abgewiesen. Die eigentliche Herstellerinstallation läuft mit überprüften Parametern und gesetztem Arbeitsverzeichnis.

MSI verwendet eine stille Installation, Neustartunterdrückung und ein eigenes MSI-Log. EXE verwendet die ausdrücklich konfigurierte Befehlszeile. Die gestartete Installation muss bis zum tatsächlichen Abschluss verfolgt werden; ein Bootstrapper, der sich früh beendet, gilt nicht automatisch als erfolgreich.

### 8.2 Benutzerinteraktion

Eine als SYSTEM gestartete Installation darf ihr Fenster nicht ausschließlich in Session 0 anzeigen. Eine geeignete, geprüfte Sitzungsvermittlung stellt Hinweise in der erreichbaren Benutzersitzung dar.

Sind konfigurierte Anwendungen geöffnet, erhalten Benutzer Gelegenheit zum Speichern und selbstständigen Schließen oder zum Verschieben. Es gibt keine erzwungene Prozessbeendigung. Kann erforderliche Interaktion nicht sicher durchgeführt werden, wird mit einem definierten Retry-Ergebnis beendet. Verhalten bei fehlender Benutzersitzung, mehreren Sitzungen und parallelen Installationen muss ausdrücklich spezifiziert und getestet sein.

Das Fortschrittsfenster zeigt Software, Aktion, verständlichen Status und Laufzeit. Es darf einen aktiven Installationslauf nicht durch bloßes Schließen des Fensters unbeabsichtigt abbrechen. Download und Entschlüsselung durch Intune passieren vor dem Start dieses Wrappers und werden nicht als dessen Fortschritt ausgegeben.

### 8.3 Erkennung und Erfolg

Die Erkennung prüft den tatsächlichen Zielzustand und funktioniert auch für passende Installationen aus anderen Verteilwegen. Ein selbst geschriebener Marker oder die Version des Paketierwerkzeugs reicht nicht als Erkennungsnachweis.

- **MSI:** ProductCode vorhanden und installierte DisplayVersion mindestens so hoch wie die konfigurierte Zielversion; passende Registry-Ansichten berücksichtigen.
- **EXE:** konfigurierte Datei vorhanden und deren FileVersion mindestens so hoch wie die Zielversion.
- **Intune-Skriptvertrag:** Bei Erkennung Exitcode 0 mit nicht leerer Standardausgabe. Bei fehlendem Zielzustand keine positive Ausgabe und ein passender Fehler-/Nicht-erkannt-Exitcode.
- **Eigenständigkeit:** Das Erkennungsskript darf keine Dateien aus dem vorübergehenden Intune-Installationscache voraussetzen.
- **Fehlerfall:** Unlesbare oder ungültige Zustände ergeben keine fälschliche Erfolgsmeldung.

Nach Installation wird der Zielzustand geprüft; nach Deinstallation die Abwesenheit des vorgesehenen Produkts. Neuere oder fremde Produkte dürfen nicht durch eine zu breite Deinstallationssuche entfernt werden.

### 8.4 Rückgabecodes und Neustarts

Die Standardzuordnung sieht 0 als Erfolg, 3010 als erfolgreichen Abschluss mit Neustartbedarf und 1618 als erneuten Versuch vor. Zeitlimit und zusätzliche Herstellercodes sind konfigurierbar und müssen in Wrapper, UI und Intune-Dokumentation übereinstimmen.

Der Wrapper löst keinen erzwungenen Neustart aus. Der Code 1641 signalisiert einen bereits vom Installer ausgelösten Neustart und darf nicht als unauffälliger Standarderfolg behandelt werden. Ein solcher Herstellerinstaller benötigt eine Überarbeitung seiner Neustartparameter und einen erneuten Pilot.

### 8.5 Gezielte Nacharbeiten

Nach erfolgreicher Installation können ausdrücklich konfigurierte gemeinsame Verknüpfungen entfernt werden. Erlaubte Bereiche sind öffentlicher Desktop und gemeinsames Startmenü; zulässig sind nur einzelne `.lnk`-Dateien innerhalb dieser Wurzeln. Benutzerprofile, Dokumente und beliebige fremde Dateien sind ausgeschlossen. Fehlgeschlagene Nacharbeiten werden sichtbar protokolliert.

## 9. Intune-Ausgabe

Für jeden Build MUSS eine eigenständige HTML-Anleitung mit genau dessen Angaben erstellt werden. Die Anleitung ist lokal lesbar und druckbar. Sie erklärt mindestens:

| Intune-Bereich | Inhalt der Anleitung |
|---|---|
| App-Typ | Windows-App (Win32) |
| Paketdatei | Exakter Name der zugehörigen `.intunewin`-Datei |
| App-Informationen | Name, Hersteller, Zielversion und Beschreibung |
| Programm | Vollständiger Installations- und Deinstallationsbefehl |
| Installationsverhalten | System |
| Anforderungen | Unterstützte Architektur und für das konkrete Paket festgelegte OS-Anforderungen |
| Zeitlimit | Konkreter, mit dem Wrapper abgestimmter Wert |
| Neustartverhalten | Zum Wrapper und den Rückgabecodes passende Einstellung |
| Rückgabecodes | Jeder Code mit seiner Intune-Klassifizierung |
| Erkennung | Skriptdatei, 32-Bit-Einstellung und tatsächlicher Signaturstatus |
| Zuweisung | Pilot zuerst; Required/Available erklären; keine automatische Gruppenzuweisung |
| Abhängigkeiten/Ersetzungen | Nur angeben, wenn ausdrücklich modelliert; sonst keine erfinden |
| Fehleranalyse | Erwartete Logpfade und Prüfschritte |

Die Befehle müssen mit den erzeugten Startdateien übereinstimmen. Beispiel für eine mögliche Schnittstelle: `Install.cmd` und `Install.cmd -DeploymentType Uninstall`. JSON und CSV transportieren dieselben Werte. Die Kopierfunktion übernimmt die Werte des ausgewählten Builds, nicht eine globale Standardvorlage.

Die Anleitung warnt konkret davor, die Intunewin-Datei einer Version mit dem Erkennungsskript eines anderen Builds zu kombinieren. Sie darf keine erfolgreiche Tenant-Zuweisung oder Installation behaupten.

## 10. Logs, Fehlerbehandlung und Sicherheit

Es gibt drei getrennte Protokollebenen:

1. **Autorentool:** beispielsweise `%LOCALAPPDATA%\Intune Package Builder\Logs`, einschließlich Startfehlern vor dem Öffnen der UI.
2. **Paketbau:** im jeweiligen Projekt/Build, mit Phase, Zeit, Fehler, Build-ID und Werkzeugversionen.
3. **Zielgerät:** beispielsweise `C:\Windows\Logs\Intune\PackageDeploy\<Projekt-ID>`, einschließlich Deployment- und MSI-Logs.

Zielgeräte-Logs werden mit angemessenen Rechten für SYSTEM und Administratoren angelegt. Das Autorentool muss seine eigenen Logs ohne Administratorrechte schreiben können. Herstellerlogs können zusätzliche Pfade verwenden; die Anleitung benennt dies.

Fehlermeldungen erklären Ursache, betroffenen Schritt und nächste Handlung. Die UI zeigt keine unverständlichen technischen Ausnahmen als einzige Hilfestellung. Details bleiben für IT-Mitarbeiter im Log verfügbar. Ein Fehler beim Lesen eines Projekts oder beim Schreiben von Notizen darf keine anderen Projekte beschädigen.

Dateinamen, Metadaten, Notizen und Konfigurationswerte werden als Daten behandelt. Generierte JSON-, PowerShell-, HTML- und CSV-Inhalte sind für das jeweilige Format korrekt zu maskieren. Externe Prozesse werden ohne unsichere Verkettung von Shellbefehlen gestartet.

Passwörter, Tokens und Lizenzschlüssel sind kein regulärer Bestandteil der Konfiguration. Keine Telemetrie, kein Tenant-Zugriff und keine externen Schreibaktionen ohne ausdrücklich eingerichtete Funktion. Importierte Herstellerdateien und Benutzerprojekte dürfen nicht in die Programmdistribution oder ein Quellcode-Repository aufgenommen werden.

## 11. Technische Struktur und Wartbarkeit

Die Implementierung trennt mindestens:

- **Oberfläche:** Darstellung, Navigation, Eingabe und Fehleranzeigen;
- **Anwendungszustand:** aktives Projekt, Entwurf, Modus und laufender Build;
- **Projektverwaltung:** Laden, Speichern, Migration, Notizen, Versionen und Sperren;
- **Quellenanalyse:** MSI-Datenbank, EXE-Dateimetadaten und Manifestbildung;
- **Build-Dienst:** Validierung, Hintergrundausführung und Ergebniserstellung;
- **Paketgenerator:** Konfiguration, Wrapper, Erkennung und Intune-Anleitung;
- **Client-Laufzeit:** SYSTEM-Installation, Benutzerinteraktion und tatsächliche Erfolgskontrolle;
- **Abhängigkeiten:** versionierte, geprüfte Drittanbieterwerkzeuge.

Steuerelementreferenzen dürfen nicht als Ablage für Projektpfade oder andere Fachwerte wiederverwendet werden. Insbesondere bei PowerShell sind Namen wegen der fehlenden Unterscheidung von Groß-/Kleinschreibung eindeutig zu halten. Oberflächenzustand und Fachmodell benötigen getrennte Strukturen.

Lange Operationen laufen asynchron. Nur der UI-Thread verändert Oberflächenelemente. Ein abgeschlossener Hintergrundauftrag wird eindeutig seinem Projekt und Konfigurationssnapshot zugeordnet. Fehler und Abbruchzustände müssen Ressourcen, Dateisperren und temporäre Dateien kontrolliert freigeben.

Die Programmdistribution enthält nur benötigte Programmdateien, Abhängigkeiten, Lizenzen und Dokumentation. Ein Build prüft den fertigen Distributionsinhalt gegen die freigegebenen Quellen. Wiederholte Builds dürfen keine veralteten Dateien aus früheren Staging-Verzeichnissen übernehmen.

## 12. Qualität und Abnahmekriterien

Ein erfolgreicher Programmstart oder ein korrekt aussehender Screenshot ist kein ausreichender Funktionsnachweis. Die Abnahme besteht aus isolierten automatisierten Prüfungen sowie Geräte- und Bedienungstests.

### 12.1 Verbindliche automatisierte Prüfungen

| ID | Szenario | Erwartetes Ergebnis |
|---|---|---|
| A01 | Erststart ohne Einstellungen und Projekte | Standardmodus, verständliche leere Projektliste, Dateiauswahl möglich |
| A02 | MSI mit gültiger Metadaten-Datenbank einlesen | Richtige Werte ohne Installation oder Ausführung von Custom Actions |
| A03 | EXE ohne Silent-/Erkennungsangaben | Build gesperrt, verständlicher Hinweis, keine geratenen Parameter |
| A04 | Datei über Auswahl und Drag-and-drop übernehmen | Gleiches Ergebnis; falsche Typen/Mehrfachauswahl werden zurückgewiesen |
| A05 | Bestehendes Projekt mehrfach öffnen und wechseln | Keine Namenskollision oder verlorene Steuerelementreferenz; keine alte Quelle im neuen Projekt |
| A06 | Notizen ändern, Projekt verlassen und erneut öffnen | Notizen vollständig erhalten |
| A07 | Update aus Version erzeugen | Konfiguration übernommen, neue Quelle/Zielversion erforderlich, Ausgangsversion unverändert |
| A08 | Standard-/Erweitert-Modus wechseln | Werte bleiben erhalten; angepasste Einstellungen werden nicht verborgen zurückgesetzt |
| A09 | Paketbau auslösen | UI bleibt reaktionsfähig, Eingaben sind gesperrt, Fortschritt und Ergebnis sind korrekt |
| A10 | Dieselbe gespeicherte Version erneut bauen | Gespeicherte Quelle wird verwendet; eigenständiger neuer Build mit Snapshot |
| A11 | Gespeicherte Quelle nachträglich verändern | Änderung wird erkannt, kein stillschweigender Build mit falschem Manifest |
| A12 | Projekt enthaltenden Quellordner oder Junction importieren | Ablehnung vor rekursivem Kopieren |
| A13 | Fehlende Rechte, volle/unerreichbare Ablage oder fehlgeschlagenes Packwerkzeug | Nachvollziehbarer Fehler; kein falsches Erfolgsergebnis |
| A14 | Zweiter Prozess baut dieselbe Version | Exklusiver Zugriff oder verständlicher Konflikt |
| A15 | Ältere, passende neuere und fremdinstallierte Software erkennen | Verhalten entspricht ausschließlich der konfigurierten Erkennungsregel |
| A16 | Erkennungsskript ohne Paketcache starten | Unabhängige Erkennung funktioniert |
| A17 | HTML/JSON/CSV mit Sonderzeichen erzeugen | Korrekte Maskierung, keine beschädigten Befehle oder aktiven Inhalte aus Metadaten |
| A18 | Distribution prüfen | Nur freigegebene Dateien, richtige Prüfsummen, keine Projekte/Herstellerinstaller/Testartefakte |

Paketiertests verwenden inerte Dateien und kontrollierte MSI-Datenbanken. Sie führen keine Herstellerinstallation aus und ändern keine produktiven Geräteeinstellungen.

### 12.2 Geräte- und Bedienungstests

- Start und Standardablauf unter Windows 11 sowie Windows Server 2019 und neuer (mindestens 2019 und 2022) mit Desktop Experience.
- Bedienung bei kleiner Fenstergröße und 100 %, 125 %, 150 % und 200 % Skalierung; keine unerreichbaren Hauptaktionen.
- Kompakte Standard-MSI-Ansicht auf einem üblichen 1366×768-Arbeitsplatz ohne unnötiges Scrollen.
- Tastaturbedienung, sichtbarer Fokus, sinnvolle Tab-Reihenfolge und verständliche Beschriftungen.
- Eine IT-Person kann eine gewöhnliche MSI ohne Studium der technischen Dokumentation paketieren.
- Mindestens ein reales MSI- und ein reales EXE-Paket: Installation, Erkennung, Deinstallation und Wiederholung im SYSTEM-Kontext.
- Benutzerfenster in erreichbarer Sitzung; offene Prozesse, Verschieben, mehrere beziehungsweise fehlende Sitzungen und Parallelinstallation.
- Rückgabecodes und Neustartbedarf; keine unbeabsichtigten Prozessenden oder erzwungenen Neustarts.
- Übernahme der erzeugten Anleitung in eine Intune-Pilot-App und Überprüfung auf einem dafür vorgesehenen Zielgerät.

Das Prüfprotokoll nennt getestete Programmversionen, Herstellerpakete, Betriebssysteme und noch offene Punkte. Lokale Paketiertests dürfen nicht als Nachweis einer erfolgreichen Intune-Verteilung ausgegeben werden.

## 13. Liefergegenstände und Fertigstellung

Eine abnahmefähige erste Version umfasst:

1. Portable Programmdistribution mit EXE-Start.
2. Vollständige, nachvollziehbar baubare Quellen und ein Abhängigkeitsmanifest.
3. Eigenständige Bedienungsanleitung für Standardmodus, Projekte, Updates und Fehleranalyse.
4. Dokumentiertes Datenformat samt Migrationsstrategie.
5. Deployment-Vorlagen, Detection-Generator und Intune-Dokumentationsgenerator.
6. Automatisierte Prüfungen einschließlich tatsächlicher UI-Abläufe.
7. Prüfprotokoll der definierten Geräte- und Bedienungstests.
8. Änderungsverlauf und bekannte Grenzen.

Als fertig gilt die Version, wenn die verbindlichen Anforderungen und Abnahmekriterien erfüllt sind, die ausgelieferte ZIP dem geprüften Stand entspricht und verbleibende Einschränkungen konkret dokumentiert sind. Eine hübsche Oberfläche und erfolgreiches Packen allein ersetzen diese Abnahme nicht.

## 14. Mögliche spätere Erweiterungen

Die folgenden Funktionen sind Ideen für nachfolgende Versionen und keine stillschweigende Anforderung an Version 1:

- geprüfte, versionierte Herstellervorlagen mit Quellenangaben für Silent-Parameter;
- automatische Hinweise auf Unterschiede zwischen zwei Paketversionen;
- Signaturanzeige der Quellen und optionales Signieren eigener Skripte;
- App-Icons als Intune-Metadaten;
- zusätzliche Erkennungsverfahren und differenzierte Upgrade-Regeln;
- Export/Import kompletter Projekte und kontrollierte gemeinsame Ablage;
- direkte Intune-Veröffentlichung über Microsoft Graph mit Anmeldung, Vorschau und ausdrücklicher Freigabe;
- getrennte Testumgebung zur geführten Installation auf Testgeräten;
- optionale weitere Installertypen nach eigener Spezifikation und Abnahme.
