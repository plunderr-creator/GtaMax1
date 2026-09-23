# GTA Max v1 — voix du chat + police à chaque don — design

Date : 2026-09-23 · Branche : main

## Problème

Pendant un live TikTok sur GTA V, le streamer veut deux choses, sans rien
toucher pendant le live :

1. que les messages du chat soient lus à voix haute, dans OBS ;
2. qu'à chaque cadeau, une voiture de police apparaisse devant lui et le
   prenne en chasse, avec une alerte à l'écran qui nomme le donateur.

Un gameplay pour les viewers (commandes, économie, progression) viendra
plus tard. Son contenu n'est pas décidé : la v1 n'en code rien, mais elle
laisse les coutures pour qu'il se greffe sans réécriture.

## Décisions

### 1. Le squelette vient de Final Max, pas de zéro

Final Max v0.3.0 tourne en live réel ; sa plomberie est reprise telle quelle,
commentaires compris :

| Repris | D'où |
|---|---|
| Connexion `TikTokLiveConnection` v2.4.0 (`sessionId` + `ttTargetIdc` + `signApiKey`), backoff 10 → 30 s (60 s si rate limit), session expirée détectée et jamais martelée | `server.js` « CONNEXION TIKTOK » |
| IPv4 forcé sur toutes les connexions sortantes | `server.js` en-tête |
| `tts.js` (client Edge « Read Aloud », Sec-MS-GEC) | verbatim |
| File TTS serveur : plafond 4, saut au-delà de 7 s d'attente, cache mp3 FIFO de 20, `GET /api/tts/<id>` ; lecture overlay `new Audio(...)` en série | `server.js` « TTS », `overlay.js` « TTS » |
| HTTP + WebSocket, `broadcast()`, ping 5 s, reconnexion auto du client, rechargement de l'overlay quand son code change | `server.js`, `overlay.js` |
| Gardes : anti DNS-rebinding (Host), mutations locales seulement (`ALLOW_LAN_CONTROL`), statiques en liste blanche, code serveur jamais servi | `server.js` |
| `config.default.json` → `config.json` au premier lancement, lecture anti-BOM ; secrets dans `.env` (`SESSION_ID`, `SIGN_API_KEY`) | `server.js` « CONFIG » |
| Journal fichier `app/logs/gtamax.log` (rotation 2 Mo), `GET /api/logs` | `server.js` « LOGS » |
| `data.json` : écriture atomique, sauvegarde débouncée 500 ms, copie de sûreté `data.backup.json` toutes les 60 s, relecture de secours, fichier corrompu conservé, flush à la fermeture (y compris la croix de la fenêtre) | `server.js` « PERSISTANCE », `lib.js` |
| Lanceur `.bat` + `app\node\node.exe` portable (v22.12.0) + `node_modules` embarqué | `Final Max.bat` |
| Dépendances en versions exactes + les cinq `overrides` | `package.json` |
| `node --test`, serveur lancé pour de vrai dans un bac à sable | `test/` |

Jeté : boss, items, lootbox, duels, familiers, présage, raid, compétences,
cerveau/miroir, licence, mode démo, banque de sons, `game-data.js` et tous
les réglages `gameplay`.

Port par défaut **3200** (Final Max garde 3100 : les deux peuvent tourner
côte à côte sans `EADDRINUSE`).

### 2. Pont vers le jeu — un fichier, deux propriétaires

`app/gta-bridge.js` écrit dans `<dossier GTA V>\scripts\gtamax_queue.txt`.
Le dossier du jeu est réglable dans le setup (`config.gta.dossier_jeu`), le
nom du fichier est fixe (le plugin le cherche à côté de lui).

**Format** : une commande par ligne, `VERBE t=<ms epoch> clé=valeur …`.
Verbe `[A-Z][A-Z0-9_]*`, clés `[a-z][a-z0-9_]*`, valeurs encodées façon URL
(`encodeURIComponent`) : un nom de cadeau avec espaces ou un pseudo exotique
ne casse jamais le découpage. `t` est posé par le pont sur chaque ligne.

**Protocole** (aucune course possible, aucun doublon) :
- seul Node **crée** le fichier, et seulement quand il n'existe pas : il
  écrit `gtamax_queue.txt.tmp` en entier puis le renomme ;
- seul le plugin le **consomme** : il le renomme en `gtamax_queue.en-cours`
  (rename atomique ; fichier verrouillé = on réessaie au tick suivant),
  le lit, le supprime, puis exécute ;
- tant que le fichier précédent n'est pas consommé, Node garde les
  commandes en mémoire et réessaie toutes les 150 ms — une rafale de dons
  part donc en un seul fichier.

**Pas de rattrapage** (même règle que le TTS) : une commande de plus de
60 s est jetée, côté Node (jeu fermé) comme côté plugin (fichier resté d'une
session précédente). Si la file n'est pas consommée depuis 10 s, Node le dit
une fois dans le journal (« GTA V lancé avec le plugin ? »).

**Point d'entrée unique** : `envoyerCommande(verbe, args)`. Rien d'autre
n'écrit dans la file. En v1, un seul appelant : le don → `SPAWN_POLICE`.

### 3. Plugin jeu — `scripts/GTAMax.cs` (ScriptHookVDotNet v3)

Compilé en `GTAMax.dll` avec le `csc` du .NET Framework 4.8 contre
`ScriptHookVDotNet3.dll` (C# 5 : pas d'interpolation `$""`, pas de `?.`).
Installé par `Installer plugin GTA.bat`, qui dépose aussi `GTAMax.ini` s'il
n'existe pas encore. Le serveur signale au démarrage un plugin absent ou plus
vieux que sa source.

- `Interval = 100` : chaque tick lit la file (`.en-cours` d'abord s'il en
  reste un), parse, jette les commandes périmées, exécute.
- **Registre d'actions** `Dictionary<string, Action<Commande>>`. Verbe
  inconnu = une ligne de journal, rien d'autre. Un handler qui lève est
  attrapé et journalisé.
- **Try/catch sur chaque étape du tick** : une exception non attrapée ferait
  décharger le script par SHVDN. Serveur éteint = pas de fichier = rien ne
  se passe, le jeu tourne normalement.
- Journal propre : `scripts\GTAMax.log` (rotation 512 Ko).

**`SPAWN_POLICE`** (args : `donateur`, `cadeau`, pour le journal) :
- File interne : 1 apparition toutes les 3 s, même si dix dons tombent
  ensemble ; 20 demandes max en attente ; une demande de plus de 60 s
  est jetée (joueur mort, cinématique, chargement : on attend, puis on ne
  rattrape pas).
- Modèle tiré au sort dans `GTAMax.ini` (`[POLICE] MODELES`), par défaut
  **`pranger, policet`** (révisé après le premier test en jeu, voir
  « Retour du test en jeu » en bas). Un modèle absent du jeu est journalisé
  et ignoré.
- Position : 20 m devant le joueur (+ 1 s de trajet s'il roule, plafonné à
  60 m, pour ne pas le faire apparaître dans son pare-chocs), recalée sur la
  route la plus proche ; repli sur le point au sol s'il n'y a pas de route à
  moins de 60 m. Orientée vers le joueur.
- Chargement des modèles **sans bloquer** : demande au tick N, création
  quand ils sont chargés, abandon après 5 s.
- Un seul `s_m_y_cop_01` (tenue Police Nationale), au volant. Sirène + gyrophares.
- **ELS** : si `ELS.asi` est à la racine du jeu **et** qu'un VCF
  `ELS\pack_default\<modèle>.xml` existe, on active la sirène et on laisse
  ELS gérer (on ne touche pas au mute) ; sinon, feux vanilla : sirène active
  et son forcé. Aujourd'hui ELS est désactivé (`_test_off\`) : c'est le cas
  vanilla qui joue, et ça bascule tout seul si ELS revient.
- **Percuter** : en voiture comme à pied, le conducteur a l'ordre de percuter
  le joueur (`TASK_VEHICLE_MISSION_PED_TARGET`, mission RAM, style de conduite
  qui ne contourne pas le joueur), réémis toutes les 4 s. Conducteur « garde
  ta tâche » + événements bloqués (ne fuit pas, ne descend pas se battre) +
  ne traverse pas le pare-brise au choc.
- **Parc** : 8 voitures vivantes max ; la 9e fait supprimer la plus ancienne
  (sauf si le joueur est dedans : elle est alors seulement lâchée).
- **Nettoyage** : voiture et policier restent persistants TANT QU'ILS
  POURSUIVENT ; distancés à plus de 250 m pendant 15 s, ils sont lâchés (le
  jeu les efface quand il veut) ; une épave est supprimée 8 s après sa mort ;
  mort ou arrestation du joueur = fin de poursuite, tout est lâché. Un script
  déchargé (`Reload()` dans la console, fermeture) lâche tout ce qu'il suivait.

### 4. Alerte don — seul élément visuel

`overlay.html` / `overlay.css` / `overlay.js`, 1920×1080, fond transparent.
Message WS `{ type:'alerte_don', pseudo, cadeau, nombre }`. Bandeau central
haut : « 🚨 ALERTE », le pseudo en grand, « envoie <cadeau> ×n — la police
arrive ! », barres de gyrophare bleu/rouge qui clignotent de part et d'autre,
lueur bleu/rouge sur les bords de l'écran. Entrée en fondu + zoom, sortie en
fondu, affiché ~5 s. Plusieurs dons = file (50 max), une alerte après
l'autre. Rien d'autre à l'écran : pas de fil de chat, pas de compteur, pas
de file visible, pas de badge.

### 5. Coutures pour le gameplay futur (rien de plus)

1. `app/evenements.js` : `onChat`, `onGift`, `onLike`, `onFollow`,
   `onShare`, fonctions nommées branchées sur la connexion TikTok. Toutes
   journalisent et tiennent le roster ; seules `chat` et `gift` agissent.
   Les likes sont agrégés par viewer et journalisés toutes les 5 s.
2. `app/commandes-chat.js` : un message qui commence par `!` est parsé en
   `{ commande, args, pseudo }` et routé vers le registre `COMMANDES`
   (`commande → handler(cmd, ctx)`). En v1 : `!aide` seul, qui répond par la
   voix « aucune commande n'est encore disponible » (une fois toutes les
   30 s au plus). Commande inconnue = journal. Un message `!` n'est jamais lu
   par le TTS.
3. Roster : `data.json` = `{ "viewers": { pseudo_minuscule: { premiere_vue,
   derniere_vue, dons_total } } }`. `premiere_vue`/`derniere_vue` en ms
   epoch, `dons_total` = **cumul en pièces TikTok** (diamants × répétitions),
   pas un nombre de cadeaux. Tout événement met `derniere_vue` à jour. La clé
   `viewers` laisse la place, à côté, à l'état d'un futur gameplay.
4. `envoyerCommande()` côté Node et le registre d'actions côté C# sont les
   deux seuls points de couplage serveur ↔ jeu.
5. README : « ajouter une action en jeu », « ajouter une commande chat ».

### 6. Réglages

`config.json` : `tiktok.username`, `tiktok.region`, `server.port`,
`tts.{enabled, voice, volume, read_username}`, `gta.dossier_jeu`.
`.env` : `SESSION_ID`, `SIGN_API_KEY` (jamais renvoyés par l'API).
`setup.html` (une page) : compte TikTok + code de session + clé
EulerStream, voix (activée, voix, volume, lire le pseudo, tester), GTA V
(dossier, état SHVDN/plugin/file, « tester un don »), port (redémarrage
requis), URL de la source OBS.

Le don garde le merci vocal de Final Max (« Merci X pour Rose ! ») : le
donateur veut être entendu, et ça passe par la même file de 4.

## Tests

`cd app && npm test` (`node --test`) :
- `file-tts.test.js` : plafond 4, saut > 7 s, cache FIFO 20, voix coupée,
  test forcé, nettoyage émojis/répétitions.
- `gta-bridge.test.js` : format de ligne et encodage, écriture atomique
  (jamais de `.tmp` visible sous le nom final, fichier existant jamais
  écrasé), mise en attente puis envoi groupé, péremption 60 s.
- `commandes-chat.test.js` : parseur (casse, arguments, `!` seul, message
  normal), routage, `!aide` et son anti-spam.
- `evenements.test.js` : chat → TTS ou commande, don → alerte + merci +
  `SPAWN_POLICE` + roster, séries de dons comptées une fois, doublons de
  msgId, follow dédoublonné.
- `persistance.test.js` : roster, copie de sûreté périodique, relecture de
  secours.
- `plugin.test.js` : compile `GTAMax.cs` avec `/define:TESTS_SANS_JEU` (la
  partie qui dépend du jeu est exclue) et un harnais C# : parseur de ligne,
  péremption, registre (verbe inconnu, handler qui lève), file de spawn
  (3 s, plafond, âge max), parc (8, éviction de la plus ancienne).
- `serveur.test.js` : serveur réel en bac à sable — pages, gardes, secrets
  jamais renvoyés, « tester un don » → `alerte_don` en WS + `SPAWN_POLICE`
  dans la file.

Non testable ici : le comportement en jeu (IA, apparition, ELS). Compilation
vérifiée contre le vrai `ScriptHookVDotNet3.dll` ; le reste se valide en jeu
avec le bouton « Tester un don ».

## Hors périmètre

Toute mécanique de viewer (économie, progression, commandes autres que
`!aide`), fil de chat ou compteur à l'écran, sons d'alerte, image du cadeau,
archivage du roster, réponse écrite dans le chat TikTok, modification des
`.rpf` ou du dossier `mods\`.

## Retour du test en jeu (2026-09-23)

Premier test : ça marche (file, 3 s d'écart, apparition, gyrophares vanilla),
mais trois écarts avec ce que veut le streamer :

- **Voitures américaines.** Scan des archives : seuls `pranger` (voiture
  Police Nationale) et `policet` (fourgon CRS) sont remplacés par des modèles
  français. `police`, `police2`, `police4`, `sheriff`, `sheriff2`, `riot`…
  sont vanilla, et le modèle français posé sur `police3` est écrasé par
  `patchday3ng`/`patchday4ng`. → `MODELES=pranger,policet`, 50/50.
  ⚠️ `policet` a déjà fait planter le jeu pendant une poursuite le
  2026-09-22 (piste : LOD inversés, `policet.yft` 6,9 Mo > `policet_hi.yft`
  3,5 Mo). Demandé explicitement : gardé, avec l'avertissement dans
  `GTAMax.ini`.
- **Pas de percussion.** → mission RAM réémise toutes les 4 s (voir « Percuter »).
- **Deux policiers par voiture.** → un seul, au volant.

Constaté dans `GTAMax.log` : marquées non persistantes dès l'apparition, les
voitures étaient effacées par le jeu 15 à 80 s plus tard (hors champ derrière
le joueur). → persistantes tant qu'elles poursuivent, lâchées à 250 m / 15 s.
