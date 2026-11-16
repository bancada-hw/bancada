#!/usr/bin/env bash
# Instala Bancada en Linux desde el código fuente:
#
#   git clone <repositorio> bancada && cd bancada && ./instalar.sh
#
# Compila el programa, lo copia a ~/.local/bin/bancada y lo añade al menú de aplicaciones.
# Si no tienes el SDK de .NET 10, se descarga el oficial de Microsoft dentro de esta carpeta
# (./.dotnet), solo para compilar: no se instala nada en el sistema ni hace falta sudo.
set -euo pipefail
cd "$(dirname "$0")"

case "$(uname -m)" in
  x86_64) RID=linux-x64 ;;
  aarch64 | arm64) RID=linux-arm64 ;;
  *) echo "Arquitectura no compatible: $(uname -m)"; exit 1 ;;
esac

if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  DOTNET=dotnet
elif [ -x .dotnet/dotnet ]; then
  DOTNET="$PWD/.dotnet/dotnet"
else
  echo "› No hay SDK de .NET 10: se descarga en ./.dotnet (solo para compilar)…"
  if command -v curl >/dev/null 2>&1; then
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o .dotnet-install.sh
  else
    wget -q https://dot.net/v1/dotnet-install.sh -O .dotnet-install.sh
  fi
  bash .dotnet-install.sh --channel 10.0 --install-dir .dotnet --no-path >/dev/null
  rm -f .dotnet-install.sh
  DOTNET="$PWD/.dotnet/dotnet"
fi
# Sin telemetría y sin depender de libicu (falta en instalaciones mínimas; para compilar no hace falta).
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

echo "› Compilando Bancada ($RID)…"
"$DOTNET" publish src/Bancada/Bancada.csproj -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -p:DebugType=none \
  -o "artifacts/$RID" -v q --nologo

mkdir -p "$HOME/.local/bin" "$HOME/.local/share/applications" "$HOME/.local/share/icons/hicolor/scalable/apps"
install -m 755 "artifacts/$RID/Bancada" "$HOME/.local/bin/bancada"
install -m 644 recursos/bancada.svg "$HOME/.local/share/icons/hicolor/scalable/apps/bancada.svg"
# PNG por tamaños: los pequeños (16–32) son la versión simplificada, que se lee mejor en paneles y menús.
for t in 16 24 32 48 64 128 256 512; do
  mkdir -p "$HOME/.local/share/icons/hicolor/${t}x${t}/apps"
  install -m 644 "recursos/iconos/bancada-$t.png" "$HOME/.local/share/icons/hicolor/${t}x${t}/apps/bancada.png"
done
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q -t "$HOME/.local/share/icons/hicolor" 2>/dev/null || true
cat > "$HOME/.local/share/applications/bancada.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Bancada
Comment=Diagnóstico de hardware para portátiles y sobremesas
Exec=$HOME/.local/bin/bancada
Icon=bancada
Terminal=false
Categories=System;Utility;
EOF

# Librerías gráficas que faltan en algunas instalaciones mínimas.
LDCONFIG=$(command -v ldconfig || echo /sbin/ldconfig)
FALTAN=""
for lib in libICE.so.6 libSM.so.6 libX11.so.6 libfontconfig.so.1; do
  "$LDCONFIG" -p 2>/dev/null | grep -q "$lib" || FALTAN="$FALTAN $lib"
done

echo "✓ Bancada instalado. Ábrelo desde el menú de aplicaciones o escribe: bancada"
if [ -n "$FALTAN" ]; then
  echo "! Faltan librerías gráficas:$FALTAN"
  echo "  Debian / Ubuntu / Mint:  sudo apt install libice6 libsm6 libx11-6 libfontconfig1"
  echo "  Fedora:                  sudo dnf install libICE libSM libX11 fontconfig"
  echo "  Arch:                    sudo pacman -S libice libsm libx11 fontconfig"
fi
# Herramientas opcionales para las pruebas de sonido y de discos.
if ! command -v parecord >/dev/null 2>&1 && ! command -v arecord >/dev/null 2>&1; then
  echo "! Para probar el micrófono hace falta parecord o arecord:"
  echo "  sudo apt install pulseaudio-utils   (o alsa-utils)"
fi
if ! command -v smartctl >/dev/null 2>&1; then
  echo "  Opcional: instala smartmontools para ver la salud SMART completa de los discos (con sudo)."
fi
echo "  Para desinstalar: rm ~/.local/bin/bancada ~/.local/share/applications/bancada.desktop ~/.local/share/icons/hicolor/*/apps/bancada.*"
