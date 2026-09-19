# 🏔️ Dwarven Arena

> Un nain, un marteau de guerre, un bouclier — et une arène pleine d'orques et de gobelins à pousser dans le vide.

![Unity](https://img.shields.io/badge/Unity-6.0-blueviolet)
![ML-Agents](https://img.shields.io/badge/ML--Agents-Unity-informational)
![License](https://img.shields.io/badge/licence-%C3%A0%20d%C3%A9finir-orange)

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
3. ▶ Play — WASD/flèches pour bouger, clic pour frapper *(selon avancement — voir Roadmap)*

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

## 🗺️ Roadmap

- [x] Stage 0 — Projet Unity, joueur déplaçable (WASD), physique 2D
- [ ] Stage 1 — Ennemis (gobelins) avec IA de poursuite
- [ ] Stage 2 — Système de vagues + spawn + 3 HP
- [ ] Stage 3 — Marteau : zone de frappe, knockback, effet domino
- [ ] Stage 4 — Ravins + zones de mort
- [ ] Stage 5 — Bouclier + pénalité de vitesse sur swing raté
- [ ] Stage 6 — Pics + explosions colorées (VFX)
- [ ] Stage 7 — Intégration ML-Agents (observations/actions/rewards)
- [ ] Stage 8 — Entraînement + tuning des récompenses
- [ ] Stage 9 — Cinématique d'intro (pont qui s'écroule)
- [ ] Stage 10 — Polish, GIF de démo, release

## 🎨 Crédits assets

*À compléter au fur et à mesure de l'ajout des assets — chaque créateur
sera crédité ici conformément à sa licence.*

## 🧑‍💻 Auteur

**Conçu par pazpop**

---

*Licence : à définir (probablement MIT pour le code, avec crédits assets dédiés)*