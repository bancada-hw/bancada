# Richtlinie zur Codesignierung

[English](en.md) · [Español](es.md) · [Português](pt.md) · [Français](fr.md) · **Deutsch** · [Italiano](it.md)

← [Zurück zur README](../../README.de.md)

## Signatur

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

Das heißt: SignPath.io stellt den Dienst zur Codesignierung kostenlos bereit, und das Zertifikat gehört der SignPath Foundation. Deshalb erscheint in den Eigenschaften der Windows-Datei **SignPath Foundation** als Signierer.

*Antrag läuft:* Solange die SignPath Foundation das Projekt nicht genehmigt hat, sind die veröffentlichten Dateien nicht signiert.

## Was signiert wird

- Nur **`Bancada-windows-x64.exe`**, die Windows-Datei jeder Version auf [GitHub Releases](https://github.com/bancada-hw/bancada/releases).
- Sie wird immer in GitHub Actions auf Rechnern von GitHub gebaut, aus dem Quellcode des Versions-Tags in [github.com/bancada-hw/bancada](https://github.com/bancada-hw/bancada), mit dem Workflow [`publicar.yml`](../../.github/workflows/publicar.yml). Nichts, was auf einem privaten Rechner gebaut wurde, wird signiert.
- Die Datei enthält freie Bibliotheken von Dritten (.NET, Avalonia und ihre Abhängigkeiten), aus ihrem Quellcode kompiliert. Programme oder Bibliotheken Dritter werden nicht separat signiert.
- Die Linux-Datei hat keine Authenticode-Signatur, weil Linux sie nicht verwendet. Jede Version veröffentlicht die SHA-256-Prüfsummen beider Dateien in `SHA256SUMS.txt`.

## Team und Rollen

| Rolle | Wer |
|---|---|
| Autoren (dürfen den Code ohne Review ändern) | [Bill Bull](https://github.com/TheBillBull), Mitglieder von [bancada-hw](https://github.com/orgs/bancada-hw/people) |
| Reviewer (prüfen jede Änderung von außerhalb des Teams, z. B. Pull Requests) | [Bill Bull](https://github.com/TheBillBull) |
| Genehmiger (genehmigen jede Signierung in SignPath von Hand) | [Bill Bull](https://github.com/TheBillBull) |

Alle Teammitglieder nutzen Zwei-Faktor-Authentifizierung bei GitHub und bei SignPath. Keine Version wird ohne ausdrückliche Freigabe eines Genehmigers signiert.

## Datenschutz

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

Bancada hat keine Telemetrie, keine Konten und keine Werbung und sendet keine Daten über den Rechner oder seine Nutzer. Es verbindet sich nur mit dem Internet, wenn du einen Test startest, der das braucht:

- **Netzwerk:** prüft die Verbindung, indem es einen Domainnamen auflöst, `1.1.1.1` anpingt und `https://www.google.com/generate_204` öffnet.
- **Internetgeschwindigkeit:** nur wenn du auf die Schaltfläche drückst, lädt Testdaten von und zu `speed.cloudflare.com`.
- **Mainboard:** vergleicht die Uhrzeit des Rechners mit der Internetzeit.
- **Akku, Surfmodus:** öffnet öffentliche Webseiten (Wikipedia, Zeitungen, GitHub…), um echte Nutzung zu simulieren.

Kamera und Mikrofon werden nie gespeichert. Der PDF-Bericht wird in deinem Ordner Dokumente gespeichert und nirgendwohin gesendet.

## Änderungen am System

Bancada ist eine einzelne Datei und wird nicht installiert. Es speichert nur seine Einstellungen (Sprache, Tastatur und den Stand langer Tests) in `%APPDATA%\Bancada` unter Windows oder `~/.config/Bancada` unter Linux, und Berichte dort, wo du willst. Zum Entfernen löschst du die Datei und diesen Ordner.

Manche Tests ändern Dinge nur, solange sie laufen, und zeigen das an: Der Akkutest passt die Helligkeit an und verhindert den Ruhezustand, der Ruhezustandstest versetzt den Rechner in den Ruhezustand, wenn du es anforderst. Danach ist alles wieder wie vorher.

Unter Linux baut das optionale Skript [`instalar.sh`](../../instalar.sh) Bancada und fügt es dem Anwendungsmenü hinzu; am Ende erklärt es, wie man es wieder deinstalliert.

## Ein Problem melden

Wenn du meinst, dass eine signierte Version von Bancada gegen diese Richtlinie verstößt, [eröffne ein Issue](https://github.com/bancada-hw/bancada/issues), und wir gehen dem nach.
