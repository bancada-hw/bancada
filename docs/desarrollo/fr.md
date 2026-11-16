# Compiler, installer depuis les sources et traduire

[English](en.md) · [Español](es.md) · [Português](pt.md) · **Français** · [Deutsch](de.md) · [Italiano](it.md)

← [Retour au README](../../README.fr.md)

Bancada est développé avec **.NET 10** et **Avalonia 12** : un seul projet (`src/Bancada`), l'interface écrite
en C# et les styles dans `Estilo/Estilos.axaml`. Le code est écrit en espagnol.

## Compiler

Il faut le [SDK .NET 10](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/Bancada          # lancer en développement
```

Sous Windows, `publicar.ps1` génère les deux exécutables autonomes (Windows et Linux) dans `dist/`.

## Installer sous Linux depuis les sources

```bash
git clone https://github.com/bancada-hw/bancada.git
cd bancada
./instalar.sh
```

`instalar.sh` n'a pas besoin de sudo : il compile, installe dans `~/.local` et ajoute Bancada au menu des
applications. Sans .NET 10, il télécharge le SDK officiel dans le dossier du projet, juste pour compiler.
Les installations minimales peuvent manquer de `libice6` et `libsm6` (Debian/Ubuntu) ou `libICE` et `libSM` (Fedora).

Ce que Bancada utilise sous Linux :

- **Son :** `pw-play`/`paplay`/`aplay` et `parecord`/`arecord` (`pulseaudio-utils` ou `alsa-utils`).
- **Disques :** `udisksctl` ; avec `smartmontools` et sudo, le SMART complet.
- **Caméra :** accès à `/dev/video*` (groupe `video`).
- **Carte graphique :** OpenCL (NVIDIA avec son pilote ; Intel `intel-opencl-icd` ; AMD `mesa-opencl-icd` ou ROCm).
- **Veille :** `systemctl suspend` ; avec sudo, `rtcwake` pour un réveil automatique.
- **Vidéo du test de batterie :** GStreamer ou mpv.

## Options de ligne de commande

Utiles pour la documentation et les tests automatiques :

```bash
Bancada --vista red --captura reseau.png --tema oscuro --completa   # capture d'une vue
Bancada --vista placa --informe rapport.pdf --espera 25             # rapport PDF sans rien toucher
Bancada --idioma fr --vista bateria --captura batterie.png         # dans une autre langue, sans l'enregistrer
Bancada --anonimo --vista inicio --captura resume.png              # masque n° de série, MAC, IP et réseaux Wi-Fi
Bancada --revision --minutos 1 --cada 15 --captura rev.png         # parcourt le bilan complet
Bancada --teclado fr-FR --vista teclado                            # force une disposition de clavier
```

`--suspender` ouvre le test de veille et le lance seul (il met vraiment l'ordinateur en veille).

`pruebas/Bancada.Pruebas.cs` mesure le matériel réel sans interface et n'enregistre ni image ni son :

```bash
dotnet run pruebas/Bancada.Pruebas.cs -- camara micro energia usb sensores estres memoria disco placa gpu
```

## Traduire Bancada

Pas besoin de programmer. Les textes sont écrits en espagnol dans `Tx("…")` ou `Tf("… {0} …", donnée)` et
chaque langue les traduit dans `src/Bancada/Idiomas/xx.json`.

1. Copiez `src/Bancada/Idiomas/en.json` en `xx.json` (`ca.json`, `nl.json`…).
2. Remplacez `"__nombre"` par le nom de la langue écrit dans cette langue (`"Nederlands"`).
3. Traduisez les **valeurs** (à droite). Les clés sont le texte d'origine et ne changent pas.
   Gardez les marqueurs `{0}`, `{1}`… et les retours à la ligne `\n`.
4. Vérifiez qu'il ne manque rien :

   ```bash
   dotnet run herramientas/Traducir.cs -- claves src/Bancada src/Bancada/Idiomas/xx.json
   ```

5. Compilez : la langue apparaît seule dans le sélecteur. Une phrase non traduite s'affiche en espagnol.

Si votre langue utilise une autre disposition de clavier, ajoutez-la dans `src/Bancada/Modelo/Teclados.cs`.
Pour traduire aussi cette documentation et le site, copiez les fichiers `fr` de `docs/guia`, `docs/desarrollo`
et `docs/web/textos`, puis ouvrez une *pull request*. Le site se régénère avec `python3 herramientas/generar-web.py`.

## Confidentialité

Bancada ne collecte ni n'envoie aucune donnée sur l'ordinateur. La caméra et le micro ne sont jamais enregistrés
et ne quittent jamais l'ordinateur ; le rapport est enregistré uniquement dans votre dossier Documents. Il ne va
sur Internet que pour :

- **Réseau, connexion :** ping vers `1.1.1.1`, résolution de `www.wikipedia.org` et requête vers `https://www.google.com/generate_204`.
- **Réseau, vitesse** (au clic sur le bouton) : données de test avec `speed.cloudflare.com`.
- **Carte mère, horloge :** heure de `time.cloudflare.com` ou `pool.ntp.org`.
- **Batterie, mode navigation :** ouvre de vraies pages web toutes les 15 secondes pendant le test.
