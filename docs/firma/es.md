# Política de firma de código

[English](en.md) · **Español** · [Português](pt.md) · [Français](fr.md) · [Deutsch](de.md) · [Italiano](it.md)

← [Volver al README](../../README.es.md)

## Firma

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

Es decir: SignPath.io presta gratis el servicio de firma de código y el certificado es de SignPath Foundation. Por eso, en las propiedades del ejecutable de Windows el firmante que aparece es **SignPath Foundation**.

*Solicitud en trámite:* hasta que SignPath Foundation apruebe el proyecto, los ejecutables publicados no van firmados.

## Qué se firma

- Solo **`Bancada-windows-x64.exe`**, el ejecutable de Windows de cada versión publicada en [GitHub Releases](https://github.com/bancada-hw/bancada/releases).
- Se compila siempre en GitHub Actions, en máquinas de GitHub, a partir del código de la etiqueta de la versión en [github.com/bancada-hw/bancada](https://github.com/bancada-hw/bancada), con el flujo [`publicar.yml`](../../.github/workflows/publicar.yml). Nunca se firma nada compilado en un ordenador personal.
- El ejecutable lleva dentro, compiladas desde su código fuente, bibliotecas libres de terceros (.NET, Avalonia y sus dependencias). No se firman programas ni bibliotecas de terceros por separado.
- El ejecutable de Linux no lleva firma de Authenticode, porque Linux no la usa. Cada versión publica las sumas SHA-256 de los dos ejecutables en `SHA256SUMS.txt`.

## Equipo y funciones

| Función | Quién |
|---|---|
| Autores (pueden cambiar el código sin revisión) | [Bill Bull](https://github.com/TheBillBull), miembros de [bancada-hw](https://github.com/orgs/bancada-hw/people) |
| Revisores (revisan cada cambio de fuera del equipo, por ejemplo las *pull requests*) | [Bill Bull](https://github.com/TheBillBull) |
| Aprobadores (aprueban a mano cada firma en SignPath) | [Bill Bull](https://github.com/TheBillBull) |

Todos los miembros del equipo usan autenticación en dos pasos en GitHub y en SignPath. Ninguna versión se firma sin que un aprobador lo autorice expresamente.

## Privacidad

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

Bancada no tiene telemetría, cuentas ni anuncios, y no envía datos del equipo ni de su usuario. Solo se conecta a Internet cuando ejecutas una prueba que lo necesita:

- **Red:** comprueba la conexión resolviendo un nombre de dominio, con un ping a `1.1.1.1` y abriendo `https://www.google.com/generate_204`.
- **Velocidad de Internet:** solo si pulsas el botón, descarga y sube datos de prueba con `speed.cloudflare.com`.
- **Placa base:** compara la hora del equipo con la de Internet.
- **Batería, modo navegación:** abre páginas web públicas (Wikipedia, periódicos, GitHub…) para simular un uso real.

La cámara y el micrófono no se guardan. El informe PDF se guarda en tu carpeta Documentos y no se envía a ningún sitio.

## Cambios en el sistema

Bancada es un solo archivo que no se instala. Solo guarda sus ajustes (idioma, teclado y el estado de las pruebas largas) en `%APPDATA%\Bancada` en Windows o `~/.config/Bancada` en Linux, y los informes donde tú digas. Para quitarlo, borra el ejecutable y esa carpeta.

Algunas pruebas cambian cosas solo mientras duran y lo avisan en pantalla: la de batería ajusta el brillo e impide la suspensión, y la de suspensión suspende el equipo cuando la pides. Al terminar, todo vuelve como estaba.

En Linux, el script opcional [`instalar.sh`](../../instalar.sh) compila Bancada y lo añade al menú de aplicaciones; al acabar explica cómo desinstalarlo.

## Avisar de un problema

Si crees que una versión firmada de Bancada incumple esta política, [abre una incidencia](https://github.com/bancada-hw/bancada/issues) y la investigaremos.
