# Run `dwarf_v01`

> Voir [POC_IA_ML-AGENTS.md](../../POC_IA_ML-AGENTS.md) pour l'architecture de l'agent (actions/observations/rewards) — inchangée pour ce run, donc non répétée ici.

**Date** : premier entraînement du projet, avant l'ajout des Orcs, des décorations,
du menu, de l'évitement de dangers par les ennemis et des corrections de hitbox.
L'arène et le comportement des gobelins de ce run ne sont donc **pas** ceux du
jeu actuel — à garder en tête en comparant avec les runs suivants.

<p align="center">
  <img src="../media/dwarf_v01/gameplay.gif" alt="Démo de l'IA en inférence — run dwarf_v01">
</p>

## Config utilisée

[`dwarf_v01_config.yaml`](./dwarf_v01_config.yaml) — PPO, batch 128, buffer 2048,
learning rate 3e-4 décroissant, 2×256 hidden units, gamma 0.99. Adapté du template
PPO du repo [ml-agents](https://github.com/Unity-Technologies/ml-agents).

```powershell
# Terminal (venv ML-Agents activé)
mlagents-learn docs/runs/dwarf_v01_config.yaml --run-id=dwarf_v01

# Puis Play dans l'éditeur Unity quand "Start training by pressing the Play button" apparaît
```

## Budget d'entraînement

| Métrique | Valeur |
|---|---|
| Steps | 3 000 000 |
| Durée | ~5 h 05 sur RTX 3080, en local, sans GPU cloud |
| Reward moyen final | 28.42 (≈ 30 kills/épisode avant mort) |
| Longueur d'épisode | ~600 steps (~10 s), stable |
| Signaux d'apprentissage visibles dès | ~1 h |

## Interpréter TensorBoard

Courbes exportées depuis `http://localhost:6006` (onglet Scalars) :

| Courbe | Ce qu'elle doit faire | Ce run |
|---|---|---|
| Cumulative Reward | Monter, puis se stabiliser | 24 → 28.4, plateau après 2M steps ✅ |
| Policy/Entropy | Baisser mais pas à zéro (sinon sur-spécialisation) | 2.8 → 1.61 ✅ |
| Losses/Value Loss | Descendre (meilleure prédiction des récompenses) | 0.14 → 0.06 ✅ |
| Losses/Policy Loss | Stable/faible | ~0.068 ✅ |
| Episode Length | S'allonger si l'agent survit mieux | Plat — voir retour d'expérience ⚠️ |

*Astuce : lisser à 0.6 (curseur Smoothing) pour lire la tendance sous le bruit.*

![Cumulative Reward](../media/dwarf_v01/tensorboard_cumulative_reward.png)

*Reward total moyen encaissé par épisode (un épisode = de l'apparition à la mort du nain). C'est la courbe la plus haut niveau : si elle ne monte pas, rien d'autre ne compte.*

![Entropy](../media/dwarf_v01/tensorboard_entropy.png)

*Incertitude de la politique : à quel point l'agent hésite encore entre plusieurs actions possibles dans une même situation. Une entropie qui baisse veut dire que l'agent devient plus confiant/déterministe dans ses choix — trop bas trop vite, et il se fige sur une stratégie sans avoir assez exploré.*

![Episode Length](../media/dwarf_v01/tensorboard_episode_length.png)

*Nombre de steps avant la mort du nain (proxy du temps de survie). Ici elle plafonne à ~600 steps sans progresser — signe que l'agent a trouvé un plateau de survie plutôt qu'une stratégie qui s'améliore, cohérent avec le comportement de camping observé plus bas.*

## Retour d'expérience

Le pipeline est validé de bout en bout : observations → rewards → PPO → modèle ONNX jouable dans Unity. Mais observer le modèle en inférence a révélé ce que les courbes cachaient : le nain campe dans un coin près d'un pic, et laisse les gobelins (qui à l'époque fonçaient droit sur lui sans éviter les dangers) se suicider seuls sur les pics en le poursuivant — une stratégie « safe » localement optimale (~28 de reward, des kills sans jamais swinguer le marteau) au lieu de la chasse agressive espérée. La variance ±1.5 du reward et l'entropie encore élevée le confirmaient.

Ce que ça nous apprend :

- Un reward moyen stable ne garantit pas un comportement fun : le reward shaping est le vrai métier du RL (*Reinforcement Learning*, apprentissage par renforcement — la famille de méthodes, dont PPO fait partie, où un agent apprend par essai-erreur en maximisant une récompense plutôt qu'à partir d'exemples étiquetés).
- Regarder l'agent jouer est une étape de validation indispensable, les courbes TensorBoard ne suffisent pas.

Pistes identifiées pour la suite (certaines déjà faites côté jeu depuis, voir la
note en haut de page) : malus de camping (distance min. aux pics), curiosity,
reward shaping sur la proximité des ennemis, évitement des pics par les gobelins.

## Conclusion du run

Ce run visait à valider un pipeline technique — il a fini par en dire plus sur le jeu lui-même. En poussant l'agent à optimiser froidement sa survie, sans intuition ni triche, le réseau de neurones a mis en évidence une faille de game design restée invisible en jouant soi-même : camper derrière un pic est une stratégie viable, ce qui va à l'encontre de l'expérience « chasse agressive » recherchée.

Depuis ce run, les gobelins/orcs évitent maintenant activement les dangers
(voir `EnemyAI.cs`) — un facteur qui change la donne pour le prochain run et
qu'il faudra observer.
