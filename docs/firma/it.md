# Politica di firma del codice

[English](en.md) · [Español](es.md) · [Português](pt.md) · [Français](fr.md) · [Deutsch](de.md) · **Italiano**

← [Torna al README](../../README.it.md)

## Firma

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

Cioè: SignPath.io fornisce gratuitamente il servizio di firma del codice e il certificato appartiene a SignPath Foundation. Per questo, nelle proprietà dell'eseguibile di Windows, il firmatario indicato è **SignPath Foundation**.

*Richiesta in corso:* finché SignPath Foundation non approva il progetto, gli eseguibili pubblicati non sono firmati.

## Cosa viene firmato

- Solo **`Bancada-windows-x64.exe`**, l'eseguibile di Windows di ogni versione pubblicata su [GitHub Releases](https://github.com/bancada-hw/bancada/releases).
- Viene sempre compilato su GitHub Actions, su macchine di GitHub, dal codice dell'etichetta della versione in [github.com/bancada-hw/bancada](https://github.com/bancada-hw/bancada), con il workflow [`publicar.yml`](../../.github/workflows/publicar.yml). Niente di compilato su un computer personale viene firmato.
- L'eseguibile contiene librerie libere di terzi (.NET, Avalonia e le loro dipendenze), compilate dal loro codice sorgente. Programmi o librerie di terzi non vengono firmati separatamente.
- L'eseguibile per Linux non ha firma Authenticode, perché Linux non la usa. Ogni versione pubblica le somme SHA-256 dei due eseguibili in `SHA256SUMS.txt`.

## Squadra e ruoli

| Ruolo | Chi |
|---|---|
| Autori (possono modificare il codice senza revisione) | [Bill Bull](https://github.com/TheBillBull), membri di [bancada-hw](https://github.com/orgs/bancada-hw/people) |
| Revisori (rivedono ogni modifica esterna alla squadra, per esempio le *pull request*) | [Bill Bull](https://github.com/TheBillBull) |
| Approvatori (approvano a mano ogni firma su SignPath) | [Bill Bull](https://github.com/TheBillBull) |

Tutti i membri della squadra usano l'autenticazione a due fattori su GitHub e su SignPath. Nessuna versione viene firmata senza l'autorizzazione esplicita di un approvatore.

## Privacy

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

Bancada non ha telemetria, account né pubblicità, e non invia dati sul computer o sul suo utente. Si collega a Internet solo quando avvii una prova che ne ha bisogno:

- **Rete:** verifica la connessione risolvendo un nome di dominio, con un ping a `1.1.1.1` e aprendo `https://www.google.com/generate_204`.
- **Velocità di Internet:** solo se premi il pulsante, scarica e invia dati di prova con `speed.cloudflare.com`.
- **Scheda madre:** confronta l'ora del computer con quella di Internet.
- **Batteria, modalità navigazione:** apre pagine web pubbliche (Wikipedia, giornali, GitHub…) per simulare un uso reale.

Fotocamera e microfono non vengono mai registrati. Il rapporto PDF viene salvato nella tua cartella Documenti e non viene inviato da nessuna parte.

## Modifiche al sistema

Bancada è un solo file che non si installa. Salva solo le sue impostazioni (lingua, tastiera e lo stato delle prove lunghe) in `%APPDATA%\Bancada` su Windows o `~/.config/Bancada` su Linux, e i rapporti dove scegli tu. Per rimuoverlo, cancella l'eseguibile e quella cartella.

Alcune prove cambiano qualcosa solo mentre sono in corso e lo indicano sullo schermo: quella della batteria regola la luminosità e impedisce la sospensione, e quella della sospensione sospende il computer quando lo chiedi. Alla fine tutto torna com'era.

Su Linux, lo script facoltativo [`instalar.sh`](../../instalar.sh) compila Bancada e lo aggiunge al menu delle applicazioni; alla fine spiega come disinstallarlo.

## Segnalare un problema

Se pensi che una versione firmata di Bancada non rispetti questa politica, [apri una segnalazione](https://github.com/bancada-hw/bancada/issues) e la esamineremo.
