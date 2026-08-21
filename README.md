# Z-Codex

*[English version](README.en.md)*

Gestionnaire de builds d'équipe pour **Guild Wars 1**, successeur spirituel de paw\*ned².

Z-Codex sert à composer une équipe de 8 personnages, à en simuler les mécaniques
(dégâts, spike, armure, énergie, altérations) et à échanger les builds au format
attendu par le jeu.

Outil non officiel, gratuit, sans lien avec ArenaNet ni NCSOFT.

Auteur : **P. Vincent**.

---

## Ce qu'il fait

**Composition d'équipe**
- 8 personnages × 8 compétences, professions primaire et secondaire, attributs
- Variantes de build organisées en arborescence, reliées entre elles par des cadenas
- Catalogue de compétences filtrable, recherche, infobulles détaillées

**Simulation**
- **Spike** — calcul des dégâts d'une salve : ordre de cast, vol de vie, Deep Wound,
  coups critiques, buffs d'arme, effets de Grenth, dégâts conditionnels et à seuil
- **Dégâts selon l'armure** — AL 60/80/100/120 et niveaux personnalisés, armure
  ignorée, pénétration, dégâts d'arme des attaques
- **Calculateur d'armure** — insignes, runes, résistances, effets temporaires
- **Énergie** — Expertise, Rituels de la Nature (Quickening Zephyr, Ether Well…),
  boosts d'attribut (Aura of the Lich, Master of Magic…)
- **Altérations** — bandeau des 10 conditions, durées effectives et réduites
- **Flux** — les 12 flux du cycle mensuel, avec leur impact sur les calculs
- **Invocation** — durée des sorts d'arme, PV et armure des esprits et serviteurs

**Confort**
- **Builds de la communauté** — récupération des *build packs* de PvXwiki depuis le menu
  Extras : ~1 460 builds et ~250 équipes, déposés là où vous voulez, au format que
  Guild Wars lit lui-même
- Interface **française et anglaise**, permutable à chaud (drapeaux en haut à droite)
- Thème clair et sombre
- Trois tailles d'icônes
- Capture d'écran d'un build ou d'une équipe, prête à coller
- Annuler / rétablir
- Navigateur de fichiers avec aperçu sans ouvrir le build

## Formats de fichiers

| Extension | Rôle | Accès |
|---|---|---|
| `.zcx` | format natif — équipe complète, équipement, réglages | lecture / écriture |
| `.pn3` | ancien format natif | lecture / écriture |
| `.pwnd` | fichiers paw\*ned² | lecture / écriture |
| `.txt` | code template du jeu — compétences (`O…`) ou équipement (`P…`) | lecture / écriture |

Les codes template se copient-collent directement depuis et vers Guild Wars.

## Installation

Windows 10 ou 11, 64 bits. Aucun prérequis : le runtime .NET est fourni avec
l'application.

Téléchargez la dernière version depuis la
[page des releases](https://github.com/LuxIudicium/Z-Codex/releases/latest).

Lancez le `Z-Codex-…-setup.exe` téléchargé et suivez l'assistant. L'installation se fait
**pour votre compte uniquement**, dans `%LocalAppData%\Programs\Z-Codex` : elle ne
demande donc aucun droit administrateur.

Pour désinstaller, passez par **Paramètres ▸ Applications** ou par le raccourci du
menu Démarrer. Le désinstalleur vous demande s'il faut conserver vos builds et le
catalogue téléchargé ; répondre *oui* évite d'avoir à refaire le téléchargement
initial en cas de réinstallation.

### Premier lancement

**Une connexion internet est nécessaire au premier démarrage.**

Z-Codex ne distribue aucune donnée ni aucune image de Guild Wars. Il les récupère
lui-même, sur votre machine, depuis le [Guild Wars Wiki](https://wiki.guildwars.com)
public : 1 507 compétences, leurs tables de progression, leurs textes et
leurs icônes, soit environ 8 Mo.

**Comptez environ cinq minutes**, pendant lesquelles une fenêtre de progression
reste affichée. Le débit des requêtes est volontairement plafonné pour ne pas peser
sur le wiki, qui est un service communautaire gratuit. Cela n'a lieu qu'une fois :
ensuite l'application démarre en quelques secondes et fonctionne hors ligne.

Si le téléchargement est interrompu, relancez-le par **Extras ▸ Mettre à jour les
compétences**.

Quand une mise à jour du jeu est publiée, Z-Codex la détecte et propose de
rafraîchir son catalogue. Le rafraîchissement peut aussi être déclenché à la
demande par **Extras ▸ Mettre à jour les compétences**.

### Où sont rangées vos données

Tout est dans `%AppData%\Z-Codex` :

```
zcodex.db          catalogue des compétences
settings.json      préférences d'affichage
icons\             icônes des compétences
professions\  conditions\  stats\  flux.jpg
armor\  weapons\   images d'équipement, téléchargées à la demande
crash.log          uniquement en cas d'erreur — utile pour un rapport de bug
```

Vos builds, eux, sont enregistrés où vous le souhaitez.

Supprimer ce dossier remet l'application à neuf ; elle refera son téléchargement
initial au lancement suivant.

## Compiler depuis les sources

Il faut le [SDK .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0).

```
git clone https://github.com/LuxIudicium/Z-Codex.git
cd Z-Codex
dotnet build Z-Codex.sln
dotnet run --project src/ZCodex.App
```

Publication d'une version autonome, celle qu'empaquette l'installateur :

```
dotnet publish src/ZCodex.App -c Release -p:PublishProfile=win-x64-selfcontained
```

La sortie atterrit dans `src/ZCodex.App/bin/publish/win-x64/`.

### Fabriquer l'installateur

Il faut en plus [Inno Setup 6](https://jrsoftware.org/isinfo.php)
(`winget install JRSoftware.InnoSetup`).

```
powershell -ExecutionPolicy Bypass -File installer/build.ps1
```

Le script enchaîne la publication, la génération des illustrations de l'assistant et
la compilation, et dépose `installer/output/Z-Codex-<version>-setup.exe` — environ
50 Mo pour 155 Mo de charge. Les options `-SkipPublish` et `-SkipImages` permettent
de n'itérer que sur l'habillage.

Le numéro de version affiché partout est lu dans l'exécutable publié : il suffit de
changer `<Version>` dans `src/ZCodex.App/ZCodex.App.csproj`.

La marche à suivre pour publier une version — étiquetage, release GitHub et
vérification de la détection de mise à jour — est décrite dans
[RELEASING.md](RELEASING.md).

### Organisation du code

| Projet | Contenu |
|---|---|
| `ZCodex.App` | interface WPF, vues et *view models* |
| `ZCodex.Core` | modèles, calculs de jeu, codecs de template |
| `ZCodex.Data` | base SQLite (Entity Framework Core) |
| `ZCodex.Scraper` | lecture du wiki public |

Les commentaires du code sont en français.

## Licence et marques

Le code source est publié sous **licence MIT**, © 2026 P. Vincent (voir
[LICENSE](LICENSE)).

Guild Wars, ses extensions, ses compétences, ses icônes et son imagerie appartiennent
à ArenaNet, LLC et NCSOFT Corporation. **Aucune ressource du jeu n'est redistribuée
avec ce logiciel** : elle est téléchargée à l'exécution, sur la machine de
l'utilisateur, depuis le wiki public, et reste soumise aux conditions de réutilisation
de celui-ci.

Z-Codex est une œuvre indépendante, inspirée de paw\*ned² mais n'en partageant aucune
ligne de code. Sa prise en charge du `.pwnd` est un codec écrit à partir du seul format
de fichier, et le lit comme il l'écrit.

Les builds de la communauté proviennent de [PvXwiki](https://gwpvx.fandom.com), wiki
indépendant hébergé par Fandom. Écrits par ses contributeurs, ils sont placés sous
licence **CC BY-NC-SA 3.0** : Z-Codex les télécharge à l'exécution, sur la machine de
l'utilisateur, et n'en redistribue aucun. Ils restent soumis à cette licence une fois
importés — créditez PvXwiki si vous les repartagez, et pas d'usage commercial.

Le détail des mentions figure dans [LICENSE](LICENSE).
