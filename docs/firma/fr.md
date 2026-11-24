# Politique de signature de code

[English](en.md) · [Español](es.md) · [Português](pt.md) · **Français** · [Deutsch](de.md) · [Italiano](it.md)

← [Retour au README](../../README.fr.md)

## Signature

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

Autrement dit : SignPath.io fournit gratuitement le service de signature de code et le certificat appartient à SignPath Foundation. C'est pourquoi, dans les propriétés de l'exécutable Windows, le signataire affiché est **SignPath Foundation**.

*Demande en cours :* tant que SignPath Foundation n'a pas approuvé le projet, les exécutables publiés ne sont pas signés.

## Ce qui est signé

- Uniquement **`Bancada-windows-x64.exe`**, l'exécutable Windows de chaque version publiée sur [GitHub Releases](https://github.com/bancada-hw/bancada/releases).
- Il est toujours compilé sur GitHub Actions, sur des machines de GitHub, à partir du code de l'étiquette de la version dans [github.com/bancada-hw/bancada](https://github.com/bancada-hw/bancada), avec le workflow [`publicar.yml`](../../.github/workflows/publicar.yml). Rien de ce qui est compilé sur un ordinateur personnel n'est signé.
- L'exécutable contient des bibliothèques libres tierces (.NET, Avalonia et leurs dépendances), compilées à partir de leur code source. Aucun programme ni bibliothèque tiers n'est signé séparément.
- L'exécutable Linux n'a pas de signature Authenticode, car Linux ne l'utilise pas. Chaque version publie les sommes SHA-256 des deux exécutables dans `SHA256SUMS.txt`.

## Équipe et rôles

| Rôle | Qui |
|---|---|
| Auteurs (peuvent modifier le code sans relecture) | [Bill Bull](https://github.com/TheBillBull), membres de [bancada-hw](https://github.com/orgs/bancada-hw/people) |
| Relecteurs (relisent chaque modification venant de l'extérieur, par exemple les *pull requests*) | [Bill Bull](https://github.com/TheBillBull) |
| Approbateurs (approuvent à la main chaque signature sur SignPath) | [Bill Bull](https://github.com/TheBillBull) |

Tous les membres de l'équipe utilisent l'authentification à deux facteurs sur GitHub et sur SignPath. Aucune version n'est signée sans l'accord explicite d'un approbateur.

## Confidentialité

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

Bancada n'a ni télémétrie, ni comptes, ni publicités, et n'envoie aucune donnée sur l'ordinateur ou son utilisateur. Il ne se connecte à Internet que lorsque tu lances un test qui en a besoin :

- **Réseau :** vérifie la connexion en résolvant un nom de domaine, avec un ping vers `1.1.1.1` et en ouvrant `https://www.google.com/generate_204`.
- **Vitesse Internet :** uniquement si tu appuies sur le bouton, télécharge et envoie des données de test avec `speed.cloudflare.com`.
- **Carte mère :** compare l'heure de l'ordinateur avec l'heure d'Internet.
- **Batterie, mode navigation :** ouvre des pages web publiques (Wikipédia, journaux, GitHub…) pour simuler un usage réel.

La caméra et le micro ne sont jamais enregistrés. Le rapport PDF est enregistré dans ton dossier Documents et n'est envoyé nulle part.

## Modifications du système

Bancada est un fichier unique qui ne s'installe pas. Il enregistre seulement ses réglages (langue, clavier et état des tests longs) dans `%APPDATA%\Bancada` sous Windows ou `~/.config/Bancada` sous Linux, et les rapports là où tu le choisis. Pour le supprimer, efface l'exécutable et ce dossier.

Certains tests modifient des choses uniquement pendant leur durée et l'indiquent à l'écran : celui de la batterie règle la luminosité et empêche la mise en veille, et celui de la veille met l'ordinateur en veille quand tu le demandes. À la fin, tout redevient comme avant.

Sous Linux, le script facultatif [`instalar.sh`](../../instalar.sh) compile Bancada et l'ajoute au menu des applications ; à la fin, il explique comment le désinstaller.

## Signaler un problème

Si tu penses qu'une version signée de Bancada ne respecte pas cette politique, [ouvre un ticket](https://github.com/bancada-hw/bancada/issues) et nous l'examinerons.
