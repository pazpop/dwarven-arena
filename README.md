# 🏔️ Dwarven Arena

> Un nain, un marteau de guerre, un bouclier — et une arène pleine d'orques et de gobelins à pousser dans le vide.

![Unity](https://img.shields.io/badge/Unity-6.0-blueviolet)
![ML-Agents](https://img.shields.io/badge/ML--Agents-Unity-informational)
![License](https://img.shields.io/badge/licence-MIT-green)

## 🎯 Concept

**Dwarven Arena** est un jeu 2D vue du dessus où l'on incarne un nain armé d'un
lourd marteau de guerre et d'un bouclier, encerclé par des vagues d'ennemis.
Pas d'attaque directe à outrance : le cœur du gameplay est la **projection
physique**. Chaque coup de marteau crée un effet domino qui envoie les ennemis
valdinguer — idéalement dans un ravin ou sur des pics. Façon *300*, mais
avec un nain grincheux.

Le projet a une double ambition :

1. **Un jeu fun pour humain** — parties courtes et nerveuses, rejouables
2. **Un terrain d'apprentissage pour l'IA** — un agent autonome, entraîné
   localement par renforcement ([Unity ML-Agents](https://github.com/Unity-Technologies/ml-agents)),
   doit apprendre à survivre et à maîtriser les mêmes mécaniques que le joueur

## 🎮 Gameplay

Voir [GAMEPLAY.md](./GAMEPLAY.md) pour le détail complet des mécaniques.

- Nain : bouclier + marteau de guerre lent à cooldown élevé
- Gobelins : nombreux, légers, faciles à projeter (dominos !)
- Orques : rares, lourds, difficiles à pousser
- Ravins tout autour de l'arène + pièges à pics au centre
- 3 points de vie — mourir explose en rouge, les ennemis en vert 💥
- Un swing raté ralentit le nain : chaque coup compte

## 🤖 L'agent IA

Le même gameplay est exposé à un agent ML-Agents entraîné en **PPO**
(Proximal Policy Optimization), entièrement en local — aucun service cloud,
aucune dépense API. L'entraînement s'effectue sur GPU grand public
(RTX 3080 ici).

Objectifs d'apprentissage pour l'agent :

- survivre aux vagues croissantes
- découvrir le knockback en chaîne (les dominos)
- comprendre le positionnement près des ravins et pièges
- gérer le cycle marteau (cooldown) / bouclier (défense)

Les modèles entraînés (`.onnx`) sont conservés dans le repo et jouables
directement dans le build via Unity Sentis — pas besoin de Python pour la démo.

## 🛠️ Stack technique

| Composant | Technologie |
|-----------|-------------|
| Moteur | Unity 6 (6000.6.2f1), template 2D |
| ML | Unity ML-Agents (PPO), Python + venv |
| Langages | C# (gameplay) / YAML (configs entraînement) |
| Entraînement | Local, GPU CUDA |
| Développé avec | VS Code + Git Bash |

## 🚀 Démarrage

### Prérequis

- Unity 6 (via [Unity Hub](https://unity.com/download))
- Python 3.10+

### Jouer

1. Ouvrir le dossier du projet dans Unity Hub
2. Ouvrir la scène `Assets/Scenes/ArenaScene.unity`
3. ▶ Play — WASD/flèches pour bouger, Espace pour frapper, clic gauche pour lever le bouclier

### Entraîner l'agent

Ouvrir un terminal Git Bash à la racine du projet :

```bash
python -m venv venv
source venv/Scripts/activate
pip install mlagents
mlagents-learn ./ML-Agents/Configs/dwarven_arena_ppo.yaml --run-id=v1
```

Suivi de l'entraînement :

```bash
tensorboard --logdir results
```

## 📜 Transparence sur l'usage de l'IA

Ce projet est développé en binôme avec des assistants IA
([Lumo](https://lumo.proton.me) par Proton, et [Claude](https://claude.ai))
dans une démarche d'apprentissage assumée :

- Les choix d'architecture et de game design sont arbitrés par un humain
- Le code est relu, compris et validé avant chaque commit
- Les assistants servent à accélérer les tâches répétitives et à explorer
  des pistes, pas à remplacer la compréhension

L'objectif est l'apprentissage du développement Unity et de l'apprentissage
par renforcement appliqué au jeu vidéo.

Je ne suis pas développeur de jeux — mon but ici était surtout de faire un projet de jeu avec un réseau de neurones. Claude et Lumo m'ont été d'une
aide précieuse pour combler mes lacunes Unity/C# tout au long du projet.

## 🗺️ Roadmap

- [x] Stage 0 — Projet Unity, joueur déplaçable (WASD), physique 2D
- [X] Stage 1 — Ennemis (gobelins) avec IA de poursuite
- [X] Stage 2 — Système de vagues + spawn + 3 HP
- [X] Stage 3 — Marteau : zone de frappe, knockback, effet domino
- [X] Stage 4 — Ravins + zones de mort
- [X] Stage 5 — Bouclier + pénalité de vitesse sur swing raté
- [X] Stage 6 — Pics + explosions colorées (VFX)
- [X] Stage 7 — Intégration ML-Agents (observations/actions/rewards) — voir [POC_IA_ML-AGENTS.md](POC_IA_ML-AGENTS.md) pour le détail des tests et résultats
- [ ] Stage 8 — Remplacement des assets graphiques
  - [x] Sprites du Nain et du Gobelin, tileset de sol et carte de l'arène — générés via [PixelLab](https://pixellab.ai)
  - [x] Animations du Nain (idle/marche/attaque/protection) et du Gobelin (idle/marche) — générées via PixelLab
  - [ ] Sprite et animations de l'Orque (voir Stage 8.5)
  - [ ] Animation de mort, effets de particules (blood splatter)
  - Ne touche ni la logique ni le modèle : l'IA se base sur les observations, pas les visuels
- [ ] Stage 8.5 — Mécaniques décrites dans [GAMEPLAY.md](./GAMEPLAY.md) mais pas encore codées
  - Orques (ennemi lourd/rare, mass=3, difficile à pousser) — actuellement `EnemyAI` est générique, seul le Goblin existe
  - Multiplicateur de score en chaîne (pousser N ennemis en un swing rapporte plus que N kills séparés) — `RegisterKill()` n'ajoute pour l'instant qu'un score fixe
- [ ] Stage 9 — Raffinement des hyperparamètres (et rewards)
  - Reward shaping anti-camping : malus de proximité aux pics, ou bonus de proximité aux ennemis
  - Curiosity (`reward_signals.curiosity`) si l'agent reste bloqué sur sa stratégie de camping
  - Ajuster `learning_rate`/`batch_size` selon les courbes du run suivant
  - Tester `lambd`/`beta` si l'exploration est insuffisante
  - Objectif : un nain qui chasse activement les gobelins au lieu de camper
- [ ] Stage 10 — Cinématique d'intro (pont qui s'écroule)
- [ ] Stage 11 — Polish, GIF de démo, release

## 🎨 Crédits assets

- Sprites (Nain, Gobelin), animations, tileset de sol et carte de l'arène :
  générés via [PixelLab](https://pixellab.ai)

*Section complétée au fur et à mesure de l'ajout d'autres assets.*

## 🧑‍💻 Auteur

**Conçu par pazpop**

---

*Licence : MIT pour le code (voir [LICENSE](LICENSE)), crédits assets dédiés ci-dessus*