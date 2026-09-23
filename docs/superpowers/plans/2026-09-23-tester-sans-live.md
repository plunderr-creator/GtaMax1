# Tester sans live — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal :** Tester voix, commandes, alerte et police en jeu sans être en live TikTok.

**Architecture :** Trames TikTok fabriquées (`app/simulation.js` : `trameChat`, `trameDon`, `creerSimulation`) données aux vrais handlers de `evenements.js` avec `{ test: true }` (roster intact). Routes `/api/test/*` + carte « 🧪 Tester sans live » dans `setup.html`. Spec : `docs/superpowers/specs/2026-09-23-tester-sans-live-design.md`.

**Tech stack :** Node 22 (`node --test`), vanilla JS.

## Global Constraints

- `{ test: true }` ne touche jamais `data.json`.
- Live simulé : 10 min max, chat 5-10 s, cadeau 20-40 s, coupé et refusé pendant un vrai live.
- Bornes : pseudo ≤ 30 sans espace, message 1-300, cadeau ≤ 50, combo 1-99, rafale 1-10, délai 0-60 s.
- Journal, interface, commentaires en français. Tests : `cd app && npm test`.

---

### Task 1 : Drapeau `{ test: true }` dans les handlers

**Files:** Modify `app/evenements.js` (onChat, onGift ; `simulerDon` retiré) · Test `app/test/evenements.test.js`

**Interfaces:** Produces `onChat(data, opts)`, `onGift(data, opts)` — `opts.test` : pas de roster, journal préfixé 🧪.

- [x] Step 1 : tests — chat de test lu + roster vide ; `!aide` de test répond ; cadeau de test → alerte + merci + `SPAWN_POLICE`, roster vide.
- [x] Step 2 : FAIL (roster rempli).
- [x] Step 3 : `const test = !!(opts && opts.test)` ; `toucher`, `ajouterDon`, `sauver` sous `if (!test)`.
- [x] Step 4 : PASS. Step 5 : commit `feat(test): drapeau test dans les handlers, roster intact`.

### Task 2 : Live simulé

**Files:** Create `app/simulation.js` · Test `app/test/simulation.test.js`

**Interfaces:** Produces `trameChat(pseudo, message, nom?)`, `trameDon(pseudo, cadeau, nombre, nom?, pieces?)`, `PIECES_CADEAUX`, `creerSimulation({ evenements, maintenant, planifier, annuler, hasard, log, dureeMaxMs }) → { demarrer() → bool, arreter(raison) → bool, etat() → { actif, resteMs } }`.

- [x] Step 1 : tests avec horloge factice (voir spec § Tests).
- [x] Step 2 : FAIL (module absent).
- [x] Step 3 : boucles `tourChat` (5-10 s) / `tourDon` (20-40 s) replanifiées tant que `actif`, minuteries suivies pour `arreter`.
- [x] Step 4 : PASS. Step 5 : commit `feat(test): live simule (faux viewers, 10 min max)`.

### Task 3 : Routes et panneau setup

**Files:** Modify `app/server.js`, `app/setup.html`, `README.md` · Test `app/test/serveur.test.js`

**Interfaces:** Consumes Tasks 1-2. Produces `POST /api/test/chat`, `POST /api/test/don`, `POST|GET /api/test/live`, `GET /api/overlay/status`.

- [x] Step 1 : tests serveur (voir spec § Tests).
- [x] Step 2 : FAIL (404).
- [x] Step 3 : routes (bornes, délai par `setTimeout`), arrêt du live simulé dans `setTikTokStatus(true)`, carte setup, overlays comptés dans « État ».
- [x] Step 4 : PASS + vérif visuelle (navigateur intégré).
- [x] Step 5 : commit `feat(test): panneau Tester sans live dans le setup`.
