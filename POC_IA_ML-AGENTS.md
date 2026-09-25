# Dwarven Arena — POC : IA locale qui apprend à jouer (ML-Agents + PPO)

> Objectif : démontrer qu'une IA entraînée **100 % en local** (Windows 11, RTX 3080) peut apprendre à jouer et survivre dans un jeu Unity 2D personnalisé.

Ce document décrit l'architecture de l'agent et comment fonctionne le pipeline
d'entraînement — stable d'un run à l'autre. Chaque entraînement a son propre
compte-rendu détaillé dans [`docs/runs/`](docs/runs/), avec ses courbes
TensorBoard et son analyse. La conclusion en bas de page agrège les
enseignements de tous les runs.

---

## 🎮 Le jeu

**Dwarven Arena** est un top-down survival : un nain au marteau lourd affronte
des vagues de gobelins/orcs dans une arène truffée de ravins (ring-out mortel)
et de pics. Chaque kill rapporte des points, chaque dégât coûte 1 HP. Détail
complet du gameplay : [GAMEPLAY.md](GAMEPLAY.md).

---

## 🤖 Configuration de l'agent (ML-Agents, Unity 6)

| Élément | Détail |
|---|---|
| **Agent** | `Assets/Scripts/Agents/DwarfAgent.cs` — contrôle externe, bascule clavier ↔ IA via `PlayerMovement.ExternalControl` |
| **Actions** | 4 branches discrètes : déplacement X/Y, marteau, bouclier |
| **Décisions** | 1 décision toutes les 5 frames (~12 Hz) |
| **Observations** | 25 valeurs : état du nain (9), pics (4), 3 ennemis les plus proches (12) |
| **Rewards** | Kill **+1.0** · dégât subi **−0.3** · mort **−1.0** · coût par step **−0.0005** |
| **Anti reward-hacking** | Aucun point pour le suicide (ravins/pics), pics neutres pour les ennemis |

Cette configuration évolue seulement quand l'agent lui-même change (nouvelle
observation, nouveau reward...) — les runs qui utilisent la même version sont
directement comparables entre eux.

---

## 🚀 Le pipeline, étape par étape

### 1. Entraîner

```powershell
# Terminal (venv ML-Agents activé)
mlagents-learn docs/runs/<config_du_run>.yaml --run-id=<nom_du_run>

# Puis Play dans l'éditeur Unity quand "Start training by pressing the Play button" apparaît
```

Chaque run part d'un fichier de config dédié dans `docs/runs/` (copié/adapté du
précédent) — ça garde une trace exacte des hyperparamètres utilisés pour
chaque résultat documenté.

### 2. Suivre l'entraînement

```powershell
tensorboard --logdir results
```

Sur `http://localhost:6006`, onglet Scalars — voir le détail des courbes à
surveiller dans chaque doc de run.

### 3. Importer le modèle entraîné dans Unity

1. Copier `results/<run-id>/Dwarf/Dwarf.onnx` (dernier checkpoint) vers `Assets/Models/`.
2. Sur le GameObject du nain, composant **Behavior Parameters** :
   - **Behavior Type** : `Inference Only`
   - **Model** : le `.onnx` copié
3. ▶️ Play — l'IA contrôle le nain sans entraînement ni Python.

---

## 📚 Historique des runs

| Run | Résumé | Détail |
|---|---|---|
| `dwarf_v01` | Pipeline validé de bout en bout ; l'agent trouve une stratégie de camping (près d'un pic) plutôt que la chasse agressive espérée | [docs/runs/dwarf_v01.md](docs/runs/dwarf_v01.md) |

---

## ✅ Conclusion des entraînements

*(Section mise à jour après chaque nouveau run — vue d'ensemble de ce que les
entraînements successifs ont révélé, pas le détail d'un run en particulier.)*

Le premier run (`dwarf_v01`) a montré la vraie valeur de cette approche : le
ML-Agents n'est pas seulement un mode démo, c'est un outil de test de
gameplay — un testeur infatigable qui explore l'espace des stratégies sans a
priori et révèle les angles morts du level design. En poussant l'agent à
optimiser froidement sa survie, il a mis en évidence une faille de design
(camper près d'un pic) restée invisible en jouant soi-même.

Depuis ce run, plusieurs changements côté jeu visent directement ce
comportement — les gobelins/orcs évitent maintenant activement les dangers au
lieu de foncer dessus, ce qui change l'intérêt tactique de camper près d'un
piège. Le prochain run permettra de voir si ce changement d'environnement,
à lui seul, suffit à faire émerger un comportement plus agressif, ou si un
reward shaping explicite (malus de camping, bonus de proximité aux ennemis)
reste nécessaire.
