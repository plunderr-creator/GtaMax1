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

## Retour du test en jeu n°4 (2026-09-23, 22:27-22:34)

**Journal** : pas de crash, 7 min 33 s (fin : survécu, nouveau record), 18 cadeaux,
14 contacts seulement. **197 recyclages** (un toutes les 2,3 s) : 174 « semé »,
18 « retourné ». Posés « à côté » (114) ou « derrière » (38), les flics
décrochaient aussitôt (126 « S'ÉLOIGNE » sur 198 contrôles) : vie moyenne 7 à
10 s, invisibles la plupart du temps. Seule l'entrée « rue de côté » arrivait
toujours sur le joueur (0 « S'ÉLOIGNE » sur 38), mais sans rendez-vous avec lui.
Le streamer : « il faut que la police arrive en face, à gauche, dans les
ruelles ; rendre visibles et dynamiques les rencontres pour créer une vraie
course-poursuite ».

**Choix du streamer** : ruelles des deux côtés (au hasard) ; en sortant, le flic
lui **coupe la route** (pas de percussion par le flanc) ; flics **sur la
mini-carte**, points rouge/bleu.

**Corrections — l'EMBUSCADE** (joueur à plus de 54 km/h) :
- **Entrée par défaut** (`EntreesPourCourse`) : l'embuscade d'abord, toujours ;
  à défaut (pas de ruelle : autoroute, campagne), à côté ou derrière, en alternance.
  Nouveaux flics et flics semés passent par là. 2 embuscades à la fois au plus
  (les autres chassent), sur deux carrefours à 40 m l'un de l'autre au moins.
- **Où** (`Embuscade`, `DistancesEmbuscade`, `RueEnTravers`) : sa route là où il
  sera dans 4 s (80 à 170 m, puis −30 et +30 m ; jamais à moins de 60 m), nœud le
  plus proche dans son sens (virage < 53°). De chaque côté (premier côté au
  hasard), sonde à 25 puis 45 m : nœud le plus proche, **ruelles comprises**
  (`GCNF_INCLUDE_SWITCHED_OFF_NODES`). Accepté s'il est à 10-60 m de sa route, à
  25 m au plus du carrefour le long de sa route, au même niveau (±6 m) et sur une
  rue **en travers** (53° à 127°). Libre (aucune voiture à 4 m, pas d'autre flic),
  caché par les immeubles ou hors champ (`Accepter`). Posé tourné vers le carrefour.
  Au plus 15 requêtes de route par essai.
- **Le rendez-vous** (`SuiviEmbuscade`, `VitesseEmbuscade`) : toutes les 250 ms,
  vitesse = distance au carrefour / (temps du joueur jusqu'au carrefour − 2 s),
  3 à 25 m/s (90 km/h : il sort vite, il ne charge pas) — `SET_DRIVE_TASK_CRUISE_SPEED`,
  ordre redonné chaque seconde vers sa route 25 m après le carrefour, dans son sens
  (conduite + `DF_UseSwitchedOffNodes` : il roule dans les ruelles). Élan de départ
  caché (`SET_VEHICLE_FORWARD_SPEED`). À 10 m du carrefour : **il débouche**, 2 s
  avant le passage du joueur. Pendant l'embuscade : ni recyclage ni rattrapage.
- **Fin ratée** : joueur passé avant lui, joueur parti sur une autre route (hors
  d'un cône de ±31° devant lui), plus de 10 s, joueur sous 29 km/h, retourné ou
  bloqué 2,5 s dans sa ruelle → il chasse comme les autres (ou est recyclé).
- **Il coupe la route** (`RoleEnCourse`) : 4 s de bloqueur à la sortie de sa
  ruelle, puis son rôle. **Course** (`EnCourse`) aussi pour un flic **devant** le
  joueur jusqu'à 250 m (avant : 60 m, au-delà il revenait vers lui par la route =
  face-à-face) ; **loin devant** (> 60 m) il file dans son sens à mi-vitesse du
  joueur, qui le rattrape (`ConsigneCourse`, phase « loin devant »).
- **Mini-carte** : chaque flic a un point `PoliceCarDot` rouge clignotant rouge/bleu
  (`SET_BLIP_FLASHES_ALTERNATE`), effacé quand il est rendu au jeu ou supprimé.
- **Journal** : « DÉBOUCHE de sa ruelle (à gauche) — joueur à X m, Y s avant son
  passage, à l'écran / PAS à l'écran », « embuscade ratée (raison) », bilan des
  embuscades (posées, débouchées dont à l'écran, ratées) dans le résumé et à la fin.

## Retour du test en jeu n°5 (2026-09-23, 23:04-23:08)

**Journal** : pas de crash (jeu quitté à 23:08), 3 min 30 s, 12 cadeaux. 19 embuscades
posées, **3 seulement débouchées** (toutes à l'écran) : 6 « passé avant lui » — posés
à 40-55 m au fond de la ruelle, réglés en vitesse, l'IA y roulait à 20-40 km/h ; 9
« tourné » — la direction instantanée du joueur hors d'un cône de ±31° : un simple
coup de volant suffisait. Le streamer : « pas convaincant, la limite à 4 est trop
faible » ; puis « il faut que je puisse les semer ».

**Choix du streamer** : **12 flics** au plus ; un flic semé **abandonne** (un de moins,
une étoile de moins) ; tous semés → « SEMÉS ! », poursuite gagnée.

**Corrections** :
- **12 flics** (`MAX_FLICS=12`, 1 à 20), une création toutes les 1,5 s (3 avant), un
  recyclage toutes les 0,5 s (0,75 avant), la moitié du parc en embuscade à la fois
  (6), rattrapage plafonné à celui de la chaleur 5 (×1,2).
- **Embuscade immobile** : ruelle à 10-35 m de sa route (sondes 18 puis 30 m),
  carrefour là où il sera dans 5 s (80 à 180 m). Posé, le flic est **immobilisé**
  (`FREEZE_ENTITY_POSITION` + `TASK_VEHICLE_TEMP_ACTION` 1 « attendre »), sirène
  allumée. Il **part** quand le joueur fonce vers le carrefour (±27°) à moins de
  `TempsDeSortie` (0,3 s + profondeur / 12 m/s) + 1,5 s, ou passe à 30 m de lui
  (jamais un mur immobile) : dégelé, ordre de sortie à 20 m/s, élan de 10 m/s
  seulement hors de sa vue. Débouché = à 8 m du carrefour. Parti, plus de délai ;
  bloqué 2,5 s → raté. Pas parti en 8 s → il chasse. Jamais laissé gelé (sortie
  d'embuscade, recyclage, fin, script arrêté).
- **« Tourné »** = le joueur s'éloigne du carrefour de 25 m (plus sa direction
  instantanée) ; « passé avant lui » = carrefour derrière lui, à 25 m au plus de sa
  route et 60 m de lui.
- **Semer** (`EstSeme`, `SortDuFlicLoin`, `Poursuite.Semer`) : un flic **venu au
  contact** (à 60 m ou moins depuis sa pose), puis hors de vue à plus de 150 m
  derrière (250 m partout) pendant **4 s**, **abandonne** : rendu au jeu, chaleur − 1
  (min(chaleur, max) − 1 : pas de remplaçant), étoiles factices mises à jour,
  notification « Flic semé ! Encore N ». Chaleur 0 → fin « semés », « SEMÉS ! » avec
  le temps et le record. Un flic **jamais venu au contact** (embuscade dans une rue
  que le joueur n'a pas prise) le cherche encore : « perdu de vue », reposé ailleurs.
  Plus de recyclage des flics semés ; plus de rattrapage invisible au-delà de 100 m.
- **Journal** : « part de sa ruelle — joueur à X s du carrefour », « SEMÉ — il
  abandonne, N à ses trousses », semés dans le résumé et à la fin.

## Retour du test en jeu n°6 (2026-09-23, 23:30-23:37)

**Journal** : pas de crash. Poursuites de **9 à 21 s** finies « semés » : 1 à 3 flics
(cadeaux un par un), en « traque » à 25-75 km/h derrière un joueur à 90-120 km/h,
semés 10-20 s après leur pose — le rattrapage invisible coupé au-delà de 100 m les
laissait décrocher. Peu d'embuscades (le joueur souvent sous 54 km/h au cadeau) ;
sorties encore trop lentes : 26-34 m en 4 s (sans élan sous les yeux du joueur),
« déjà passé ». Le streamer : « encore trop facile, peut-être ajoute la vraie police
du jeu en même temps ».

**Corrections** :
- **La vraie police du jeu en même temps** (`[SURVIE] POLICE_DU_JEU=1`, défaut) :
  chaque cadeau ajoute aussi une étoile (réglages de `[ETOILES]` : +1, 3 max — la
  police française du `dispatch.meta` — sans armes, sans hélicos, voitures du jeu
  boostées sauf les nôtres, `IS_ENTITY_A_MISSION_ENTITY`), avec une tenue courte
  (`[SURVIE] TENUE_S=15` : 15 s après chaque cadeau elle sait où il est, puis on peut
  la semer). Vraies étoiles à l'écran (plus d'étoiles factices ni de recherche max 0).
  Police du jeu retouchée seulement pendant une poursuite ; hélicos rendus à la fin.
- **« SEMÉS ! »** (`TousSemes`) = tous nos flics semés **et** plus une étoile ; notif
  « Tous semés ! Perds maintenant tes étoiles ». Fin survécu, attrapé ou semés : étoiles
  effacées (`EffacerEtoilesALaFin`) ; mort : le jeu s'en charge. **Arrêté** par la police
  du jeu (`IS_PLAYER_BEING_ARRESTED`) = « attrapé ».
- **Plus dur à semer** : rattrapage invisible jusqu'à 150 m (la distance où il est
  semé) — en ligne droite il revient, il faut tourner pour le semer ; 5 s hors de vue
  (4 avant).
- **Sortie de ruelle** : `TempsDeSortie` = 0,5 s + profondeur / 8 m/s (mesuré), ruelles
  à 10-30 m (sondes 15 puis 25 m), élan de 10 m/s toujours, sortie à 25 m/s.

## Semer = casser la vue (2026-09-24)

**Problème** (5 parties du 23-24/09 avec la police du jeu) : 8 à 20 flics semés par
partie. Un flic distancé 5 s abandonne, et chaque abandon enlève un flic pour de
bon. Le streamer : « trop facile à semer ». Choix : semer comme les étoiles de GTA.

**Règles** (`[SURVIE] POLICE_DU_JEU=1` ; à 0, rien ne change) :
1. **Vu** : toutes les secondes, si un de nos flics au volant est à 100 m ou moins
   du joueur, ligne de vue dégagée (`HAS_ENTITY_CLEAR_LOS_TO_ENTITY`, 17) :
   `REPORT_POLICE_SPOTTED_PLAYER` → les étoiles restent pleines.
2. **Étoiles pleines** : un flic distancé (même seuil qu'avant : hors de vue, 150 m
   derrière ou 250 m partout, pendant 5 s) **réapparaît** hors de vue (`Recycler`,
   raison « distancé ») au lieu d'abandonner. La chaleur ne baisse plus.
3. **Étoiles grises** (`ARE_PLAYER_STARS_GREYED_OUT` : plus personne ne le voit) :
   ni recyclage, ni création, ni rattrapage invisible. Chaque flic roule à 20 m/s
   vers le centre de recherche du jeu (`GET_PLAYER_WANTED_CENTRE_POSITION`), décalé
   de 40 m selon son numéro (pas tous au même point). Une embuscade en cours finit
   comme avant. Un flic le revoit → règle 1 → étoiles pleines, la chasse reprend.
4. **« SEMÉS ! »** = plus aucune étoile, après en avoir eu pendant cette poursuite
   (sinon une étoile refusée par le jeu finirait la poursuite aussitôt). La tenue de
   15 s après chaque cadeau reste : un cadeau les remet sur lui.

Journal : une ligne à chaque passage étoiles pleines ↔ grises.

**Tests** : fin « semés » (avec la police du jeu, les étoiles seules décident ;
garde « a eu des étoiles ») ; sort d'un flic distancé (réapparaît / abandonne /
rien pendant la recherche) ; point de fouille (40 m du centre, un point par numéro).

**Fait (version réduite de 90 %, demande du streamer)** : règles 2 et 4 ; étoiles grises =
aucun recyclage ni rattrapage invisible ; un de nos flics qui le voit (100 m, ligne de vue 17,
à chaque passage de 250 ms) → `REPORT_POLICE_SPOTTED_PLAYER`. Pas de fouille (nos flics visent
toujours le joueur), pas de garde « a eu des étoiles », pas de ligne de journal. Tests 284.
