# FIP Mini

Un mini lecteur natif Windows pour écouter [FIP](https://www.radiofrance.fr/fip) et ses webradios, sans navigateur.

Un seul exécutable d'environ 55 Ko, un seul fichier source C#, aucune dépendance à installer : tout repose sur ce que Windows fournit déjà.

> **Projet non officiel.** FIP Mini n'est ni affilié à Radio France, ni approuvé par elle. « FIP » et son logo sont des marques de Radio France.

## Fonctions

- **Lecture du direct** de FIP et de 12 webradios : Rock, Jazz, Groove, Monde, Nouveautés, Reggae, Electro, Metal, Pop, Hip-Hop, Sacré français, Cultes.
- **Infos du titre en cours** : titre, artiste, album, année, label, pochette, progression et titre suivant.
- **Contrôles média de Windows** : touches média du clavier et panneau média, avec titre, artiste et pochette. Les touches précédent/suivant changent de webradio.
- **Tiroir des webradios** : un clic sur le nom de la station déplie la liste, avec le titre et la pochette en cours sur chacune.
- **Zone de notification** : fermer la fenêtre la cache, la musique continue. Clic droit sur l'icône pour lecture/pause, stations et Quitter.
- **Léger** : environ 100 Mo de mémoire et moins de 1 % d'un cœur en lecture.

## Prérequis

- Windows 10 ou 11, 64 bits (les coins arrondis demandent Windows 11).
- .NET Framework 4.8, livré avec Windows.
- Pour les pochettes des webradios : l'extension « Images WebP » de Windows, généralement déjà installée.

## Compilation

Aucun SDK n'est nécessaire : le script utilise le compilateur C# livré avec Windows.

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

| Option | Effet |
|---|---|
| `-NoDownload` | N'utilise pas le logo FIP : icône neutre (`icon.svg`). |
| `-Out <chemin>` | Écrit l'exe ailleurs, utile si `FipMini.exe` est en cours d'exécution. |

### À propos du logo

Le logo FIP appartient à Radio France et **n'est pas distribué dans ce dépôt**. Par défaut, `build.ps1` le télécharge depuis radiofrance.fr au moment de la compilation, pour votre usage personnel. Si le téléchargement échoue, ou avec `-NoDownload`, l'icône neutre est utilisée. Ne redistribuez pas un exe contenant le logo.

## Utilisation

Lancez `FipMini.exe` : la lecture démarre aussitôt.

| Action | Effet |
|---|---|
| Clic sur le nom de la station | Ouvre ou ferme le tiroir des webradios |
| Clic droit dans la fenêtre | Menu : stations, toujours au premier plan, Quitter |
| Molette | Volume |
| Espace | Lecture / pause |
| Échap | Réduit la fenêtre |
| Clic sur la pochette | Ouvre le titre sur YouTube ou Apple Music |
| Bouton × | Cache la fenêtre dans la zone de notification |

En ligne de commande :

| Argument | Effet |
|---|---|
| `--drawer` | Démarre avec le tiroir ouvert |
| `--quit` | Ferme proprement l'instance déjà ouverte |

La pause coupe la connexion au flux au lieu de mettre en tampon : la reprise revient au direct.

### Ce que l'app écrit sur la machine

- `%APPDATA%\FipMini\settings.ini` : station, volume, position de la fenêtre.
- Un raccourci « FIP Mini » dans le menu Démarrer, créé au lancement. Windows en a besoin pour afficher le nom et l'icône de l'app dans le panneau média.

## Fonctionnement

- **Audio** : flux Icecast AAC de Radio France (`icecast.radiofrance.fr`), avec bascule sur le flux HLS en cas d'échec. Lecture par le `MediaPlayer` WinRT de Windows.
- **Métadonnées** : `api.radiofrance.fr/livemeta/pull/{id}` (détaillé) et `api.radiofrance.fr/livemeta/live/{id}/webrf_fip_player` (toutes les webradios).
- **Interface** : WPF, écrite entièrement en code dans `FipMini.cs`.

Ces API sont celles qu'utilise le site de Radio France. Elles ne sont pas documentées et peuvent changer ou disparaître sans préavis.

## Licence

[MIT](LICENSE) pour le code de ce dépôt. Les flux audio, les métadonnées, les pochettes et le logo restent la propriété de leurs ayants droit.
