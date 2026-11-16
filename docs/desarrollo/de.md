# Kompilieren, aus dem Quellcode installieren und übersetzen

[English](en.md) · [Español](es.md) · [Português](pt.md) · [Français](fr.md) · **Deutsch** · [Italiano](it.md)

← [Zurück zum README](../../README.de.md)

Bancada ist mit **.NET 10** und **Avalonia 12** gebaut: ein einziges Projekt (`src/Bancada`), die Oberfläche in
C# geschrieben, die Stile in `Estilo/Estilos.axaml`. Der Code ist auf Spanisch geschrieben.

## Kompilieren

Sie brauchen das [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/Bancada          # in der Entwicklung starten
```

Unter Windows erzeugt `publicar.ps1` beide eigenständigen ausführbaren Dateien (Windows und Linux) in `dist/`.

## Unter Linux aus dem Quellcode installieren

```bash
git clone https://github.com/bancada-hw/bancada.git
cd bancada
./instalar.sh
```

`instalar.sh` braucht kein sudo: Es kompiliert, installiert nach `~/.local` und fügt Bancada dem Anwendungsmenü
hinzu. Ohne .NET 10 lädt es das offizielle SDK nur zum Kompilieren in den Projektordner.
Bei Minimalinstallationen fehlen eventuell `libice6` und `libsm6` (Debian/Ubuntu) bzw. `libICE` und `libSM` (Fedora).

Was Bancada unter Linux nutzt:

- **Ton:** `pw-play`/`paplay`/`aplay` und `parecord`/`arecord` (`pulseaudio-utils` oder `alsa-utils`).
- **Laufwerke:** `udisksctl`; mit `smartmontools` und sudo vollständiges SMART.
- **Kamera:** Zugriff auf `/dev/video*` (Gruppe `video`).
- **Grafikkarte:** OpenCL (NVIDIA mit Treiber; Intel `intel-opencl-icd`; AMD `mesa-opencl-icd` oder ROCm).
- **Energiesparmodus:** `systemctl suspend`; mit sudo `rtcwake`, damit er von selbst aufwacht.
- **Video für den Akkutest:** GStreamer oder mpv.

## Befehlszeilenoptionen

Nützlich für Dokumentation und automatische Tests:

```bash
Bancada --vista red --captura netz.png --tema oscuro --completa   # Screenshot einer Ansicht
Bancada --vista placa --informe bericht.pdf --espera 25           # PDF-Bericht, ohne etwas anzufassen
Bancada --idioma de --vista bateria --captura akku.png           # in einer anderen Sprache, ohne zu speichern
Bancada --anonimo --vista inicio --captura uebersicht.png        # verbirgt Seriennummer, MAC, IP und WLAN-Namen
Bancada --revision --minutos 1 --cada 15 --captura rev.png       # durchläuft die Gesamtprüfung
Bancada --teclado de-DE --vista teclado                          # erzwingt ein Tastaturlayout
```

`--suspender` öffnet den Test des Energiesparmodus und startet ihn selbst (der Computer schläft wirklich ein).

`pruebas/Bancada.Pruebas.cs` misst die echte Hardware ohne Oberfläche und speichert weder Bilder noch Ton:

```bash
dotnet run pruebas/Bancada.Pruebas.cs -- camara micro energia usb sensores estres memoria disco placa gpu
```

## Bancada übersetzen

Programmieren ist nicht nötig. Die Texte stehen auf Spanisch in `Tx("…")` oder `Tf("… {0} …", Wert)`, und jede
Sprache übersetzt sie in `src/Bancada/Idiomas/xx.json`.

1. Kopieren Sie `src/Bancada/Idiomas/en.json` als `xx.json` (`ca.json`, `nl.json` …).
2. Ändern Sie `"__nombre"` in den Namen der Sprache, in ihr selbst geschrieben (`"Nederlands"`).
3. Übersetzen Sie die **Werte** (rechts). Die Schlüssel sind der Originaltext und bleiben unverändert.
   Behalten Sie die Platzhalter `{0}`, `{1}` … und die Zeilenumbrüche `\n` bei.
4. Prüfen Sie, ob nichts fehlt:

   ```bash
   dotnet run herramientas/Traducir.cs -- claves src/Bancada src/Bancada/Idiomas/xx.json
   ```

5. Kompilieren: Die Sprache erscheint von selbst in der Auswahl. Nicht übersetzte Sätze erscheinen auf Spanisch.

Wenn Ihre Sprache ein anderes Tastaturlayout nutzt, fügen Sie es in `src/Bancada/Modelo/Teclados.cs` hinzu.
Um auch diese Dokumentation und die Website zu übersetzen, kopieren Sie die `de`-Dateien in `docs/guia`,
`docs/desarrollo` und `docs/web/textos` und öffnen Sie einen *Pull Request*. Die Website wird mit `python3 herramientas/generar-web.py` neu erzeugt.

## Datenschutz

Bancada erhebt und sendet keine Daten über den Computer. Kamera und Mikrofon werden nie gespeichert und verlassen
nie den Computer; der Bericht wird nur in Ihrem Ordner „Dokumente“ gespeichert. Ins Internet geht es nur bei:

- **Netzwerk, Verbindung:** Ping an `1.1.1.1`, Auflösung von `www.wikipedia.org` und Anfrage an `https://www.google.com/generate_204`.
- **Netzwerk, Geschwindigkeit** (beim Klick auf die Schaltfläche): Testdaten mit `speed.cloudflare.com`.
- **Mainboard, Uhr:** Zeit von `time.cloudflare.com` oder `pool.ntp.org`.
- **Akku, Surfmodus:** öffnet während des Tests alle 15 Sekunden echte Webseiten.
