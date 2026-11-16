# Building, installing from source and translating

**English** · [Español](es.md) · [Português](pt.md) · [Français](fr.md) · [Deutsch](de.md) · [Italiano](it.md)

← [Back to the README](../../README.md)

Bancada is built with **.NET 10** and **Avalonia 12**: a single project (`src/Bancada`) with the interface
written in C# and the styles in `Estilo/Estilos.axaml`. The code is written in Spanish.

## Building

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/Bancada          # run in development
```

On Windows, `publicar.ps1` builds both self-contained executables (Windows and Linux) into `dist/`.

## Installing on Linux from source

```bash
git clone https://github.com/bancada-hw/bancada.git
cd bancada
./instalar.sh
```

`instalar.sh` doesn't need sudo: it builds, installs into `~/.local` and adds Bancada to the applications menu.
If you don't have .NET 10, it downloads the official SDK inside the project folder just to build.
Minimal installs may lack `libice6` and `libsm6` (Debian/Ubuntu) or `libICE` and `libSM` (Fedora).

What Bancada uses on Linux:

- **Sound:** `pw-play`/`paplay`/`aplay` and `parecord`/`arecord` (`pulseaudio-utils` or `alsa-utils`).
- **Drives:** `udisksctl`; with `smartmontools` and sudo, full SMART.
- **Camera:** access to `/dev/video*` (`video` group).
- **Graphics:** OpenCL (NVIDIA with its driver; Intel `intel-opencl-icd`; AMD `mesa-opencl-icd` or ROCm).
- **Sleep:** `systemctl suspend`; with sudo, `rtcwake` so it wakes up by itself.
- **Battery test video:** GStreamer or mpv.

## Command-line options

Useful for documentation and automated tests:

```bash
Bancada --vista red --captura red.png --tema oscuro --completa   # screenshot of one view
Bancada --vista placa --informe report.pdf --espera 25           # PDF report without touching anything
Bancada --idioma en --vista bateria --captura battery.png       # in another language, without saving it
Bancada --anonimo --vista inicio --captura summary.png          # hides serial, MAC, IP and Wi-Fi names
Bancada --revision --minutos 1 --cada 15 --captura rev.png      # walks through the full check-up
Bancada --teclado de-DE --vista teclado                         # forces a keyboard layout
```

`--suspender` opens the sleep test and starts it by itself (it really puts the computer to sleep).

`pruebas/Bancada.Pruebas.cs` measures the real hardware without a UI and saves no images or audio:

```bash
dotnet run pruebas/Bancada.Pruebas.cs -- camara micro energia usb sensores estres memoria disco placa gpu
```

## Translating Bancada

No programming needed. Texts are written in Spanish inside `Tx("…")` or `Tf("… {0} …", value)` and each
language translates them in `src/Bancada/Idiomas/xx.json`.

1. Copy `src/Bancada/Idiomas/en.json` to `xx.json` (`ca.json`, `nl.json`…).
2. Change `"__nombre"` to the language's name written in that language (`"Nederlands"`).
3. Translate the **values** (the right-hand side). The keys are the original text and must not change.
   Keep the placeholders `{0}`, `{1}`… and the line breaks `\n`.
4. Check that nothing is missing:

   ```bash
   dotnet run herramientas/Traducir.cs -- claves src/Bancada src/Bancada/Idiomas/xx.json
   ```

5. Build: the language shows up in the selector by itself. Any untranslated sentence appears in Spanish.

If your language uses another keyboard layout, add it to `src/Bancada/Modelo/Teclados.cs`.
To translate this documentation and the website too, copy the `en` files in `docs/guia`, `docs/desarrollo`
and `docs/web/textos`, and open a pull request. The website is rebuilt with `python3 herramientas/generar-web.py`.

## Privacy

Bancada doesn't collect or send any data about the computer. Camera and microphone are never saved and never
leave the computer; the report is saved only to your Documents folder. It only goes online for:

- **Network, connection:** ping to `1.1.1.1`, resolving `www.wikipedia.org` and a request to `https://www.google.com/generate_204`.
- **Network, speed** (when you click the button): test data with `speed.cloudflare.com`.
- **Motherboard, clock:** time from `time.cloudflare.com` or `pool.ntp.org`.
- **Battery, browsing mode:** opens real web pages every 15 seconds while the test runs.
