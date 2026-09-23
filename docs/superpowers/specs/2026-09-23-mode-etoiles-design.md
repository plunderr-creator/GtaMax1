# Mode « étoiles » — la vraie police du jeu, sans tirs ni hélicos — design

Date : 2026-09-23 · Branche : main

## Problème

Quatre tests en jeu du mode « voitures » (une voiture de police créée par le
script à chaque cadeau) : la mission RAM freine les voitures, la poursuite du
jeu lancée à la main reste plantée ou molle, le fourgon CRS s'arrête, et même
la traque maison se fait semer. Les voitures du script n'ont pas le cerveau de
la police du jeu.

Idée du streamer : utiliser le **système de recherche (étoiles)**, en retirant
à la police la possibilité de tirer, et les hélicoptères.

## Décisions

### 1. Un cadeau = une étoile de recherche

`SPAWN_POLICE` ne change pas côté serveur : c'est le `MODE` de `GTAMax.ini`
qui décide ce que fait le plugin — `etoiles` (défaut), `voitures` (l'ancien
mode), `les_deux`. Le serveur ne sait pas comment la police arrive.

- +1 étoile par cadeau (`PAR_CADEAU`), plafond `MAX` = 3 : lu dans le
  `dispatch.meta` effectif (`mods\update\update.rpf`), jusqu'à 3 étoiles le
  jeu n'envoie que des `pranger` (voiture Police Nationale) avec des
  `S_M_Y_Cop_01` (tenue Police Nationale), plus des barrages `pranger` (et
  parfois un `riot`) ; à 4 il ajoute le SWAT (`fbi2`, américain).
- Jamais à la baisse : 5 étoiles gagnées en jouant restent 5 étoiles.
- Pas pendant une mission (`GET_MISSION_FLAG`), ni joueur mort, en
  chargement ou en cinématique : l'étoile est journalisée et ignorée.

### 2. Police sans armes à feu (`TIRS=0`)

Toutes les 0,5 s, tout ped de police (`GET_PED_TYPE` 6 cop, 27 SWAT, 29
armée) à moins de 300 m du joueur qui porte une arme à feu ou un explosif
perd toutes ses armes. Les renforts que le jeu fait apparaître n'ont pas le
temps de tirer : la police percute, fait des PIT, bloque, et arrête à pied.

### 3. Pas d'hélicoptères (`HELICOS=0`)

`ENABLE_DISPATCH_SERVICE` coupe DT_PoliceHelicopter (2) et DT_SwatHelicopter
(12), revérifié toutes les 5 s, rallumé à l'arrêt du script.

### 4. La tenue (`TENUE_S=45`)

Sans hélicos, semer la police est plus facile que dans le jeu normal. Pendant
45 s après chaque cadeau : les étoiles ne descendent pas sous la cible, et dès
qu'elles grisent la police « revoit » le joueur (`REPORT_POLICE_SPOTTED_PLAYER`).
Ensuite, règles du jeu. Mort ou arrestation : le jeu efface les étoiles, la
tenue s'arrête.

## Tests

`plugin.test.js` (harnais C#) : lecture du mode (casse, accents, inconnu →
étoiles), étoiles visées (+1, plafond, cible en cours, jamais à la baisse),
types de ped de police. Compilation contre le vrai `ScriptHookVDotNet3.dll`.
Le comportement se vérifie en jeu (« Tester sans live », rafale de 3).

## Hors périmètre

Retoucher `dispatch.meta` (`police3` américain pour `sheriff2`, `riot` en
barrage, SWAT `fbi2`) : possible plus tard si des véhicules non français
dérangent. Étoiles selon la valeur du cadeau.
