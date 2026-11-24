# Compilar, instalar desde el código y traducir

[English](en.md) · **Español** · [Português](pt.md) · [Français](fr.md) · [Deutsch](de.md) · [Italiano](it.md)

← [Volver al README](../../README.es.md)

Bancada está hecho con **.NET 10** y **Avalonia 12**: un solo proyecto (`src/Bancada`) con la interfaz
escrita en C# y los estilos en `Estilo/Estilos.axaml`.

## Compilar

Hace falta el [SDK de .NET 10](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/Bancada          # ejecutar en desarrollo
```

En Windows, `publicar.ps1` genera los dos ejecutables autocontenidos (Windows y Linux) en `dist/`.

Las versiones oficiales no salen de ahí: al subir una etiqueta `vX.Y.Z`, el flujo [`.github/workflows/publicar.yml`](../../.github/workflows/publicar.yml) compila los dos ejecutables en GitHub Actions, firma el de Windows con SignPath y deja un borrador de versión. Ver la [política de firma de código](../firma/es.md).

## Instalar en Linux desde el código

```bash
git clone https://github.com/bancada-hw/bancada.git
cd bancada
./instalar.sh
```

`instalar.sh` no necesita sudo: compila, instala en `~/.local` y añade Bancada al menú de aplicaciones.
Si no tienes .NET 10, descarga el SDK oficial dentro de la carpeta del proyecto solo para compilar.
En instalaciones mínimas pueden faltar `libice6` y `libsm6` (Debian/Ubuntu) o `libICE` y `libSM` (Fedora).

Lo que usa Bancada en Linux:

- **Sonido:** `pw-play`/`paplay`/`aplay` y `parecord`/`arecord` (`pulseaudio-utils` o `alsa-utils`).
- **Discos:** `udisksctl`; con `smartmontools` y sudo, el SMART completo.
- **Cámara:** acceso a `/dev/video*` (grupo `video`).
- **Gráfica:** OpenCL (NVIDIA con su controlador; Intel `intel-opencl-icd`; AMD `mesa-opencl-icd` o ROCm).
- **Suspensión:** `systemctl suspend`; con sudo, `rtcwake` para despertarlo solo.
- **Vídeo de la prueba de batería:** GStreamer o mpv.

## Opciones de línea de órdenes

Sirven para documentación y pruebas automáticas:

```bash
Bancada --vista red --captura red.png --tema oscuro --completa   # captura de una vista
Bancada --vista placa --informe informe.pdf --espera 25          # informe PDF sin tocar nada
Bancada --idioma en --vista bateria --captura bateria.png       # en otro idioma, sin cambiar el guardado
Bancada --anonimo --vista inicio --captura inicio.png           # oculta serie, MAC, IP y redes Wi-Fi
Bancada --revision --minutos 1 --cada 15 --captura rev.png      # recorre la revisión completa
Bancada --teclado de-DE --vista teclado                         # fuerza una distribución de teclado
```

`--suspender` abre la prueba de suspensión y la lanza sola (suspende el equipo de verdad).

`pruebas/Bancada.Pruebas.cs` mide el hardware real sin interfaz y no guarda imágenes ni audio:

```bash
dotnet run pruebas/Bancada.Pruebas.cs -- camara micro energia usb sensores estres memoria disco placa gpu
```

## Traducir Bancada a otro idioma

No hace falta programar. Los textos se escriben en castellano dentro de `Tx("…")` o `Tf("… {0} …", dato)`
y cada idioma los traduce en `src/Bancada/Idiomas/xx.json`.

1. Copia `src/Bancada/Idiomas/en.json` como `xx.json` (`ca.json`, `nl.json`…).
2. Cambia `"__nombre"` por el nombre del idioma escrito en él (`"Català"`).
3. Traduce los **valores** (lo de la derecha). Las claves son el texto original y no se tocan.
   Mantén los huecos `{0}`, `{1}`… y los saltos de línea `\n`.
4. Comprueba que no falta nada:

   ```bash
   dotnet run herramientas/Traducir.cs -- claves src/Bancada src/Bancada/Idiomas/xx.json
   ```

5. Compila: el idioma aparece solo en el selector. Si una frase no está traducida, sale en castellano.

Si tu idioma usa otra distribución de teclado, añádela en `src/Bancada/Modelo/Teclados.cs`.
Para traducir también esta documentación y la web, copia los archivos `es` de `docs/guia`,
`docs/desarrollo` y `docs/web/textos` y abre un *pull request*. La web se regenera con `python3 herramientas/generar-web.py`.

## Privacidad

Bancada no recoge ni envía datos del equipo. La cámara y el micrófono no se guardan ni salen del
equipo; el informe se guarda solo en tu carpeta Documentos. Solo sale a Internet en:

- **Red, conexión:** ping a `1.1.1.1`, resolución de `www.wikipedia.org` y petición a `https://www.google.com/generate_204`.
- **Red, velocidad** (al pulsar el botón): datos de prueba con `speed.cloudflare.com`.
- **Placa base, reloj:** hora de `time.cloudflare.com` o `pool.ntp.org`.
- **Batería, modo navegación:** abre páginas web reales cada 15 segundos mientras dura la prueba.
