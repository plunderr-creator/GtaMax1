# GTA Max

TikTok Live → voix du chat, alertes sur l'overlay et poursuites dans GTA V.
Un cadeau terminé ou un palier de 250 likes ajoute une voiture de police.
Les noms des donateurs s'affichent au-dessus des voitures.

## Lancer

1. Ouvrir **GTA Max.bat**, puis configurer le compte TikTok, la voix et le dossier du jeu dans http://localhost:3200/setup.
2. Lancer GTA V par Steam.
3. Dans OBS, ajouter http://localhost:3200/overlay comme source navigateur en 1080 × 1920 et cocher « Contrôler l'audio via OBS ».

Les secrets restent dans app/.env ; les réglages de l'application sont dans app/config.json.
Les commandes viewer commencent par « !ok » (ex. `!ok garage`, `!ok barrage`). Les messages commençant par « ! » ne sont jamais lus par la voix.

## Points, niveaux et garage

Le premier contact de chaque voiture de viewer avec le joueur rapporte **1 point**.
Une même voiture ne peut pas marquer plusieurs fois. Les points déjà gagnés sont repris.
Les niveaux 1 à 5 correspondent à **0, 5, 15, 30 et 60 points cumulés**.
Les achats diminuent le solde disponible, jamais le total, le niveau ou le classement.

L'overlay OBS affiche les cinq premiers viewers, leur niveau, leur total et leur solde.
Les gains et nouveaux niveaux apparaissent dans un bandeau. Il reprend les scores à
chaque connexion sans rejouer les animations anciennes. En cas de lecture interrompue,
il garde les derniers scores et indique qu'il attend le jeu.

Commandes du chat (un appel toutes les trois secondes par viewer) :

| Commande | Action |
|---|---|
| `!ok garage`, `!ok points`, `!ok niveau` | Afficher son profil dans le bandeau OBS |
| `!ok lvlup` | Monter sa voiture d'un rang : moteur (+8 puissance/vitesse), transmission (+0,08 couple) et freins GTA d'un coup |
| `!ok voitures` | Afficher les modèles et niveaux requis |
| `!ok voiture rs3` | Choisir un modèle débloqué pour sa prochaine apparition |
| `!ok voiture municipale` | Revenir à la Police Municipale (modèles de `[POLICE] MODELES` dans l'INI) |
| `!ok acheter boost` | Acheter un boost (3 points, niveau 1) : il part en stock, affiché 🚀 sur sa carte de l'overlay (max 9) |
| `!ok acheter barrage` | Acheter un barrage (5 points, niveau 2) : en stock, affiché 🚧 sur sa carte (max 9) |
| `!ok boost` | Lancer un boost du stock : ses voitures en poursuite +30 vitesse, +0,6 couple pendant 15 s (🚀 qui pulse). Rien n'est utilisé s'il n'a pas de voiture en poursuite |
| `!ok barrage` | Lancer un barrage du stock : 3 voitures de police devant le joueur d'un coup (🚧 qui pulse 20 s), lance la poursuite si besoin |
| `!ok` ou `!ok aide` | Afficher les commandes et tarifs |

`!ok lvlup` a trois rangs : **3 / 8 / 15 points**, aux niveaux **2 / 3 / 4**. Chaque rang améliore les trois pièces ensemble.
Les améliorations sont attachées au viewer et suivent ses voitures. Les freins GTA
s'appliquent dans la limite des modifications disponibles sur chaque modèle.
Moteur et transmission restent bornés.

Catalogue (seuils 0 / 5 / 15 / 30 / 60 / 100 points cumulés) : `municipale` (INI, `pranger`) niveau 1,
`police` (police GTA) niveau 2, `golf` (`mk7`) niveau 3, `rs3` (`cmstrs3`) niveau 4, `m5` (`bmci`) niveau 5,
`scooter` (`mbkstunt`) niveau 6. La sélection est gratuite après déblocage. Le modèle choisi doit exister
dans le jeu ; il ne remplace pas une voiture déjà en poursuite. Un modèle sauvegardé inconnu revient à `municipale`.

Le jeu reste l'unique source des points et des achats. Il sauvegarde dans
`scripts/GTAMax.points` et transmet ses réponses via `GTAMax.points.messages`.
Le serveur et l'overlay lisent ces données sans les modifier. Les achats ne sont
confirmés qu'après sauvegarde ; un fichier illisible suspend la progression pour
éviter de l'écraser. Les commandes nécessitent GTA V et le plugin en fonctionnement.

## Poursuite

Le plugin utilise uniquement le mode survie : jusqu'à 20 voitures, un conducteur par voiture.
Toutes les voitures poursuivent directement le joueur et cherchent le contact. Elles visent
un point proche de sa trajectoire, roulent plus vite pour le rattraper et ralentissent dans
les virages serrés. Elles apparaissent sur une route derrière lui lorsque c'est possible.

Un cadeau prolonge la poursuite de 120 secondes, même au plafond de voitures.
Un policier venu au contact puis suffisamment loin et hors de vue pendant 5 secondes abandonne.
La poursuite se termine lorsque tous sont semés, à l'expiration du délai, à la mort ou à la capture.
Le meilleur temps reste enregistré dans scripts/GTAMax.record, dans le dossier du jeu.

La police normale du jeu est suspendue pendant la poursuite. Les étoiles affichent sa pression.
Le comportement normal du jeu est rétabli à la fin ou au rechargement du plugin.

## Réglages du plugin

Modifier scripts/GTAMax.ini **dans le dossier de GTA V**, puis F8 → Reload().

| Section | Réglage | Valeur actuelle |
|---|---|---|
| SURVIE | MAX_FLICS | 20 |
| SURVIE | DUREE_S | 120 |
| SURVIE | DISTANCE_M | 60 |
| SURVIE | INDESTRUCTIBLES | 1 |
| POLICE | MODELES | pranger |
| POLICE | PED | s_m_y_sheriff_01 |

MODELES accepte plusieurs véhicules séparés par des virgules, utilisés à tour de rôle.
INDESTRUCTIBLES=0 permet de détruire les véhicules et les conducteurs.

## Tester et installer

La page de réglages permet de tester un message, un don, une rafale ou un live simulé sans compte connecté.
La simulation s'arrête à la connexion d'un vrai live. L'overlay doit être ouvert pour voir les alertes et entendre la voix.

Après une modification du plugin, lancer **Installer plugin GTA.bat**, puis F8 → Reload() si le jeu tourne.
L'installateur compile les deux fichiers C# et conserve le fichier INI déjà installé.

Depuis app, lancer node/node.exe --test "test/**/*.test.js" pour vérifier les événements,
la voix, le serveur, le pont vers GTA, la simulation et les règles de poursuite.
Les tests du plugin incluent sa compilation contre ScriptHookVDotNet3.dll lorsque le jeu est configuré.

Les erreurs sont dans app/logs/gtamax.log et dans scripts/GTAMax.log côté jeu.
Les documents de docs/superpowers décrivent les anciennes étapes de conception.

### Bandeau TikTok vertical

Source navigateur OBS : **1080 × 1920**, placée sur tout le canevas vertical, sans étirer ni recadrer. Le bandeau reste dans les 440 premiers pixels (moins du quart de l’écran) ; le reste est transparent. Les trois premiers scores sont mis en avant, les places 4 et 5 restent visibles dessous. Les gains, niveaux, cadeaux et réponses du garage partagent une seule ligne d’animation ; les longues réponses passent en plusieurs pages.
