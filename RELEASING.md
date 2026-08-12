# Publier une version de Z-Codex

Mémo de publication. Rien ici n'est nécessaire pour utiliser ou compiler
Z-Codex : voir le [README](README.md) pour cela.

## La règle qui commande tout le reste

**L'étiquette de la release GitHub doit être le numéro de version précédé d'un
`v`.** Si `<Version>` vaut `1.0.1` dans `src/ZCodex.App/ZCodex.App.csproj`,
l'étiquette doit être exactement `v1.0.1`.

Ce n'est pas une convention d'esthétique. Depuis l'ajout de la détection de mises
à jour, l'application installée chez l'utilisateur interroge chaque jour l'API des
releases GitHub, lit cette étiquette et la compare à son propre numéro. Les deux
manières de se tromper sont silencieuses, et c'est ce qui les rend pénibles :

| Erreur | Ce qui se passe |
|---|---|
| Étiquette **plus basse** que la version publiée (oubli d'incrémenter) | personne n'est prévenu, la nouvelle version passe inaperçue |
| Étiquette **plus haute** que ce que contient l'installateur | tout le parc se voit proposer une mise à jour qu'il a déjà — à chaque démarrage, sans fin |

Aucune des deux ne produit d'erreur visible côté développement : le seul contrôle
est celui de l'étape 7.

## Le filet de sécurité

L'application interroge `/releases/latest`, qui **ignore les brouillons et les
préversions**. Une release laissée en *Draft*, ou cochée *pre-release*, n'alerte
donc personne. On peut préparer entièrement une publication, l'installer soi-même
et la vérifier avant de la rendre publique — c'est le mode de travail recommandé
ci-dessous.

## Marche à suivre

Exemple pour une version `1.0.1`.

**1. Partir d'un arbre propre.** Rien en attente, tout poussé.

```
git status
git push origin main
```

**2. Changer le numéro de version.** Une seule ligne, dans
`src/ZCodex.App/ZCodex.App.csproj` :

```xml
<Version>1.0.1</Version>
```

Tout le reste en découle — nom du fichier d'installation, propriétés de l'exe,
fenêtre « À propos », entrée dans « Applications installées ». Il n'y a aucun
autre endroit à mettre à jour.

**3. Commiter et pousser ce changement.**

```
git add src/ZCodex.App/ZCodex.App.csproj
git commit -m "Version 1.0.1"
git push origin main
```

**4. Fabriquer l'installateur.** Fermer Z-Codex d'abord : l'application verrouille
ses propres fichiers, et la publication échouerait.

```
powershell -ExecutionPolicy Bypass -File installer/build.ps1
```

Résultat dans `installer/output/Z-Codex-1.0.1-setup.exe`, environ 50 Mo. Le script
vide ce dossier à chaque fabrication : il ne peut jamais y traîner deux
installateurs de versions différentes.

**5. Installer soi-même le fichier produit, et lancer l'application.** C'est le
contrôle qui compte, et le seul qui attrape une version qui ne démarre pas. Un
`dotnet build` vert ne prouve rien sur l'installateur.

**6. Poser l'étiquette et pousser.**

```
git tag v1.0.1
git push origin v1.0.1
```

**7. Créer la release sur GitHub.** Sur
<https://github.com/LuxIudicium/Z-Codex/releases/new> :

- **Choose a tag** : `v1.0.1`, celle qui vient d'être poussée.
- **Title** : `1.0.1`.
- Décrire ce qui change, en français et en anglais.
- **Joindre `installer/output/Z-Codex-1.0.1-setup.exe`.** Une release sans
  l'installateur attaché envoie l'utilisateur sur une page vide.
- Publier en **Draft** tant que l'étape 8 n'est pas faite.

**8. Vérifier la détection avant de rendre public.** Depuis une machine — ou une
installation — encore en version précédente : **Aide ▸ Rechercher les mises à
jour**. Tant que la release est en brouillon, la réponse attendue est « Z-Codex
est à jour ». Publier la release, puis refaire le test : la modale doit cette fois
proposer la nouvelle version, et le bouton *Télécharger* ouvrir la page de cette
release précise.

Une fois cette vérification passée, la release peut sortir du mode brouillon.

## Ce qui n'est volontairement pas automatisé

L'application **ne télécharge ni n'installe** la mise à jour elle-même : elle
signale et ouvre la page. Décision du 12/08/2026, prise en connaissance du coût de
l'alternative. Deux raisons, et elles n'ont pas bougé :

- « Une application récupère un `.exe` et l'exécute » est le comportement même
  d'un logiciel malveillant. L'installateur de Z-Codex n'étant pas signé
  numériquement — choix assumé, le coût d'un certificat étant hors de proportion
  avec la diffusion du projet — le risque de blocage par un antivirus est réel.
- Une mise à jour automatique rend toute release immédiatement irrattrapable :
  une version qui plante au démarrage part chez tout le monde avant qu'on puisse
  la retirer. L'étape 8 ci-dessus perdrait son filet.
