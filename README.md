# GTA Max

Overlay TikTok Live pour GTA V : la voix lit le chat, et chaque cadeau fait
apparaître devant toi une voiture de police qui te prend en chasse, avec une
alerte à l'écran.

Plomberie reprise de Final Max v0.3.0 (connexion TikTok, voix Edge, overlay,
réglages, persistance). Spec et plan : `docs/superpowers/`.

## Pendant un live

1. Lancer **`GTA Max.bat`** — la page de réglages s'ouvre (`http://localhost:3200/setup`).
   Ne pas fermer la fenêtre noire.
2. Lancer GTA V **par Steam**.
3. Dans OBS, la source navigateur de l'overlay (voir plus bas) est déjà là.

## Première fois

**Réglages** (page `/setup`) : pseudo TikTok, code `sessionid` (étapes dans la
page), clé EulerStream (recommandée), voix, dossier GTA V. Les secrets vont dans
`app\.env`, jamais dans `config.json`.
Raccourci : copier `app\.env` de Final Max dans `app\` reprend son `SESSION_ID`
et sa clé EulerStream.

**OBS** : Source navigateur `http://localhost:3200/overlay`, **1920 × 1080**,
cocher **« Contrôler l'audio via OBS »** (la voix sort dans la source).

**Côté jeu** (déjà fait le 2026-09-23) :
- ScriptHookVDotNet v3 **nightly** (v3.7.0-nightly.190, obligatoire pour le jeu
  en build 3889) à la racine du jeu. Console SHVDN sur **F8** (F4 = TrainerV).
- **`Installer plugin GTA.bat`**, GTA V fermé : compile `scripts\GTAMax.cs` en
  `<GTA V>\scripts\GTAMax.dll` et dépose `GTAMax.ini`. À relancer après toute
  modification de `GTAMax.cs` (le setup dit « pas à jour » sinon).

**Vérifier** : bouton **« Tester un don »** du setup → alerte sur l'overlay,
merci vocal, voiture de police en jeu (roster non touché).

## Tester sans live

TikTok n'envoie rien hors live. Carte **« 🧪 Tester sans live »** du setup :

- **Message du chat** (pseudo au choix) : lu par la voix, ou commande s'il
  commence par « ! » (`!aide` répond).
- **Cadeau** : nom, combo (×1-99), rafale (1-10 d'un coup : on voit l'espacement
  de 3 s et le plafond de 8 voitures en jeu).
- **Envoyer dans 5/10/30 s** : le temps de revenir sur GTA avant que ça parte.
- **Live simulé** : de faux viewers écrivent toutes les 5-10 s et offrent un
  cadeau toutes les 20-40 s, 10 min au plus. Coupé tout seul (et refusé) si le
  vrai live TikTok est connecté.

Même chemin qu'en live (voix, commandes, alerte, `SPAWN_POLICE`), mais **rien
dans le roster**. L'overlay doit être ouvert (OBS ou « Voir l'overlay ») : la
carte « État » dit combien il y en a.

## Réglages du plugin

`<GTA V>\scripts\GTAMax.ini` (jamais écrasé par une réinstallation) :
modèles tirés au sort (`police, police2, police3, sheriff` — **`policet` retiré** :
le fourgon CRS installé à sa place fait planter le jeu en poursuite), distance
(20 m), une voiture toutes les 3 s, 8 voitures max, 20 demandes en attente max.
Journal du plugin : `<GTA V>\scripts\GTAMax.log`. Journal du serveur :
`app\logs\gtamax.log` (ou « Télécharger les logs » dans le setup).

## Ajouter une action en jeu

1. `scripts/GTAMax.cs` : écrire `void MonAction(Commande c)` (arguments :
   `c.Arg("cle")`), puis dans le constructeur
   `registre.Enregistrer("MON_ACTION", MonAction);`.
2. Côté serveur, l'appeler par le seul pont : `envoyerCommande('MON_ACTION', { cle: 'valeur' })`
   (`app/gta-bridge.js`). Verbe `[A-Z0-9_]`, clés `[a-z0-9_]`.
3. `Installer plugin GTA.bat`, relancer le jeu.

Un verbe inconnu du plugin est journalisé et ignoré. Une commande de plus de
60 s n'est jamais jouée.

## Ajouter une commande chat

1. `app/commandes-chat.js`, registre `COMMANDES` :

   ```js
   police: {
     recharge_ms: 60000,   // optionnel : anti-spam global
     executer(cmd, ctx) {  // cmd = { commande, args, pseudo }
       ctx.envoyerCommande('SPAWN_POLICE', { donateur: cmd.pseudo, cadeau: '!police' });
       ctx.repondre('Police envoyée par ' + cmd.pseudo);
     },
   },
   ```

2. `ctx` : `repondre(texte)` (voix), `envoyerCommande(verbe, args)` (jeu),
   `viewers()` + `sauver()` (roster).
3. Un test dans `app/test/commandes-chat.test.js`.

Les messages « ! » ne sont jamais lus par la voix. Les autres événements
(`onLike`, `onFollow`, `onShare`) sont dans `app/evenements.js`, prêts à agir.

## Roster

`app/data.json` : `{ "viewers": { "pseudo": { premiere_vue, derniere_vue, dons_total } } }`
— horodatages en ms, `dons_total` en pièces TikTok. La clé `viewers` laisse la
place à l'état d'un futur gameplay à côté.

## Tests

```bash
cd app && node/node.exe --test "test/**/*.test.js"
```

Couvre la file TTS, le pont (écriture atomique, péremption), les commandes
chat, les événements, la persistance, le serveur en bac à sable, et le plugin
C# (logique pure compilée par `csc`, protocole Node → C#, compilation contre le
vrai `ScriptHookVDotNet3.dll`). Le comportement en jeu se vérifie avec
« Tester un don ».

## Retour arrière côté jeu

Tout est additif : supprimer à la racine du jeu `ScriptHookVDotNet.asi`,
`ScriptHookVDotNet.ini`, `ScriptHookVDotNet2.dll/.pdb`, `ScriptHookVDotNet3.dll/.pdb`,
`ScriptHookVDotNet.pdb`, et le dossier `scripts\`. Aucun `.rpf`, rien dans `mods\`.

## Dépendances

Versions exactes + `overrides` (voir `app/package.json`). Ne jamais lancer
`npm audit fix` : il rétrograde `tiktok-live-connector` et casse le protocole TikTok.
