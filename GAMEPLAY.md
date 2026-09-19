# 🎮 Dwarven Arena — Document de Gameplay

## Vue d'ensemble

- **Genre** : survie arcade / arène à vagues, 2D vue du dessus
- **Format** : parties courtes (1 à 5 minutes), rejouables, score
- **Tonalité** : cartoon brutal, physique comique, explosions colorées
- **Inspirations** : le défilé des *300*, le knockback tactique
  d'*Into the Breach*, le rythme infernal de *Vampire Survivors*

## Le héros : le nain

### Caractéristiques

| Propriété | Valeur | Note |
|-----------|--------|------|
| Points de vie | 3 | Mort instantanée à 0 |
| Vitesse (normal) | Rapide | Plus rapide que les gobelins |
| Vitesse (récupération) | Lente | Égale aux gobelins |

### Le marteau de guerre

- **Coup lent** avec un **cooldown élevé** — chaque swing est une décision
- Dégâts + **projection physique vers l'avant** : les ennemis touchés
  sont poussés et entrent en collision avec ceux derrière → **effet domino**
- **Swing raté** = pénalité de vitesse prolongée (le nain traîne son
  marteau) : punition incarnée, pas juste un débuff abstrait
- Fenêtres de vulnérabilité : le windup du coup laisse le nain exposé

### Le bouclier

- **Blocage frontal** : réduit/annule les dégâts venant de la moitié avant
- **Micro-poussée** : permet de repousser légèrement les ennemis —
  ajuster le positionnement, pas tuer
- Lève le bouclier **pendant le cooldown du marteau** : c'est la parade
  défensive du cycle de combat
- Légère lenteur quand le bouclier est levé

### La boucle de combat (core loop)

    Marteau prêt
      → se positionner (dos au ravin ?)
      → encaisser / repousser avec le bouclier pendant le windup
      → SWING → dominos → ennemis dans le vide
      → reculer → attendre le cooldown, bouclier levé
      → recommencer

## Les ennemis

| Propriété | Gobelin 🟢 | Orque 🟠 |
|----------|-----------|----------|
| Nombre | Très nombreux | Rares |
| Masse | Légère (0.5) | Lourde (3) |
| Vitesse | Moyenne | Lente |
| Facilité à pousser | Très facile | Difficile |
| Rôle design | Pop-corn, dominos à chaînes | Mur vivant, absorbe les coups |

### Émergence souhaitée

- Les gobelins servent de chaînons : poussés les uns contre les autres
- Les orques **bloquent la propagation** des dominos et obligent à
  changer d'angle d'attaque
- Deux orques côte à côte = mur quasi infranchissable pour un swing :
  les contourner devient nécessaire

## L'arène

- **Ravins tout autour** : chute = mort instantanée (ring-out façon *300*)
- **Goulot d'entrée** : passage étroit par lequel arrivent les ennemis,
  flanqué de ravins — piège à dominos naturel
- **Pièges à pics au centre** : trous avec des piques, zone de kill
  plus petite mais en position stratégique
- Des murs épars pour du cover et du positionnement

### Entrée en scène

Brève intro scriptée (5-7 s, skippable, vue de dessus fixe) :
le nain traverse un pont de bois → **le pont s'écroule derrière lui** →
il est piégé dans l'arène. Justifie l'impossibilité de fuir et sert de
tutoriel visuel : le joueur voit les ravins et les pics avant le premier
combat.

## Mort et feedback (le "juice")

- **Ennemis** : explosent en **vert** 💥 (cascade visible sur les chaînes)
- **Nain** : explose en **rouge** 💥
- Petit délai comique (0,5 s de panique) pour l'orque qui bascule
  dans le ravin avant d'exploser
- Screen shake léger sur les gros impacts de marteau

## Score et progression

- Kill gobelin : 10 pts, kill orque : 20 pts
- **Multiplicateur de chaîne** : pousser N ennemis en un seul
  swing/chaîne rapporte plus que N kills séparés → récompense le
  style de jeu agressif et réfléchi, décourage le camping
- Vagues croissantes : introduction progressive des orques au fil
  des vagues (courbe d'apprentissage)

## Mesures anti-camping

- Les gobelins contournent et attaquent en flanc
- Le multiplicateur de chaîne valorise les grandes frappes
- Aucun soin passif : survivre passivement ne rapporte rien

## Principes de design

1. **Chaque action a un coût** : le marteau punit le spam, le bouclier
   ralentit, l'agressivité mal calibrée tue
2. **Le décor est une arme** : le skill ceiling est le positionnement,
   pas la dextérité
3. **Lisible en 3 secondes** : feedback visuel constant (état du nain,
   direction des coups, danger)
4. **Compatible IA** : chaque mécanique est conçue pour être apprise
   par un agent RL — actions discrètes, récompenses claires, pas
   d'information cachée