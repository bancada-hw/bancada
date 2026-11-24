# Política de assinatura de código

[English](en.md) · [Español](es.md) · **Português** · [Français](fr.md) · [Deutsch](de.md) · [Italiano](it.md)

← [Voltar ao README](../../README.pt.md)

## Assinatura

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

Ou seja: a SignPath.io presta gratuitamente o serviço de assinatura de código e o certificado é da SignPath Foundation. Por isso, nas propriedades do executável do Windows, o signatário que aparece é a **SignPath Foundation**.

*Pedido em curso:* enquanto a SignPath Foundation não aprovar o projeto, os executáveis publicados não são assinados.

## O que é assinado

- Apenas o **`Bancada-windows-x64.exe`**, o executável do Windows de cada versão publicada no [GitHub Releases](https://github.com/bancada-hw/bancada/releases).
- É sempre compilado no GitHub Actions, em máquinas do GitHub, a partir do código da etiqueta da versão em [github.com/bancada-hw/bancada](https://github.com/bancada-hw/bancada), com o fluxo [`publicar.yml`](../../.github/workflows/publicar.yml). Nada compilado num computador pessoal é assinado.
- O executável inclui bibliotecas livres de terceiros (.NET, Avalonia e as suas dependências), compiladas a partir do código-fonte. Não são assinados programas nem bibliotecas de terceiros em separado.
- O executável do Linux não tem assinatura Authenticode, porque o Linux não a usa. Cada versão publica as somas SHA-256 dos dois executáveis em `SHA256SUMS.txt`.

## Equipa e funções

| Função | Quem |
|---|---|
| Autores (podem alterar o código sem revisão) | [Bill Bull](https://github.com/TheBillBull), membros de [bancada-hw](https://github.com/orgs/bancada-hw/people) |
| Revisores (revêem cada alteração de fora da equipa, por exemplo *pull requests*) | [Bill Bull](https://github.com/TheBillBull) |
| Aprovadores (aprovam manualmente cada assinatura na SignPath) | [Bill Bull](https://github.com/TheBillBull) |

Todos os membros da equipa usam autenticação de dois fatores no GitHub e na SignPath. Nenhuma versão é assinada sem autorização expressa de um aprovador.

## Privacidade

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

O Bancada não tem telemetria, contas nem anúncios, e não envia dados do computador nem do utilizador. Só se liga à Internet quando executas um teste que precisa disso:

- **Rede:** verifica a ligação resolvendo um nome de domínio, com um ping a `1.1.1.1` e abrindo `https://www.google.com/generate_204`.
- **Velocidade da Internet:** só se carregares no botão, descarrega e envia dados de teste com `speed.cloudflare.com`.
- **Placa-mãe:** compara a hora do computador com a da Internet.
- **Bateria, modo navegação:** abre páginas web públicas (Wikipédia, jornais, GitHub…) para simular uma utilização real.

A câmara e o microfone não são gravados. O relatório PDF é guardado na tua pasta Documentos e não é enviado para lado nenhum.

## Alterações no sistema

O Bancada é um único ficheiro que não se instala. Só guarda as suas definições (idioma, teclado e o estado dos testes longos) em `%APPDATA%\Bancada` no Windows ou `~/.config/Bancada` no Linux, e os relatórios onde indicares. Para o remover, apaga o executável e essa pasta.

Alguns testes alteram coisas apenas enquanto decorrem e avisam no ecrã: o da bateria ajusta o brilho e impede a suspensão, e o da suspensão suspende o computador quando o pedes. No fim, tudo volta ao que era.

No Linux, o script opcional [`instalar.sh`](../../instalar.sh) compila o Bancada e adiciona-o ao menu de aplicações; no fim explica como o desinstalar.

## Comunicar um problema

Se achas que uma versão assinada do Bancada não cumpre esta política, [abre um *issue*](https://github.com/bancada-hw/bancada/issues) e vamos investigar.
