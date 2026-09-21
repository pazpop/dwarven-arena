# Dwarven Arena — POC : IA locale qui apprend à jouer (ML-Agents + PPO)

> Objectif : démontrer qu'une IA entraînée **100 % en local** (Windows 11, RTX 3080) peut apprendre à jouer et survivre dans un jeu Unity 2D personnalisé.
> Résultat : pipeline complet validé, premier modèle fonctionnel — avec ses limites.

<p align="center">
  <img src="docs/media/gameplay.gif" alt="Démo de l'IA en inférence">
</p>

---

## 🎮 Le jeu

**Dwarven Arena** est un top-down survival : un nain au marteau lourd (le carré blanc/bleu dans le gif) affronte des vagues de gobelins (vert) dans une arène truffée de ravins (ring-out mortel) et de pics (rectangle gris foncé). Chaque kill rapporte +10 points, chaque dégât coûte 1 HP.

---

## 🤖 Configuration de l'agent (ML-Agents 4.1.0, Unity 6)

| Élément | Détail |
|---|---|
| **Agent** | `DwarfAgent.cs` — contrôle externe, bascule clavier ↔ IA |
| **Actions** | 4 branches discrètes : déplacement X/Y, marteau, bouclier |
| **Décisions** | 1 décision toutes les 5 frames (~12 Hz) |
| **Observations** | 25 valeurs : état du nain (9), pics (4), 3 ennemis les plus proches (12) |
| **Rewards** | Kill **+1.0** · dégât subi **−0.3** · mort **−1.0** · coût par step **−0.0005** |
| **Anti reward-hacking** | Aucun point pour le suicide (ravins/pics), pics neutres pour les ennemis |

---

## 🚀 Entraînement (PPO)

Fichier config : [`docs/dwarven_arena_ppo.yaml`](docs/dwarven_arena_ppo.yaml) (adapté du template PPO fourni par le repo [ml-agents](https://github.com/Unity-Technologies/ml-agents))
*(batch 128, buffer 2048, lr 3e-4 décroissant, 2×256 hidden units, gamma 0.99)*

```powershell
# Terminal (venv ML-Agents activé)
mlagents-learn docs/dwarven_arena_ppo.yaml --run-id=dwarf_v01

# Puis Play dans l'éditeur Unity quand "Start training by pressing the Play button" apparaît
```

### Budget d'entraînement — ce qu'il a coûté

| Métrique | Valeur |
|---|---|
| Steps | 3 000 000 |
| Durée | ~5 h 05 sur RTX 3080, en local, sans GPU cloud |
| Reward moyen final | 28.42 (≈ 30 kills/épisode avant mort) |
| Longueur d'épisode | ~600 steps (~10 s), stable |
| Signaux d'apprentissage visibles dès | ~1 h |

---

## 📊 Interpréter TensorBoard

Courbes exportées depuis `http://localhost:6006` (onglet Scalars) :

| Courbe | Ce qu'elle doit faire | Notre run |
|---|---|---|
| Cumulative Reward | Monter, puis se stabiliser | 24 → 28.4, plateau après 2M steps ✅ |
| Policy/Entropy | Baisser mais pas à zéro (sinon sur-spécialisation) | 2.8 → 1.61 ✅ |
| Losses/Value Loss | Descendre (meilleure prédiction des récompenses) | 0.14 → 0.06 ✅ |
| Losses/Policy Loss | Stable/faible | ~0.068 ✅ |
| Episode Length | S'allonger si l'agent survit mieux | Plat — voir limites ⚠️ |

*Astuce : lisser à 0.6 (curseur Smoothing) pour lire la tendance sous le bruit.*

![Cumulative Reward](docs/media/tensorboard_cumulative_reward.png)

*Reward total moyen encaissé par épisode (un épisode = de l'apparition à la mort du nain). C'est la courbe la plus haut niveau : si elle ne monte pas, rien d'autre ne compte.*

![Entropy](docs/media/tensorboard_entropy.png)

*Incertitude de la politique : à quel point l'agent hésite encore entre plusieurs actions possibles dans une même situation. Une entropie qui baisse veut dire que l'agent devient plus confiant/déterministe dans ses choix — trop bas trop vite, et il se fige sur une stratégie sans avoir assez exploré.*

![Episode Length](docs/media/tensorboard_episode_length.png)

*Nombre de steps avant la mort du nain (proxy du temps de survie). Ici elle plafonne à ~600 steps sans progresser — signe que l'agent a trouvé un plateau de survie plutôt qu'une stratégie qui s'améliore, cohérent avec le comportement de camping observé plus bas.*

---

## 📥 Importer le modèle dans Unity

1. Copier `results/dwarf_v01/Dwarf/Dwarf.onnx` (le dernier checkpoint) vers `Assets/Models/` du projet Unity.
2. Sur le GameObject du nain, composant **Behavior Parameters** :
   - **Behavior Type** : `Inference Only`
   - **Model** : `Dwarf.onnx`
3. ▶️ Play — l'IA contrôle le nain sans entraînement ni Python.

---

## 🔍 Retour d'expérience (leçon n°1 du POC)

Le pipeline est validé de bout en bout : observations → rewards → PPO → modèle ONNX jouable dans Unity. Mais observer le modèle en inférence a révélé ce que les courbes cachaient : le nain campe derrière un pic — une stratégie « safe » localement optimale (~28 de reward) au lieu de la chasse agressive espérée. La variance ±1.5 du reward et l'entropie encore élevée le confirmaient.

Ce que ça nous apprend :

- Un reward moyen stable ne garantit pas un comportement fun : le reward shaping est le vrai métier du RL (*Reinforcement Learning*, apprentissage par renforcement — la famille de méthodes, dont PPO fait partie, où un agent apprend par essai-erreur en maximisant une récompense plutôt qu'à partir d'exemples étiquetés).
- Regarder l'agent jouer est une étape de validation indispensable, les courbes TensorBoard ne suffisent pas.

Prochaines itérations : malus de camping (distance min. aux pics), curiosity, reward shaping sur la proximité des ennemis, ou revoir le déplacement des gobelins en esquivant les pics en voulant aller vers le nain — avant de retoucher les hyperparamètres. Ensuite : remplacement des assets graphiques.

---

## ✅ Conclusion

Ce POC visait à valider un pipeline technique — il a fini par en dire plus sur le jeu lui-même. En poussant l'agent à optimiser froidement sa survie, sans intuition ni triche, le réseau de neurones a mis en évidence une faille de game design restée invisible en jouant soi-même : camper derrière un pic est une stratégie viable, ce qui va à l'encontre de l'expérience « chasse agressive » recherchée.

C'est la vraie valeur de cette approche pour la suite : le ML-Agents n'est pas seulement un mode démo, c'est un outil de test de gameplay — un testeur infatigable qui explore l'espace des stratégies sans a priori et révèle les angles morts du level design. Les prochaines itérations sur le comportement des gobelins et l'équilibrage de l'arène s'appuieront sur ce que cet agent a montré, avant de retenter un entraînement.
