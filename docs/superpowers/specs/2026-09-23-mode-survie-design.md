# Mode « survie » façon Driver 2 — design

Date : 2026-09-23 · Branche : main · Code : `scripts/Survie.cs`

## Problème

Après les tests en jeu du 2026-09-23 :

- **Mode « voitures »** (voitures du script) : conduite molle, facile à semer.
- **Mode « étoiles »** (police du jeu) : elle arrive de trop loin, poursuite trop
  facile. Avec les renforts rapprochés (dispatch à 60 m, cadence ×4), le jeu a
  **planté** 2 s après un renfort à 3 étoiles. Sans ça, la police « apparaît un
  peu n'importe où et ne conduit pas bien ».

Demande du streamer : s'inspirer du mode **Survival de Driver 2** (PS1, 2000) —
une poursuite sans fin contre la police — pour un mode fun qui ne plante pas.

## Ce que fait Driver 2 (code décompilé, projet REDRIVER2)

Sources lues : `src_rebuild/Game/C/cop_ai.c`, `civ_ai.c`, `mission.c`,
`dr2limits.h` (github.com/OpenDriver2/REDRIVER2).

- **Parc fixe** : `car_data[MAX_CARS]`, `MAX_CARS = 20` voitures en tout autour
  du joueur (civils + flics), jamais d'allocation. `maxCivCars = 14`,
  `maxCopCars = 4` (fixé par la mission ; en survie, 4).
- **Ping out / ping in** : une voiture au-delà de 24000 unités du joueur est
  effacée (`PingOutCar` vide juste la case) ; une nouvelle est posée sur un
  cercle autour du joueur (flics : rayon 20480, plus loin que les civils à
  16384), à un angle qui tourne (56 positions pour les flics), seulement sur une
  voie de route valide. Posée, elle est aussitôt tournée vers le joueur
  (`justPinged`). Le brouillard de la PS1 cachait tout ça.
- **Réglages de la survie** (`InitCops`, `mission.c`) : `gCopRespawnTime = 0`
  (un flic manquant est remplacé tout de suite), vitesse et puissance ×1,12
  (4596/4096) plus jusqu'à +25 % avec le « felony » (`autoMaxPowerScaleLimit`),
  `gBatterPlayer = 1` (ils percutent), `gCopData.immortal = 1` (jamais
  détruits), `CopsCanSeePlayer = 1` (ils savent toujours où est le joueur).
- **Nombre de flics** (`ControlNumberOfCops`) : seuils de felony → nombre voulu,
  borné par `maxCopCars`. En trop : puissance coupée (`cutOffPowerScale`), ils
  décrochent et sortent de la bulle d'eux-mêmes. Jamais de disparition à l'écran.
- **Conduite** (`CopControl1`) : de loin, suivre la route vers le joueur ; près
  (< 2800 unités), viser l'endroit où il sera (temps d'interception borné à 60
  images = 2 s), et en mode « batter », viser son **flanc** ; cible derrière :
  marche arrière ; plantée 10 images : manœuvre de dégagement 22 images
  (recule en braquant, stratégies alternées).
- Détail : seules 2 sirènes s'entendent à la fois (canaux son de la PS1).

## Décisions (GTA V)

1. **Parc fixe de 4 flics** (`MAX_FLICS`, 1 à 6) : une voiture Police Nationale
   (`pranger`) + un policier chacun, créés une fois (une création / 3 s au plus),
   **jamais supprimés** pendant la poursuite. Modèles chargés une fois et gardés
   jusqu'à la fin. Aucun va-et-vient d'entités.
2. **Aucune police du jeu** pendant la poursuite : `SET_MAX_WANTED_LEVEL(0)`
   (rendu à la fin et à l'arrêt du script). Plus de renforts, d'hélicos, de
   barrages ni de tirs — ce qui a fait planter le jeu. Étoiles **factices**
   (`SET_FAKE_WANTED_LEVEL`) = la chaleur, pour les viewers.
3. **Chaleur = le felony** : +1 par cadeau (max 5). Flics voulus = min(chaleur,
   `MAX_FLICS`). Rattrapage ×(1,05 + 0,03 × chaleur). La poursuite s'arrête
   `DUREE_S` (60 s) après le dernier cadeau ; le temps compte depuis le premier.
4. **Apparition jamais sous les yeux du joueur** : route goudronnée à `DISTANCE_M`
   (80 m) dans une direction candidate (nouveau flic : derrière puis côtés, jamais
   devant ; recyclé : les 8 directions, départ tournant), sens de circulation
   tourné vers le joueur (`GET_NTH_CLOSEST_VEHICLE_NODE_FAVOUR_DIRECTION`), écart
   de hauteur ≤ 12 m, pas sur un autre flic. Refusé dans le cône de la caméra
   (demi-angle 60°), sauf à 60 m ou plus ET caché par le décor
   (`WOULD_ENTITY_BE_OCCLUDED`). Pas de point trouvé : on réessaie, jamais de
   repli « n'importe où ».
5. **Recyclage (ping out/in)** : hors champ seulement, un flic retourné ou à
   l'eau, à plus de 150 m, ou planté 2,5 s est **replacé** (même voiture),
   réparé, relancé (derrière : à la vitesse du joueur). Un toutes les 0,4 s au plus.
6. **Indestructibles** (`INDESTRUCTIBLES=1`) : voiture et policier invincibles,
   pneus increvables, moteur remis à neuf ; les chocs cabossent quand même.
7. **Conduite** : > 50 m, suivre les routes vers la position anticipée ; ≤ 50 m,
   viser le **flanc** du joueur au point d'interception (≤ 2 s), **20 m au-delà**
   de lui (l'IA de GTA freine en approchant de son but : visé au-delà, elle
   arrive à pleine vitesse) ; ligne droite forcée < 40 m ; coup d'accélérateur
   face à lui < 15 m. Planté sous ses yeux 1,2 s : marche arrière en braquant
   (`TASK_VEHICLE_TEMP_ACTION` 13, puis 28 ; jamais 16-18, qui font planter le jeu).
8. **Rattrapage invisible** : hors champ, à plus de 35 m, tourné vers le joueur
   et moins rapide que lui : `SET_VEHICLE_FORWARD_SPEED` à sa vitesse ×facteur
   + 3 m/s (20 m/s au moins, 65 max).
9. **À pied** : les flics approchent à 14 m/s sans l'écraser ; l'un d'eux arrêté
   à moins de 8 m pendant 2 s → **« Attrapé ! »**.
10. **Fin et score** : survécu (plus de cadeau depuis `DUREE_S`), attrapé, mort,
    mission ou changement de personnage. Message en jeu (notification) avec le
    temps et le record (`scripts\GTAMax.record`). Messages aussi au début et à
    chaque cadeau (+1 flic / la police accélère).

## Garde-fous anti-crash

Entités bornées (≤ 4 voitures + 4 policiers), zéro suppression/création en
boucle, modèles résidents, zéro police du jeu, recyclages limités, requêtes de
route limitées (≤ 8 par essai, essais espacés de 0,25 à 0,5 s), chaque flic sous
try/catch, aucune action « temporaire » connue pour planter.

## Tests

`plugin.test.js` (harnais C#) : poursuite (début, chaleur, plafond, fin 60 s
après le dernier cadeau, durée, rattrapage), directions candidates (derrière
d'abord, jamais devant pour un nouveau ; 8 directions pour un recyclé),
distances d'apparition, cône de la caméra, raisons de recycler (jamais à la
vue), temps d'interception, point visé (flanc, au-delà), vitesse de rattrapage,
textes. Compilation des deux fichiers contre le vrai `ScriptHookVDotNet3.dll`.
Le comportement se vérifie en jeu (« Tester sans live », rafale de 3, puis
journal `GTAMax.log` : apparitions « hors champ / caché par le décor », résumé
toutes les 10 s).

## Hors périmètre

Chronomètre à l'écran en continu (il faudrait dessiner à chaque image : le
script tourne toutes les 100 ms), densité de la circulation, barrages.

## Retour du test en jeu n°1 (2026-09-23, 21:37-21:39)

**Journal** : joueur à 144-233 km/h (voiture add-on `cupra22f`). 63 recyclages
en 1 min 45, jusqu'à 3 par seconde ; les flics posés derrière (50-120 m) ou sur
les côtés (65-123 m) étaient « semés » 1 à 2 s plus tard ; 12 « retourné ou à
l'eau » : le rattrapage invisible les poussait à 234 km/h. Au plus près dans les
résumés : 16-18 m (les contacts n'étaient pas encore comptés) ; le streamer :
« ils n'ont jamais le temps de m'attraper ».

**Crash** : `GTA5.exe+0x10078DC`, lecture de `0xFFFFFFFFFFFFFFFF`, dans le code
du jeu (aucune frame du plugin, « No Active scrThread »). Les 32 minidumps de
`%LOCALAPPDATA%\Rockstar Games\GTAV\CrashLogs` montrent 22 crashs AVANT
l'installation de GTA Max (21-22/09), surtout `ERR_MEM_EMBEDDEDALLOC_GUARD_*`
/ `_FREE_2` (mémoire du jeu corrompue, typique des add-ons), et 5 des 6
signatures des crashs pendant les tests GTA Max existaient déjà sans lui. La
poursuite (vitesse, streaming) le fait tomber plus tôt ; la cause est côté mods.

**Corrections** :
- Joueur rapide (> 54 km/h) : les flics viennent **devant**, sur la route qu'il
  va prendre (`FIND_SPAWN_POINT_IN_DIRECTION`), à 3 s de lui (90 à 170 m),
  tournés vers lui ; visibles seulement au loin (≥ 120 m) sinon cachés/hors
  champ ; à défaut derrière, sur sa route (60 m).
- Sens de la route toujours retourné vers le joueur (`CapDeRouteVers`) : le jeu
  peut rendre le sens de la chaussée d'en face.
- « Semé » = plus de 120 m **derrière**, ou plus de 200 m partout (un flic posé
  devant à 170 m n'est plus recyclé aussitôt).
- Un même flic replacé au plus une fois toutes les 3 s ; 1 recyclage / 0,75 s au plus.
- Rattrapage invisible plafonné à 180 km/h, seulement bien aligné (±25°) et
  4 roues au sol ; coup d'accélérateur au contact jamais « à la baisse ».
- Rayon autour d'un joueur lent : 80 → 60 m.
- Journal : contrôle 2 s après chaque pose (« se rapproche » ou « S'ÉLOIGNE »),
  contacts par flic et au total.

## Retour du test en jeu n°2 (2026-09-23, 21:57-22:02)

**Journal** : pas de crash. 3 min 22 s de survie (fin : mort), 7 contacts en tout
(0 à 4 par flic). Les flics posés devant arrivaient de face (« 155 → 15 m en 2 s »),
ceux posés loin derrière s'éloignaient (« S'ÉLOIGNE »). Le streamer : « on faisait
des face-à-face à chaque fois » ; il veut qu'ils apparaissent à côté de lui, un peu
devant, et fassent la course avec lui.

**Corrections — la COURSE** (joueur à plus de 54 km/h) :
- **Apparition** : sur la voie d'à côté (4 ou 7,5 m sur le côté), 15 ou 25 m
  derrière le joueur — donc derrière la caméra, hors de sa vue —, au même niveau
  (écart ≤ 3 m), sur la route (`IS_POINT_ON_ROAD`), voie libre (aucune voiture à
  3,5 m), tourné dans SON sens, lancé 6 m/s plus vite que lui : à sa hauteur en
  1 à 2 s. À défaut, derrière lui sur sa route (60 m). Plus jamais posé devant,
  tourné vers lui. (Posé « un peu devant » directement, il surgirait sous ses yeux.)
- **Consignes de course** (`ReglesSurvie.ConsigneCourse`, repère du joueur) :
  derrière → il remonte sur son côté (joueur + 8 m/s) ; à sa hauteur (±8 m) → il
  serre vers lui en ligne droite (coup d'épaule, joueur + 2 m/s) ; devant → il se
  rabat devant lui, à peine moins vite (joueur − 2 m/s : il lui barre la route).
  La cible court toujours devant le flic dans le sens de la course : jamais de
  demi-tour, jamais de face-à-face. La vitesse de croisière suit celle du joueur.
- **Vitesse de pointe adaptée** (`BonusPourSuivre`) : +8 % au-dessus de la voiture
  du joueur (`GET_VEHICLE_ESTIMATED_MAX_SPEED` contre
  `GET_VEHICLE_MODEL_ESTIMATED_MAX_SPEED` du modèle de police), jamais moins que
  `BONUS_MOTEUR`, 150 % au plus ; recalculée à chaque recyclage et toutes les 10 s
  (il peut changer de voiture).
- Rattrapage invisible remonté à 250 km/h (toujours aligné, 4 roues au sol).
- Journal : phase de chaque flic (rattrape / à sa hauteur / devant / percute /
  traque) et bonus moteur dans le résumé.

## Retour du test en jeu n°3 (2026-09-23, 22:11-22:13)

**Journal** : l'entrée « à côté, 15 m derrière » marche (« 16 → 3 m » en 2 s,
contacts réguliers ; attrapé à pied au bout de 42 s). Mais tous les recyclages
se ressemblent (« à côté, 15 m derrière » à chaque fois), et joueur lent, le flic
passait en « percute » : 216 km/h demandés + coup d'accélérateur instantané.
Le streamer : « les véhicules qui apparaissaient derrière étaient bien, mais il
faudrait varier et enlever le boost qu'ils peuvent avoir pour te rentrer dedans ».

**Crash** 22:13 : `ERR_MEM_EMBEDDEDALLOC_GUARD_4` (même signature que le 21/09
avant GTA Max et le 23/09 à 19:57). 2 flics du plugin seulement, 79 voitures
ambiantes. Piste : les mods de police (voiture `pranger`, tenue `s_m_y_cop_01`,
posée le 22/09) ou d'autres add-ons ; le dump ne contient pas les noms d'assets.

**Corrections** :
- **Plus de boost** : coup d'accélérateur supprimé ; joueur lent au contact →
  « approche » à sa vitesse + 4 m/s (8 au moins), visée 6 m au-delà (20 avant) ;
  de loin → 15 m/s de plus que lui (25 à 60) au lieu de 60 m/s d'office.
- **Entrées variées** (`EntreesPourCourse`, l'ordre tourne) : à côté (une fois sur
  deux en tête), derrière sur sa route, **rue de côté** devant lui (25-50°, à 3 s,
  ≥ 2 s devant, ≥ 15 m de sa ligne, cachée) d'où le flic sort vers sa route, dans
  son sens.
- **Rôles tournants** (`RolePour`) : coureur (se cale et serre), bloqueur (double
  sans serrer puis barre la route), collant (dans sa voie, pare-choc contre
  pare-choc à +1,5 m/s, se laisse doubler). Jamais plus de 10 m/s au-dessus du
  joueur, jamais de demi-tour.
- **`[POLICE] PED`** : policier configurable. Test des crashs proposé :
  `MODELES=police` + `PED=s_m_y_hwaycop_01` (d'origine) ; plus de crash = mods police.
