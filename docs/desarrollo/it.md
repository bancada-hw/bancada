# Compilare, installare dal codice e tradurre

[English](en.md) · [Español](es.md) · [Português](pt.md) · [Français](fr.md) · [Deutsch](de.md) · **Italiano**

← [Torna al README](../../README.it.md)

Bancada è fatto con **.NET 10** e **Avalonia 12**: un solo progetto (`src/Bancada`) con l'interfaccia scritta in
C# e gli stili in `Estilo/Estilos.axaml`. Il codice è scritto in spagnolo.

## Compilare

Serve l'[SDK di .NET 10](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/Bancada          # eseguire in sviluppo
```

Su Windows, `publicar.ps1` genera i due eseguibili autonomi (Windows e Linux) in `dist/`.

## Installare su Linux dal codice

```bash
git clone https://github.com/bancada-hw/bancada.git
cd bancada
./instalar.sh
```

`instalar.sh` non richiede sudo: compila, installa in `~/.local` e aggiunge Bancada al menu delle applicazioni.
Se non hai .NET 10, scarica l'SDK ufficiale nella cartella del progetto solo per compilare.
Nelle installazioni minime possono mancare `libice6` e `libsm6` (Debian/Ubuntu) o `libICE` e `libSM` (Fedora).

Cosa usa Bancada su Linux:

- **Audio:** `pw-play`/`paplay`/`aplay` e `parecord`/`arecord` (`pulseaudio-utils` o `alsa-utils`).
- **Unità:** `udisksctl`; con `smartmontools` e sudo, lo SMART completo.
- **Fotocamera:** accesso a `/dev/video*` (gruppo `video`).
- **Scheda grafica:** OpenCL (NVIDIA con il suo driver; Intel `intel-opencl-icd`; AMD `mesa-opencl-icd` o ROCm).
- **Sospensione:** `systemctl suspend`; con sudo, `rtcwake` per riattivarlo da solo.
- **Video del test della batteria:** GStreamer o mpv.

## Opzioni da riga di comando

Servono per la documentazione e i test automatici:

```bash
Bancada --vista red --captura rete.png --tema oscuro --completa   # schermata di una vista
Bancada --vista placa --informe rapporto.pdf --espera 25          # rapporto PDF senza toccare nulla
Bancada --idioma it --vista bateria --captura batteria.png       # in un'altra lingua, senza salvarla
Bancada --anonimo --vista inicio --captura riepilogo.png         # nasconde numero di serie, MAC, IP e reti Wi-Fi
Bancada --revision --minutos 1 --cada 15 --captura rev.png       # percorre il controllo completo
Bancada --teclado it-IT --vista teclado                          # forza un layout di tastiera
```

`--suspender` apre il test di sospensione e lo avvia da solo (sospende davvero il computer).

`pruebas/Bancada.Pruebas.cs` misura l'hardware reale senza interfaccia e non salva immagini né audio:

```bash
dotnet run pruebas/Bancada.Pruebas.cs -- camara micro energia usb sensores estres memoria disco placa gpu
```

## Tradurre Bancada

Non serve programmare. I testi sono scritti in spagnolo dentro `Tx("…")` o `Tf("… {0} …", dato)` e ogni lingua
li traduce in `src/Bancada/Idiomas/xx.json`.

1. Copia `src/Bancada/Idiomas/en.json` come `xx.json` (`ca.json`, `nl.json`…).
2. Cambia `"__nombre"` con il nome della lingua scritto in essa (`"Nederlands"`).
3. Traduci i **valori** (a destra). Le chiavi sono il testo originale e non si toccano.
   Mantieni i segnaposto `{0}`, `{1}`… e gli a capo `\n`.
4. Verifica che non manchi nulla:

   ```bash
   dotnet run herramientas/Traducir.cs -- claves src/Bancada src/Bancada/Idiomas/xx.json
   ```

5. Compila: la lingua compare da sola nel selettore. Le frasi non tradotte appaiono in spagnolo.

Se la tua lingua usa un altro layout di tastiera, aggiungilo in `src/Bancada/Modelo/Teclados.cs`.
Per tradurre anche questa documentazione e il sito, copia i file `it` di `docs/guia`, `docs/desarrollo` e
`docs/web/textos` e apri una *pull request*. Il sito si rigenera con `python3 herramientas/generar-web.py`.

## Privacy

Bancada non raccoglie né invia dati sul computer. Fotocamera e microfono non vengono mai salvati e non escono mai
dal computer; il rapporto viene salvato solo nella cartella Documenti. Va su Internet solo per:

- **Rete, connessione:** ping a `1.1.1.1`, risoluzione di `www.wikipedia.org` e richiesta a `https://www.google.com/generate_204`.
- **Rete, velocità** (quando premi il pulsante): dati di prova con `speed.cloudflare.com`.
- **Scheda madre, orologio:** ora da `time.cloudflare.com` o `pool.ntp.org`.
- **Batteria, modalità navigazione:** apre pagine web reali ogni 15 secondi durante il test.
