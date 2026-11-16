# Compilar, instalar a partir do código e traduzir

[English](en.md) · [Español](es.md) · **Português** · [Français](fr.md) · [Deutsch](de.md) · [Italiano](it.md)

← [Voltar ao README](../../README.pt.md)

A Bancada é feita com **.NET 10** e **Avalonia 12**: um único projeto (`src/Bancada`) com a interface escrita
em C# e os estilos em `Estilo/Estilos.axaml`. O código está escrito em espanhol.

## Compilar

É preciso o [SDK do .NET 10](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/Bancada          # executar em desenvolvimento
```

No Windows, `publicar.ps1` gera os dois executáveis autocontidos (Windows e Linux) em `dist/`.

## Instalar no Linux a partir do código

```bash
git clone https://github.com/bancada-hw/bancada.git
cd bancada
./instalar.sh
```

`instalar.sh` não precisa de sudo: compila, instala em `~/.local` e adiciona a Bancada ao menu de aplicativos.
Se você não tiver o .NET 10, ele baixa o SDK oficial dentro da pasta do projeto só para compilar.
Em instalações mínimas podem faltar `libice6` e `libsm6` (Debian/Ubuntu) ou `libICE` e `libSM` (Fedora).

O que a Bancada usa no Linux:

- **Som:** `pw-play`/`paplay`/`aplay` e `parecord`/`arecord` (`pulseaudio-utils` ou `alsa-utils`).
- **Discos:** `udisksctl`; com `smartmontools` e sudo, o SMART completo.
- **Câmera:** acesso a `/dev/video*` (grupo `video`).
- **Placa de vídeo:** OpenCL (NVIDIA com o driver; Intel `intel-opencl-icd`; AMD `mesa-opencl-icd` ou ROCm).
- **Suspensão:** `systemctl suspend`; com sudo, `rtcwake` para acordar sozinho.
- **Vídeo do teste de bateria:** GStreamer ou mpv.

## Opções de linha de comando

Servem para documentação e testes automáticos:

```bash
Bancada --vista red --captura rede.png --tema oscuro --completa   # captura de uma tela
Bancada --vista placa --informe relatorio.pdf --espera 25         # relatório PDF sem tocar em nada
Bancada --idioma pt --vista bateria --captura bateria.png        # em outro idioma, sem mudar o salvo
Bancada --anonimo --vista inicio --captura resumo.png            # oculta série, MAC, IP e redes Wi-Fi
Bancada --revision --minutos 1 --cada 15 --captura rev.png       # percorre a revisão completa
Bancada --teclado pt-BR --vista teclado                          # força um layout de teclado
```

`--suspender` abre o teste de suspensão e o inicia sozinho (suspende o computador de verdade).

`pruebas/Bancada.Pruebas.cs` mede o hardware real sem interface e não salva imagens nem áudio:

```bash
dotnet run pruebas/Bancada.Pruebas.cs -- camara micro energia usb sensores estres memoria disco placa gpu
```

## Traduzir a Bancada

Não é preciso programar. Os textos são escritos em espanhol dentro de `Tx("…")` ou `Tf("… {0} …", dado)` e
cada idioma os traduz em `src/Bancada/Idiomas/xx.json`.

1. Copie `src/Bancada/Idiomas/en.json` como `xx.json` (`ca.json`, `nl.json`…).
2. Troque `"__nombre"` pelo nome do idioma escrito nele (`"Nederlands"`).
3. Traduza os **valores** (o lado direito). As chaves são o texto original e não se mexem.
   Mantenha os marcadores `{0}`, `{1}`… e as quebras de linha `\n`.
4. Verifique se não falta nada:

   ```bash
   dotnet run herramientas/Traducir.cs -- claves src/Bancada src/Bancada/Idiomas/xx.json
   ```

5. Compile: o idioma aparece sozinho no seletor. Frases não traduzidas aparecem em espanhol.

Se o seu idioma usa outro layout de teclado, adicione-o em `src/Bancada/Modelo/Teclados.cs`.
Para traduzir também esta documentação e o site, copie os arquivos `pt` de `docs/guia`, `docs/desarrollo`
e `docs/web/textos` e abra um *pull request*. O site é gerado de novo com `python3 herramientas/generar-web.py`.

## Privacidade

A Bancada não coleta nem envia dados do computador. Câmera e microfone nunca são salvos nem saem do computador;
o relatório é salvo só na sua pasta Documentos. Ela só acessa a internet em:

- **Rede, conexão:** ping a `1.1.1.1`, resolução de `www.wikipedia.org` e requisição a `https://www.google.com/generate_204`.
- **Rede, velocidade** (ao clicar no botão): dados de teste com `speed.cloudflare.com`.
- **Placa-mãe, relógio:** hora de `time.cloudflare.com` ou `pool.ntp.org`.
- **Bateria, modo navegação:** abre páginas web reais a cada 15 segundos enquanto o teste dura.
