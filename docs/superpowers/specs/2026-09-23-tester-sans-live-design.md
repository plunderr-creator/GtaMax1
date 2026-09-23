# Tester sans live — design

Date : 2026-09-23 · Branche : main

## Problème

TikTok n'envoie rien tant qu'on n'est pas en live : sans live, impossible
d'entendre la voix lire le chat, de voir une alerte ou de faire venir la
police en jeu. Seul « Tester un don » existe, avec un donateur et un cadeau
fixes.

## Décisions

### 1. Les tests passent par les VRAIS handlers

Messages et cadeaux de test sont des trames fabriquées (même forme que
tiktok-live-connector) données à `onChat` / `onGift` avec `{ test: true }` :
voix, parseur de commandes « ! », alerte, merci vocal et `SPAWN_POLICE`
suivent exactement le chemin d'un live. Seule différence : `{ test: true }`
ne touche **jamais** au roster (pas de faux viewer dans la table du futur
gameplay) et préfixe le journal par 🧪.

`simulerDon()` disparaît : « Tester un don » envoie un cadeau de test par
défaut (testeur, Rose ×1) par le même chemin.

### 2. Panneau « 🧪 Tester sans live » dans le setup

- **Pseudo** (défaut `testeur`) et **délai** : tout de suite, 5 s, 10 s, 30 s
  — le délai laisse le temps de revenir sur GTA avant que la voiture arrive.
- **Message du chat** + « Envoyer » (Entrée marche) : lu par la voix, ou
  traité comme commande s'il commence par « ! » (`!aide` répond).
- **Cadeau** (Rose, Perfume, Hand Hearts, Galaxy, TikTok Universe), **combo**
  (×1 à ×99, comme une série de roses) et **rafale** (1 à 10 cadeaux d'un coup,
  pour voir l'espacement de 3 s et le plafond de 8 voitures) + « Envoyer ».
- **Live simulé** (bouton marche/arrêt) : des faux viewers écrivent toutes
  les 5-10 s (dont un `!aide` de temps en temps) et offrent un cadeau toutes
  les 20-40 s. Arrêt automatique au bout de 10 min. **Coupé dès que le vrai
  live TikTok se connecte**, et refusé pendant un vrai live : jamais de faux
  cadeaux mélangés à un vrai live. Les tests manuels, eux, restent possibles
  en live (action explicite du streamer).
- Rappel dans la carte : l'overlay doit être ouvert (OBS ou « Voir
  l'overlay ») pour entendre la voix et voir l'alerte. La carte « État »
  affiche le nombre d'overlays connectés.

### 3. API (POST locales uniquement, comme les autres)

- `POST /api/test/chat { pseudo?, message, delai_s? }`
- `POST /api/test/don { pseudo?, cadeau?, nombre?, rafale?, delai_s? }`
- `POST /api/test/live { actif }` · `GET /api/test/live` → `{ actif, resteMs }`
- `GET /api/overlay/status` → `{ connectes }`

Bornes : pseudo ≤ 30 caractères sans espace, message 1-300, cadeau ≤ 50,
combo 1-99, rafale 1-10, délai 0-60 s. Valeur hors bornes = ramenée dans les
bornes, message vide = 400.

## Tests

- `evenements.test.js` : `{ test: true }` → mêmes effets (voix, alerte,
  `SPAWN_POLICE`, commande), roster vide.
- `simulation.test.js` (horloge factice) : chat puis cadeau au démarrage,
  cadences, tout en `{ test: true }`, arrêt propre (plus aucune minuterie),
  arrêt auto à la durée max, double démarrage refusé, roster vide avec les
  vrais handlers.
- `serveur.test.js` : chat de test journalisé, rafale de 3 → 3 alertes et 3
  `SPAWN_POLICE`, délai respecté, message vide refusé, live simulé
  marche/arrêt, overlays comptés.

## Hors périmètre

Raccourci clavier en jeu, contrôle depuis le téléphone (les POST restent
locaux), likes/follows/partages simulés (ils ne font que journaliser en v1).
