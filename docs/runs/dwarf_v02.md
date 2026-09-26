# Run `dwarf_v02`

> Voir [POC_IA_ML-AGENTS.md](../../POC_IA_ML-AGENTS.md) pour l'architecture de l'agent (actions/observations/rewards) — inchangée pour ce run, donc non répétée ici.

**Contexte** : mêmes observations, actions, rewards et hyperparamètres que
[`dwarf_v01`](./dwarf_v01.md), mais sur le jeu **après** l'ajout des Orcs, des
sprites/animations, du menu, du combat plus lisible et de la refonte perf
(commit `a4816b9`). Les gobelins évitent aussi les dangers depuis `v01`. Seul
l'environnement change : c'est ce qui rend la comparaison instructive.

<p align="center">
  <img src="../media/dwarf_v02/gameplay.gif" alt="Démo de l'IA en inférence — run dwarf_v02">
</p>

## Config utilisée

[`dwarf_v02_config.yaml`](./dwarf_v02_config.yaml) — **identique** à
`dwarf_v01_config.yaml` : PPO, batch 128, buffer 2048, learning rate 3e-4
décroissant, 2×256 hidden units, gamma 0.99.

```powershell
# Terminal (venv ML-Agents activé), depuis le dossier ML-Agents
mlagents-learn D:\git\dwarven-arena\dwarven-arena\docs\runs\dwarf_v02_config.yaml --run-id=dwarf_v02
```

## Budget d'entraînement

| Métrique | `dwarf_v02` | `dwarf_v01` |
|---|---|---|
| Steps | 3 000 000 | 3 000 000 |
| Durée | ~5 h 15 (RTX 3080, local) | ~5 h 03 |
| Reward moyen final (lissé) | **25.0** | 28.4 |
| Entropie finale | **0.92** | 1.61 |
| Longueur d'épisode finale | ~597-599 | ~599 |

## Interpréter TensorBoard

Les courbes de `v01` (gris) et `v02` (bleu) sont superposées.

| Courbe | Ce qu'elle doit faire | Ce run |
|---|---|---|
| Cumulative Reward | Monter, puis se stabiliser | ~23 → 25 en 500k steps, puis plateau très plat (24.5-25.3) ✅ |
| Policy/Entropy | Baisser mais pas à zéro | 2.8 → 0.92, régulière ✅ (plus basse que `v01`) ⚠️ |
| Episode Length | S'allonger si l'agent survit mieux | Colle à ~599 dès 400k steps — voir ci-dessous ⚠️ |

![Cumulative Reward](../media/dwarf_v02/tensorboard_cumulative_reward.png)

*Reward total moyen par épisode. `v02` monte plus lentement au début, puis se stabilise à ~25 avec très peu de variance ; `v01` oscille entre 25 et 28.5 pendant tout le run mais finit plus haut (28.4).*

![Entropy](../media/dwarf_v02/tensorboard_entropy.png)

*Incertitude de la politique. `v02` converge beaucoup plus vite et plus bas (0.92 contre 1.61) : l'agent est devenu nettement plus déterministe.*

![Episode Length](../media/dwarf_v02/tensorboard_episode_length.png)

*Nombre de décisions par épisode. Les deux runs sont collés à ~599.*

## Ce que disent les courbes

Établi par les courbes :

- **Les deux runs atteignent le plafond de durée d'épisode.** `MaxStep` vaut 3000
  et une décision a lieu tous les 5 steps, soit ~600 décisions : la longueur
  d'épisode à ~599 veut dire que le nain **survit presque toujours jusqu'à la
  limite de temps**. Cette courbe ne distingue donc plus rien — elle ne mesure
  plus la survie mais le plafond. (Le commentaire de `DwarfAgent.Initialize`
  parlait à tort de « 3000 décisions ».)
- **Le reward mesure donc surtout le nombre de kills en temps limité**
  (+1 par kill, −0.3 par dégât, −0.0005 par step ≈ −0.3 sur l'épisode).
  `v02` en fait environ 3.4 de moins que `v01` avec les mêmes hyperparamètres.
- **`v02` se fige plus tôt** : plateau dès ~1M steps et entropie basse, alors
  que `v01` explorait encore à 3M.

Hypothèses, **non vérifiées** :

- **Orcs plus lourds** : le prefab de l'Orc a une masse de 1 contre 0.5 pour le
  Gobelin, donc un knockback deux fois plus court — plus dur à pousser dans un
  ravin ou sur un pic, alors que c'est la source de kills du run précédent.
  Ils représentent 20 % des spawns (`orcChance`).
- **Stratégie plus prudente/uniforme** : l'entropie basse et la faible variance
  suggèrent une politique stable, mais pas nécessairement meilleure.

## Retour d'expérience

**Observé en inférence** : le nain **ne se déplace plus**. Il reste au milieu
de l'arène et tue les ennemis avec le marteau et le bouclier, sans jamais
utiliser les pics ni les ravins — alors que pousser les ennemis dedans est le
cœur de la boucle de gameplay. Comme pour `v01`, le reward stable (≈ 25) cachait
un comportement qui va à l'encontre de l'intention de design.

**Pourquoi l'agent n'a aucune raison d'utiliser le décor** (vérifié dans le
code) :

- **Le reward ne distingue pas la façon de tuer.** Le score du jeu donne ×1
  (marteau) à ×3 (pic au bouclier), mais `DwarfAgent` récompense `Kills`, un
  compteur plat de +1 par ennemi mort (choix documenté dans le code pour garder
  le reward constant). Un kill au marteau au centre vaut donc autant qu'un
  kill sur un pic, en bien moins risqué.
- **Les ennemis viennent à lui.** Ils le poursuivent : rester immobile au
  centre et frapper ce qui arrive est le plus sûr, et bouger n'est jamais
  récompensé. Un coup de marteau raté déclenche en plus un ralentissement
  (`missPenaltyDuration`), ce qui décourage de se déplacer et d'attaquer
  au hasard.
- **L'agent ne voit pas les ravins.** Les observations couvrent la position du
  nain, ses états, 2 pics (à zéro en pratique, `spikeTraps` étant vide) et les 3
  ennemis les plus proches, mais **aucun ravin**, et rien sur la position d'un
  ennemi *par rapport à un danger* — l'information dont il aurait besoin pour
  décider de pousser.
- **Les ennemis évitent les dangers** (depuis `v01`) : les pousser dedans est
  plus difficile qu'avant, sans incitation pour compenser.

Hypothèse, non vérifiée : les Orcs (masse 1, knockback deux fois plus court)
rendent la poussée dans un danger encore moins rentable que le marteau direct.

## Pistes pour la suite

Leviers étudiés pour `v03`, et décisions :

1. **Récompenser la façon de tuer** — retenu : kill poussé = +1 × multiplicateur
   du score, au lieu de +1 plat.
2. **Faire voir les dangers** — retenu : 5 pics, ravin le plus proche du nain, et
   danger le plus proche de chaque ennemi observé (39 observations).
3. **Rendre le kill direct moins rentable** — retenu : +0.3 au lieu de +1.
4. **Pénaliser l'immobilité** — écarté : le joueur doit pouvoir jouer comme il veut.
5. **Épisode plus long** — écarté : même durée, pour garder un point de comparaison.

Le gameplay appris changera avec ces modifications : c'est le but, on observe
les mouvements optimaux pour ajuster ensuite le jeu. `v01`, `v02` et `v03` ne
seront pas directement comparables (observations et reward différents).
Voir aussi le Stage 9 de la [ROADMAP](../../ROADMAP.md).
