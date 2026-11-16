# Bancada

Diagnóstico de hardware para portátiles y sobremesas, en **Windows y Linux**.
Pensado para técnicos que revisan, reparan o reacondicionan equipos: abres el programa, ves qué
lleva el equipo y vas comprobando que cada parte funciona.

Es un programa nativo (no es una web ni usa Electron): un único archivo de unos 47 MB que no
necesita instalación, no tiene publicidad, no pide cuenta y no envía datos a nadie.

> Estado: versión 0.2, en desarrollo. «Bancada» es un nombre provisional.

## Qué comprueba

| Prueba | Qué hace |
|---|---|
| **Resumen del equipo** | Marca, modelo y número de serie; tipo (portátil, sobremesa, todo en uno, mini PC); procesador; RAM y módulos por ranura; gráficas; discos con su estado; batería con su salud y ciclos; placa y BIOS. |
| **Revisión completa** | Guía paso a paso por las pruebas en las que hay que hacer algo (teclado, sonido, touchpad, pantalla, cámara, USB, batería y suspensión) mientras hace sola las demás en segundo plano, una detrás de otra (placa, discos, red, memoria, procesador y cada gráfica). Las pruebas de estrés esperan a que termine la de sonido para que el ventilador no moleste. Al final, el informe. Unos 20 minutos. |
| **Teclado** | Teclado completo dibujado en pantalla: cada tecla que llega se ilumina. Distribuciones española, latinoamericana, inglesa (EE. UU. y Reino Unido), portuguesa (Portugal y Brasil ABNT2), alemana (QWERTZ), francesa (AZERTY) e italiana, con su forma real (ISO, ANSI o ABNT2). Por defecto sigue al idioma y al teclado del sistema; se puede elegir otra y se recuerda. Recoge también las teclas multimedia (combinaciones con Fn). |
| **Touchpad y ratón** | Zona que se colorea por donde pasa el puntero (para encontrar partes del touchpad que no responden), botones izquierdo, derecho y central, doble clic, arrastre, desplazamiento vertical y lateral, botones laterales, pantalla táctil y lápiz. |
| **Pantalla** | Pantalla completa con colores lisos, degradados y cuadrícula para buscar píxeles muertos, manchas, fugas de luz y bandas. Lista los monitores conectados. |
| **Sonido** | Altavoz izquierdo y derecho por separado, barrido de graves a agudos para oír vibraciones, nivel del micrófono en directo y «grabar y escuchar». |
| **Cámara** | Imagen en directo de cada cámara con su resolución y fotogramas por segundo. Avisa si la imagen sale negra (tapa de privacidad). Se apaga sola al salir. |
| **Red** | Tarjetas de red (solo las físicas) con velocidad de enlace, IP y MAC; Wi-Fi con la conexión actual y las redes cercanas (señal, canal, banda); Bluetooth; conexión por pasos (router → DNS → Internet → web) para ver dónde se corta; y velocidad de bajada, subida y latencia. |
| **Puertos USB** | Se conecta algo en cada puerto y queda marcado con la velocidad real (USB 1, 2 o 3). Detecta aparatos USB 3 que funcionan a velocidad USB 2 (cable o conector dañado). Separa los dispositivos internos. |
| **Batería y cargador** | Capacidad de fábrica y actual, salud y ciclos; consumo, voltaje y tiempo restante en directo con gráfica; prueba de descarga (15 min, 30 min, 1 h o hasta el 20 %, en uso ligero o con el procesador al 100 %) que calcula la autonomía real y detecta caídas bruscas del porcentaje y un indicador de carga descalibrado; prueba guiada de desenchufar y enchufar el cargador. |
| **Discos** | Salud SMART (temperatura, horas de uso, vida consumida, sectores dañados, errores) y errores de lectura que ha registrado el sistema; velocidad real de lectura, escritura y lectura aleatoria de 4 KB con un archivo temporal que se borra al terminar. |
| **Memoria RAM** | Llena la memoria libre con 8 patrones (ceros, unos, damero, bits que caminan, dirección propia, aleatorio…) y comprueba cada bit. Acceso directo al diagnóstico completo al arrancar (Windows) o a MemTest86+ (Linux). |
| **Procesador y temperaturas** | Temperatura, frecuencia y uso en directo, todos los sensores y ventiladores disponibles, y prueba de estrés de 1 a 30 minutos con gráficas que detecta sobrecalentamiento, pérdida de rendimiento y errores de cálculo. |
| **Gráfica** | Se elige cuál probar por su nombre (la dedicada, la integrada o cualquier otra que haya) y se pone al máximo con un cálculo que se comprueba (detecta errores) y una animación en directo, vigilando temperatura, frecuencia, uso y consumo (NVIDIA con NVML; el resto con lo que da el sistema; en las integradas, la del procesador). Prueba aparte de la memoria de vídeo con patrones. Usa OpenCL, que viene con los controladores de NVIDIA, AMD e Intel. |
| **Suspensión** | Suspende el equipo y comprueba que vuelve: cuánto estuvo dormido, si la red se recupera y si algún dispositivo ha dejado de funcionar. Con suspensión clásica (S3) se despierta solo a los 30 segundos; con suspensión moderna (Modern Standby) se entra apagando la pantalla, como hace Windows, y lo despierta el técnico con una tecla (Windows no deja que un programa lo despierte). En Windows lee el registro del sistema para saber qué pasó de verdad: si entró en suspensión moderna o hibernó, cuánto duró, qué lo despertó y qué controlador la frenó. Si el equipo no vuelve y hay que forzar el apagado, al abrir Bancada otra vez lo marca como fallo. |
| **Placa base** | Comprobaciones automáticas: firmware y arranque seguro, chip TPM, dispositivos con fallos o sin controlador, enlaces PCI Express con menos líneas de las debidas, errores de hardware registrados por el sistema, reloj de la placa (pila CMOS) y cálculo correcto en cada núcleo del procesador. |
| **Informe** | Resumen de todas las pruebas con sus medidas y observaciones, en PDF (A4) listo para entregar o archivar. |

Cada prueba termina con un veredicto: **Funciona bien**, **Con defectos** o **No funciona**. Las que se
miden solas (red, discos, memoria, procesador, gráfica, suspensión, placa) lo proponen automáticamente y
el técnico puede cambiarlo.

## Idiomas

Bancada está en **español**, **inglés**, **portugués de Brasil**, **francés**, **alemán** e **italiano**. Al abrirlo usa el
idioma del sistema y se puede cambiar en la barra lateral; la elección se recuerda. El informe PDF
sale en el idioma elegido, con sus números y fechas (1,234.5 en inglés; 1 234,5 en francés), y el
teclado de la prueba sigue al idioma (AZERTY con el francés, QWERTZ con el alemán) salvo que se elija otro.

### Traducir Bancada a otro idioma

No hace falta programar:

1. Copia `src/Bancada/Idiomas/en.json` como `xx.json` (`de.json`, `it.json`, `ca.json`…).
2. Cambia `"__nombre"` por el nombre del idioma tal como se escribe en él (`"Deutsch"`).
3. Traduce los **valores** (el texto de la derecha). Las claves de la izquierda son el texto original
   en castellano y no se tocan. Mantén los huecos `{0}`, `{1}`… y los saltos de línea `\n`.
4. Comprueba que no falta nada:

   ```bash
   dotnet run herramientas/Traducir.cs -- claves src/Bancada src/Bancada/Idiomas/xx.json
   ```

5. Compila y abre Bancada: el idioma nuevo aparece en el selector.

Si una frase no está traducida, se muestra en castellano.

## Usarlo

### Windows (10 y 11)
Descarga `Bancada-windows-x64.exe` y ábrelo. No se instala nada.

### Linux
Descarga `Bancada-linux-x64`, dale permiso de ejecución y ábrelo:

```bash
chmod +x Bancada-linux-x64
./Bancada-linux-x64
```

O instálalo desde el código fuente (queda en el menú de aplicaciones):

```bash
git clone <repositorio> bancada
cd bancada
./instalar.sh
```

`instalar.sh` no necesita sudo. Si no tienes .NET 10, descarga el SDK oficial de Microsoft
dentro de la carpeta del proyecto, solo para compilar.

En instalaciones mínimas (servidores, WSL) pueden faltar dos librerías gráficas:
`sudo apt install libice6 libsm6` (Debian/Ubuntu) o `sudo dnf install libICE libSM` (Fedora).

En Linux, Bancada usa lo que ya traen las distribuciones de escritorio:

- **Sonido:** `pw-play`/`paplay`/`aplay` para reproducir y `parecord`/`arecord` para el micrófono (paquetes `pulseaudio-utils` o `alsa-utils`).
- **Salud de los discos:** `udisksctl` (udisks2); con `smartmontools` y sudo, los datos SMART completos.
- **Cámara:** acceso a `/dev/video*` (el usuario de la sesión lo tiene; si no, añádelo al grupo `video`).
- **Gráfica:** OpenCL. Con NVIDIA viene con su controlador; con Intel, `intel-opencl-icd`; con AMD, `mesa-opencl-icd` o ROCm.
- **Suspensión:** `systemctl suspend`; con sudo usa `rtcwake` para que el equipo se despierte solo.

Algunos datos solo se pueden leer como administrador: en Linux, el número de serie, los módulos
de RAM y el SMART completo; en Windows, el SMART de los discos SATA.

## Privacidad

Bancada no recoge ni envía datos del equipo. Las imágenes de la cámara y lo que graba el micrófono
no se guardan ni salen del equipo. El informe PDF se guarda solo en tu carpeta Documentos.

Solo sale a Internet en estas pruebas:

- **Red, conexión:** un ping a `1.1.1.1`, la resolución de `www.wikipedia.org` y una petición a `https://www.google.com/generate_204`.
- **Red, velocidad** (solo al pulsar el botón): descarga y subida de datos de prueba a `speed.cloudflare.com`.
- **Placa base, reloj:** consulta la hora a `time.cloudflare.com` (o `pool.ntp.org`) para ver si el reloj del equipo va bien.

## Compilar

Hace falta el [SDK de .NET 10](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/Bancada                  # ejecutar en desarrollo
```

En Windows, `publicar.ps1` genera los dos ejecutables (Windows y Linux) en `dist/`.

Para documentación y pruebas automáticas, el programa puede guardar una captura de cada vista:

```bash
Bancada --captura salida.png --vista red --tema oscuro --completa
Bancada --vista placa --informe informe.pdf --espera 25
Bancada --idioma en --captura salida.png --vista bateria        # en otro idioma, sin cambiar el guardado
Bancada --revision --minutos 1 --cada 15 --captura rev.png --informe rev.pdf   # recorre la revisión completa
```

`--suspender` abre la prueba de suspensión y la lanza sola (suspende el equipo de verdad); con `--captura`
espera a que vuelva para capturar. `dotnet run pruebas/Bancada.Pruebas.cs -- suspension` solo lee lo que
Windows registró de las últimas suspensiones, sin suspender nada.

Con `--cada N` saca una captura cada N segundos (`rev-001.png`, `rev-002.png`…): avanza un paso de la
revisión en cada una y, al acabar los pasos, enseña la prueba automática que esté en marcha. No pulsa
el botón de suspender.

Los textos visibles se escriben en castellano dentro de `Tx("…")` o `Tf("… {0} …", dato)`, y cada
idioma los traduce en `src/Bancada/Idiomas/xx.json`.

`pruebas/Bancada.Pruebas.cs` mide el hardware real sin interfaz (cámara, micrófono, batería, USB,
sensores, estrés, memoria, disco, placa, descarga, velocidad de Internet y gráfica). No guarda imágenes ni audio:

```bash
dotnet run pruebas/Bancada.Pruebas.cs -- camara micro energia usb sensores estres memoria disco placa gpu
```

## Hecho con

- [.NET 10](https://dotnet.microsoft.com) y [Avalonia UI](https://avaloniaui.net) (interfaz nativa multiplataforma).
- Fuente [Inter](https://rsms.me/inter/).

## Licencia

Copyright © 2026 Bill Bull. Bancada es software libre con licencia
[GPL-3.0 o posterior](LICENSE): puedes usarlo, copiarlo, modificarlo y distribuirlo, también
gratis o cobrando, siempre que las versiones que distribuyas sigan siendo libres y publiques
su código.

Además ([AVISO.md](AVISO.md)), toda versión distribuida debe mostrar en su interfaz y en sus
informes la mención «Basado en Bancada, de Bill Bull — https://github.com/bancada-hw/bancada»,
las versiones modificadas deben indicar que no son la oficial y no pueden usar el nombre ni el
logotipo de Bancada para identificarse.
