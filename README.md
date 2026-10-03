# Fonte

Carnet de musculation hors ligne : on note chaque série pendant la séance, l'app retrouve ce qui a été fait la fois d'avant, décompte le repos et repère les records ; elle propose des programmes dont les charges montent toutes seules, montre les progrès semaine après semaine et suit le poids, les mensurations et les photos. En **.NET MAUI 10** pour **iPhone, Android et Mac**.

| Accueil | Séance en cours | Fin de séance | Fiche exercice | Bibliothèque | Mode sombre |
|---|---|---|---|---|---|
| ![](docs/01-home-light.png) | ![](docs/02-workout-light.png) | ![](docs/03-summary-light.png) | ![](docs/04-exercise-light.png) | ![](docs/05-library-light.png) | ![](docs/02-workout-dark.png) |

## Fonctionnalités

- **Bienvenue** (premier lancement, en 3 étapes) : ce que fait l'app, l'**objectif** (force, muscle, forme) et le nombre de **séances par semaine**, puis un programme prêt à l'emploi qui leur correspond, à suivre ou non. L'objectif règle le temps de repos et les répétitions proposées.
- **Séances** (premier onglet) :
  - la **prochaine séance du programme** suivi (« Semaine 3 sur 8 · Haut / Bas »), à lancer en une touche, ou une séance libre ;
  - la semaine en cours face à l'**objectif hebdomadaire** (« 2 / 4 séances ») et la **série de semaines** réussies d'affilée ;
  - les dernières séances, et l'**historique** : calendrier du mois, nombre de séances, durée totale, volume, semaines régulières.
- **Programmes** (second onglet) :
  - le programme suivi : semaine en cours, ses séances dans l'ordre (A, B, C…), la prochaine, celles faites et quand ;
  - **programmes prêts à l'emploi** : Full body (3×/sem.), 5×5 (force), Haut / Bas (4×/sem.), Push Pull Legs (6×/sem.), adaptés à l'objectif ;
  - ses propres programmes et séances (« Bras », « Cardio »…) ;
  - l'**éditeur de séance** : séries, répétitions (ou durée) et charge visées par exercice, ordre, **supersets** (deux exercices enchaînés, sans repos entre eux) ;
  - **progression automatique** : quand toutes les séries atteignent leurs répétitions, la charge monte la fois suivante (+2,5 kg à la barre ou à la machine, +2 kg aux haltères, une répétition ou 5 s de plus sans charge). Une charge vide est apprise à la première séance.
- **Séance en cours** :
  - chronomètre depuis le début, écran maintenu allumé ;
  - un exercice reprend **les séries de la dernière fois** (charges et répétitions à battre), ou celles prévues par la séance du programme, avec l'**objectif** affiché (« Objectif 4 × 6 · 60 kg ») et la colonne « Précédent » en regard ;
  - saisie kg × répétitions, répétitions seules ou durée ; une série validée remplit la suivante et lance le **minuteur de repos** (±15 s, « Passer »), avec notification si le téléphone est verrouillé ; dans un superset, le repos attend le dernier exercice ;
  - menu « ⋯ » : monter, descendre, superset avec le suivant, **calculateur de disques** (exercices à la barre), historique, retirer.
- **Fin de séance** : durée, volume, séries, **comparaison avec la dernière fois** (« +4 % de volume »), **records personnels**, **objectifs relevés** pour la prochaine fois, ressenti et note.
- **Progrès** (troisième onglet) : volume par semaine sur 1 ou 3 mois (graphique et moyenne), **séries par groupe musculaire** sur 7 jours face à une cible de 10, alerte quand un groupe est délaissé depuis deux semaines ou sous la cible, **records du mois**.
- **Corps** (quatrième onglet) : **poids** et courbe sur 3 mois, évolution sur 8 semaines, **objectif de poids** avec progression ; **mensurations** (taille, poitrine, bras, cuisse, hanches) avec l'écart depuis la mesure précédente ; **photos de progression** prises ou choisies, gardées dans l'app (pas dans la photothèque), **verrouillées par Face ID**, à comparer avant / après.
- **Exercices** (cinquième onglet) : 51 exercices intégrés avec **conseils d'exécution**, filtres par groupe musculaire et par **matériel**, recherche sans accents ; chaque fiche montre le record, le **1RM estimé**, la progression sur 3 mois, 1 an ou tout, et l'historique avec le volume de chaque séance. On peut créer ses propres exercices.
- **Calculateur de disques** : disques à mettre de chaque côté pour une charge, barre de 20, 15 ou 10 kg, **échauffement proposé** (barre, puis environ 40, 60 et 80 %) ; les disques disponibles se règlent dans les réglages.
- **Réglages** : objectif, séances par semaine, minuteur et durée du repos, disques disponibles, langue.
- **Langues** : français, néerlandais et anglais (par défaut : la langue du téléphone), changement immédiat.
- **Thème clair / sombre** automatique.

## Persistance

**SQLite** local via `sqlite-net-pcl`, dans le dossier privé de l'app. Aucune connexion réseau.

- Tables `exercises`, `workouts`, `workout_exercises`, `workout_sets` ; `programs`, `templates`, `template_exercises` (programmes et séances types) ; `body_weights`, `measurements`, `body_photos`. Les exercices et programmes intégrés sont stockés par clé (`bench_press`, `full_body`…) et traduits à l'affichage ; les nouveaux exercices d'une mise à jour sont ajoutés au démarrage, et les colonnes nouvelles ajoutées aux tables existantes.
- Une séance lancée depuis un programme garde le lien vers sa séance type (`TemplateId`) et copie ses objectifs : la prochaine séance du programme est celle qui suit la dernière faite, et la progression automatique relève les objectifs de la séance type quand la séance est terminée.
- Les photos de progression sont des fichiers dans le dossier privé de l'app (`photos/`), jamais dans la photothèque.
- Il y a au plus une séance en cours (`FinishedAt` nul). Les records, la dernière fois et les statistiques sont **toujours recalculés** à partir des séries validées des séances terminées : rien ne peut se désynchroniser.
- Un exercice déjà utilisé n'est jamais effacé, seulement masqué de la bibliothèque, pour que les anciennes séances le montrent encore.
- Comparaison des séries : 1RM estimé pour les séries chargées (plafonné à 12 répétitions), répétitions sinon, durée pour les exercices chronométrés.
- La fin du repos est enregistrée : le minuteur continue si l'app est fermée, et une notification locale (`Plugin.LocalNotification`) sonne à l'heure. Sur Android 14+, l'autorisation « Alarmes et rappels » la rend exacte ; sans elle, elle peut arriver avec un peu de retard.

## Architecture

```
Fonte.slnx
├── src/Fonte.Core          net10.0 — modèles, FonteStore (SQLite), catalogues d'exercices et de programmes, calculs (1RM, volume, records, progression, disques, séries de semaines)
├── src/Fonte               app .NET MAUI (MVVM avec CommunityToolkit.Mvvm)
│   ├── Views/              pages XAML (bindings compilés)
│   ├── ViewModels/         un ViewModel par page
│   ├── Controls/           dessin vectoriel maison : icônes, courbe, barre de progression
│   ├── Services/           réglages, minuteur de repos, dialogues, palette, routes
│   └── Resources/Styles/   couleurs et styles (clair/sombre)
└── tests/Fonte.Core.Tests  tests xUnit de la logique métier (SQLite réel en fichier temporaire) et des traductions
```

La base technique (contrôles, feuilles sur Mac, localisation, raccourcis clavier) vient de Poches.

### Traductions

Les textes sont dans `src/Fonte/Resources/Strings/` : `AppResources.resx` (anglais, langue par défaut), `AppResources.fr.resx` et `AppResources.nl.resx`.
- En XAML : `Text="{l:Tr Home_Start}"` ; en C# : `Loc.Get("Home_Start")` ou `Loc.Format("Picker_Add", n)`.
- Noms construits à l'exécution : exercices intégrés `Ex_<clé>` et leurs conseils `Tip_<clé>`, programmes `Program_*` / `ProgramText_*`, séances types `Template_*`, objectifs `Goal_*` / `GoalText_*`, mensurations `Measure_*`, groupes `Muscle_*`, matériel `Equipment_*`, saisie `Tracking_*`, erreurs `Error_*` (codes `FonteError` de `Fonte.Core`).
- Les tests `TranslationTests` échouent si une langue n'a pas les mêmes clés ou les mêmes `{0}`, si une clé utilisée n'existe pas, si une clé n'est plus utilisée, ou s'il manque le nom ou le conseil d'un exercice, le nom d'un programme, d'une séance type, d'un objectif, d'une mensuration, d'un groupe, d'un matériel ou d'une erreur.

## Lancer l'app

Prérequis : SDK .NET 10 et workload MAUI (`dotnet workload install maui`).

```bash
dotnet test tests/Fonte.Core.Tests
```

```bash
dotnet build src/Fonte -t:Run -f net10.0-ios
```

```bash
dotnet build src/Fonte -t:Run -f net10.0-android
```

```bash
dotnet build src/Fonte -t:Run -f net10.0-maccatalyst
```

Sur un vrai iPhone, un compte Apple Developer est nécessaire pour signer l'app. Avec Xcode 27, tant que le SDK .NET pour iOS ne le reconnaît pas officiellement, ajouter `-p:ValidateXcodeVersion=false` aux commandes iOS et Mac. Sur Mac, les feuilles s'affichent en carte centrée et `Échap` les ferme, comme dans Poches.

## Idées pour la suite

- Unité en livres.
- Sauvegarde chiffrée et restauration (comme Poches), export CSV.
- Repos propre à chaque exercice d'une séance type.
