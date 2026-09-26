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
| **Observations** | 39 valeurs (depuis `dwarf_v03`) : nain (9), 5 pics (10), ravin le plus proche du nain (2), 3 ennemis les plus proches (4 chacun + le danger le plus proche **de cet ennemi**, 2 chacun) |
| **Rewards** | Kill **direct** (marteau) **+0.3** · kill en **poussant dans un danger** **+1.0 × multiplicateur du score** (ravin marteau ×1.5, pic marteau ×2, ravin bouclier ×2.5, pic bouclier ×3) · dégât subi **−0.3** · mort **−1.0** · coût par step **−0.0005** |
| **Anti reward-hacking** | Aucun point pour le suicide (ravins/pics), pics neutres pour les ennemis |

**Pourquoi ces observations et ces rewards (`dwarf_v03`)** : `dwarf_v02` campait
au centre sans jamais utiliser le décor ([compte-rendu](docs/runs/dwarf_v02.md)) :
le reward était +1 plat par kill, et l'agent ne voyait ni les ravins ni les pics.
Désormais, pousser un ennemi dans un danger rapporte 3 à 10 fois plus qu'un kill
direct, et l'agent voit où sont les dangers, par rapport à lui et à chaque ennemi.
Aucune pénalité d'immobilité : le joueur reste libre de jouer comme il veut.

**Réglages dans l'Inspector** (déjà appliqués dans `ArenaScene` pour `v03`) :
`DwarfAgent` > `Spike Traps` = `SpikePit1` à `SpikePit5` (l'ordre est fixe :
chaque pic garde son slot), `Observed Spikes` = `5`, `Direct Kill Reward` = `0.3` ;
`Behavior Parameters` > `Space Size` = `39`. Formule : `11 + 2 × pics + 6 × ennemis
observés`. Les modèles `dwarf_v01`/`v02` (25 observations) ne sont pas utilisables
avec cette scène.

Cette configuration évolue seulement quand l'agent lui-même change (nouvelle
observation, nouveau reward...) — les runs qui utilisent la même version sont
directement comparables entre eux.

---

## 🚀 Le pipeline, étape par étape

### 1. Entraîner

**Réglages à faire dans Unity avant de lancer** (scène `ArenaScene`, GameObject
du nain — le Player) :

| Composant | Réglage pour entraîner |
|---|---|
| `DwarfAgent` | **Coché (activé)**. Décoché, aucun agent ne s'enregistre et `mlagents-learn` attend indéfiniment. |
| `Behavior Parameters` | **Behavior Type : `Default`** (pas `Inference Only`, qui rejoue un modèle sans jamais se connecter à Python). |
| `Behavior Parameters` | **Behavior Name : `Dwarf`**, identique à la clé `behaviors:` du fichier YAML. |
| `Decision Requester` | Decision Period `5` (inchangé). |

Rien d'autre à désactiver : le menu principal et la pause se coupent
tout seuls quand Python est connecté (`GameManager.IsTraining`), et le
`Time Scale` est géré par `mlagents-learn`. Le champ `Model` peut rester
assigné : il ne sert pas pendant l'entraînement.

**Lancer** :

```powershell
# Terminal (venv ML-Agents activé), depuis le dossier ML-Agents (ici D:\git\ml-agents)
mlagents-learn D:\git\dwarven-arena\dwarven-arena\docsuns\<config_du_run>.yaml --run-id=<nom_du_run>

# Puis Play dans l'éditeur Unity quand "Start training by pressing the Play button" apparaît
```

Le dossier `results/` est créé dans le **dossier courant du terminal** (ici
`D:\git\ml-agentsesults\<nom_du_run>\`), pas dans le projet Unity.

Chaque run part d'un fichier de config dédié dans `docs/runs/` (copié/adapté du
précédent) — ça garde une trace exacte des hyperparamètres utilisés pour
chaque résultat documenté.

**Ça ne se connecte pas ?** Vérifie dans l'ordre : `DwarfAgent` coché, Behavior
Type sur `Default`, Behavior Name identique au YAML, aucune erreur rouge dans
la console Unity au moment de Play (une référence manquante désactive l'agent),
et un seul `mlagents-learn` en cours (port 5004 libre).

**Après l'entraînement**, remets le Behavior Type selon l'usage — `DwarfAgent`
reste coché dans tous les cas :

| Usage | Behavior Type |
|---|---|
| Entraîner | `Default` |
| Jouer au clavier | `Heuristic Only` (`DwarfAgent.Heuristic` lit le clavier) |
| Voir l'IA jouer | `Inference Only` + modèle `.onnx` |

Avec `Default` et sans Python, l'IA (modèle) joue à ta place.

### 2. Suivre l'entraînement

```powershell
tensorboard --logdir results   # depuis le dossier ML-Agents
```

Sur `http://localhost:6006`, onglet Scalars — voir le détail des courbes à
surveiller dans chaque doc de run.

### 3. Importer le modèle entraîné dans Unity

1. Copier `results/<run-id>/Dwarf.onnx` (modèle final, directement dans le
   dossier du run) vers `Assets/Models/`. Les checkpoints intermédiaires
   (`Dwarf-<étape>.onnx`) sont dans `results/<run-id>/Dwarf/`.
2. Sur le GameObject du nain, composant **Behavior Parameters** :
   - **Behavior Type** : `Inference Only` (voir le tableau des usages ci-dessus)
   - **Model** : le `.onnx` copié
3. ▶️ Play — l'IA contrôle le nain sans entraînement ni Python.

---

## 📚 Historique des runs

| Run | Résumé | Détail |
|---|---|---|
| `dwarf_v01` | Pipeline validé de bout en bout ; l'agent trouve une stratégie de camping (près d'un pic) plutôt que la chasse agressive espérée | [docs/runs/dwarf_v01.md](docs/runs/dwarf_v01.md) |
| `dwarf_v02` | Mêmes hyperparamètres sur le jeu avec Orcs/nouveau combat : reward final 25.0 (contre 28.4), entropie plus basse (0.92), épisodes au plafond de durée | [docs/runs/dwarf_v02.md](docs/runs/dwarf_v02.md) |

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
