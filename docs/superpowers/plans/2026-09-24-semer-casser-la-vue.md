# Semer = casser la vue — plan d'implémentation

> **Non exécuté tel quel** : le streamer l'a jugé trop lourd. Une version réduite de 90 % a été faite à la place (voir la note « Fait » en fin de section de la spec).

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Avec la police du jeu (`POLICE_DU_JEU=1`), on ne sème plus la poursuite en distançant un flic mais en cassant la vue, comme avec les étoiles de GTA.

**Architecture:** Toute la logique vit dans `scripts/Survie.cs`. En haut du fichier, les règles pures (`ReglesSurvie`) sont testées sans le jeu par `app/test/csharp/TestsPlugin.cs`. En bas, la classe `Survie` joue ces règles en jeu. Pour savoir si le joueur est vu, perdu de vue ou semé, on s'appuie sur le système d'étoiles du jeu : `REPORT_POLICE_SPOTTED_PLAYER`, `ARE_PLAYER_STARS_GREYED_OUT`, `GET_PLAYER_WANTED_CENTRE_POSITION`.

**Tech Stack:** C# 5 (csc du .NET Framework 4.8), ScriptHookVDotNet v3 nightly 3.7.0-190, natives via `Function.Call(Hash.…)`, tests `node --test` + harnais C#.

**Spec :** `docs/superpowers/specs/2026-09-23-mode-survie-design.md`, section « Semer = casser la vue (2026-09-24) ».

## Global Constraints

- C# 5 seulement : pas de `$""`, pas de `?.`, pas de `nameof`.
- Natives par `Function.Call(Hash.NOM, …)` (API SHVDN récentes marquées obsolètes).
- `POLICE_DU_JEU=0` : comportement strictement inchangé (un flic semé abandonne).
- Jamais `TASK_VEHICLE_TEMP_ACTION` 16 à 18 (plantent le jeu).
- Commentaires en français, dans le style du fichier (référence datée : « casser la vue », 2026-09-24).
- Tests : depuis `app/`, `node/node.exe --test test/plugin.test.js` (195 au départ). Le dernier test compile le plugin complet contre le vrai `ScriptHookVDotNet3.dll`, ce qui attrape un nom de native faux.
- Pas de commit sauf si Elliot le demande (le travail d'hier dans les mêmes fichiers n'est pas commité non plus).

---

### Task 1 : « SEMÉS ! » décidé par les étoiles

**Files:**
- Modify: `scripts/Survie.cs` — `ReglesSurvie.TousSemes` (vers la ligne 223), champs de `Survie` (vers la ligne 674), `Commencer()` (vers la ligne 719), `RaisonDeFin()` (vers la ligne 778)
- Test: `app/test/csharp/TestsPlugin.cs` — les 4 tests `TousSemes` (vers les lignes 486-491)

**Interfaces:**
- Produces: `public static bool ReglesSurvie.TousSemes(int chaleur, bool policeDuJeu, int etoilesDuJeu, bool aEuDesEtoiles)` ; champ `bool aEuDesEtoiles` dans `Survie`, remis à `false` par `Commencer()`.

- [ ] **Step 1 : Écrire les tests qui échouent**

Dans `app/test/csharp/TestsPlugin.cs`, remplacer ce bloc :

```csharp
        // Test n°6 (« encore trop facile ») : la vraie police du jeu en même temps. « SEMÉS ! » =
        // nos flics semés ET plus une étoile.
        Verifier("semés : nos flics semés, pas de police du jeu → gagné", ReglesSurvie.TousSemes(0, false, 3), "non");
        Verifier("semés : nos flics semés mais encore 2 étoiles → pas encore", !ReglesSurvie.TousSemes(0, true, 2), "gagné");
        Verifier("semés : nos flics semés et plus d'étoile → gagné", ReglesSurvie.TousSemes(0, true, 0), "non");
        Verifier("semés : plus d'étoile mais un de nos flics → pas encore", !ReglesSurvie.TousSemes(1, true, 0), "gagné");
```

par :

```csharp
        // « SEMÉS ! » — sans police du jeu : plus aucun de nos flics (chacun abandonne). Avec
        // (« casser la vue », 2026-09-24) : nos flics ne lâchent plus, les étoiles seules décident,
        // après en avoir eu pendant la poursuite.
        Verifier("semés : sans police du jeu, nos flics semés → gagné", ReglesSurvie.TousSemes(0, false, 3, false), "non");
        Verifier("semés : sans police du jeu, encore un flic → pas encore", !ReglesSurvie.TousSemes(1, false, 0, false), "gagné");
        Verifier("semés : police du jeu, encore 2 étoiles → pas encore", !ReglesSurvie.TousSemes(0, true, 2, true), "gagné");
        Verifier("semés : police du jeu, plus d'étoile → gagné, même avec nos flics", ReglesSurvie.TousSemes(3, true, 0, true), "non");
        Verifier("semés : police du jeu, jamais eu d'étoile → pas fini", !ReglesSurvie.TousSemes(3, true, 0, false), "gagné");
```

- [ ] **Step 2 : Lancer les tests, vérifier l'échec**

Run : `cd app && node/node.exe --test test/plugin.test.js`
Attendu : ÉCHEC à la compilation du harnais, `error CS1501: Aucune surcharge pour la méthode 'TousSemes' ne prend 4 arguments` (ou le même message en anglais).

- [ ] **Step 3 : Écrire l'implémentation minimale**

Dans `scripts/Survie.cs`, remplacer :

```csharp
        // « SEMÉS ! » (test n°6 : « encore trop facile, ajoute la vraie police du jeu ») : plus
        // aucun de nos flics à ses trousses, ET, avec la police du jeu, plus une étoile.
        public static bool TousSemes(int chaleur, bool policeDuJeu, int etoilesDuJeu)
        {
            return chaleur <= 0 && (!policeDuJeu || etoilesDuJeu <= 0);
        }
```

par :

```csharp
        // « SEMÉS ! » — sans police du jeu : plus aucun de nos flics à ses trousses (chacun
        // abandonne quand il est semé, test n°5). Avec (« casser la vue », 2026-09-24) : nos flics
        // ne lâchent plus, les étoiles du jeu seules décident — plus une étoile, après en avoir eu
        // pendant cette poursuite (une étoile refusée par le jeu ne finit rien).
        public static bool TousSemes(int chaleur, bool policeDuJeu, int etoilesDuJeu, bool aEuDesEtoiles)
        {
            return policeDuJeu ? aEuDesEtoiles && etoilesDuJeu <= 0 : chaleur <= 0;
        }
```

Dans la classe `Survie`, sous `int semes;                      // flics semés (abandonnés) sur la poursuite`, ajouter :

```csharp
        bool aEuDesEtoiles;             // avec la police du jeu : une étoile au moins depuis le début (« semés » = plus aucune)
```

Dans `Commencer()`, sous `semes = 0;`, ajouter :

```csharp
            aEuDesEtoiles = false;
```

Dans `RaisonDeFin()`, remplacer :

```csharp
            // Le dernier flic a abandonné (test n°5) et, avec la police du jeu, plus une étoile (test n°6).
            if (ReglesSurvie.TousSemes(poursuite.Chaleur, policeDuJeu, EtoilesDuJeu())) return "semés";
```

par :

```csharp
            // Sans police du jeu : le dernier flic a abandonné (test n°5). Avec : plus une étoile,
            // après en avoir eu (« casser la vue », 2026-09-24).
            int etoiles = EtoilesDuJeu();
            if (etoiles > 0) aEuDesEtoiles = true;
            if (ReglesSurvie.TousSemes(poursuite.Chaleur, policeDuJeu, etoiles, aEuDesEtoiles)) return "semés";
```

- [ ] **Step 4 : Lancer les tests, vérifier qu'ils passent**

Run : `cd app && node/node.exe --test test/plugin.test.js`
Attendu : `# fail 0`, 196 tests (195 − 4 + 5), dont « compile contre le vrai ScriptHookVDotNet3.dll » OK.

---

### Task 2 : Casser la vue — vu, distancé reposé, fouille

**Files:**
- Modify: `scripts/Survie.cs` — `ReglesSurvie` (2 nouvelles règles sous `SortDuFlicLoin`), commentaire de `RaisonDeRecycler`, constantes et champs de `Survie`, `Commencer()`, `Tick()`, `Piloter()`, `Abandonner()`, 3 nouvelles méthodes (`SuivreLaVue`, `UnFlicLeVoit`, `Fouiller`)
- Modify: `scripts/GTAMax.ini` (commentaires `DUREE_S` et `POLICE_DU_JEU`), `README.md` (lignes 66-67)
- Test: `app/test/csharp/TestsPlugin.cs` — sous les tests `SortDuFlicLoin`

**Interfaces:**
- Consumes: `ReglesSurvie.SortDuFlicLoin(...)` (existe, rend « semé », « perdu de vue » ou `null`), `Recycler(f, joueur, raison, maintenant)` (existe), `EtoilesDuJeu()` (existe), champ `policeDuJeu` (existe).
- Produces: `public static string ReglesSurvie.DevenirDuFlicLoin(string loin, bool semeAssez, bool policeDuJeu, bool recherche)` → « abandonne », « distancé », « perdu de vue » ou `null` ; `public static void ReglesSurvie.PointDeFouille(float cx, float cy, int numero, float rayonM, out float x, out float y)` ; champ `bool recherche` (étoiles grises) dans `Survie`.

- [ ] **Step 1 : Écrire les tests qui échouent**

Dans `app/test/csharp/TestsPlugin.cs`, juste après le test `"loin : pas assez loin → rien"` (et avant le bloc « SEMÉS ! » de la tâche 1), ajouter :

```csharp
        // Que devient un flic loin (« casser la vue », 2026-09-24) ? Sans police du jeu : semé 5 s →
        // il abandonne. Avec : tant que le joueur est vu, il est reposé hors de sa vue ; pendant la
        // recherche (étoiles grises), rien — personne n'est reposé près du joueur.
        Verifier("devenir : sans police du jeu, semé 5 s → abandonne",
            ReglesSurvie.DevenirDuFlicLoin("semé", true, false, false) == "abandonne", ReglesSurvie.DevenirDuFlicLoin("semé", true, false, false));
        Verifier("devenir : semé depuis moins de 5 s → rien", ReglesSurvie.DevenirDuFlicLoin("semé", false, false, false) == null,
            ReglesSurvie.DevenirDuFlicLoin("semé", false, false, false));
        Verifier("devenir : sans police du jeu, perdu de vue → reposé",
            ReglesSurvie.DevenirDuFlicLoin("perdu de vue", false, false, false) == "perdu de vue", ReglesSurvie.DevenirDuFlicLoin("perdu de vue", false, false, false));
        Verifier("devenir : police du jeu, étoiles pleines, semé 5 s → distancé (reposé, n'abandonne plus)",
            ReglesSurvie.DevenirDuFlicLoin("semé", true, true, false) == "distancé", ReglesSurvie.DevenirDuFlicLoin("semé", true, true, false));
        Verifier("devenir : police du jeu, étoiles pleines, perdu de vue → reposé",
            ReglesSurvie.DevenirDuFlicLoin("perdu de vue", false, true, false) == "perdu de vue", ReglesSurvie.DevenirDuFlicLoin("perdu de vue", false, true, false));
        Verifier("devenir : recherche, semé 5 s → rien (il fouille)", ReglesSurvie.DevenirDuFlicLoin("semé", true, true, true) == null,
            ReglesSurvie.DevenirDuFlicLoin("semé", true, true, true));
        Verifier("devenir : recherche, perdu de vue → rien", ReglesSurvie.DevenirDuFlicLoin("perdu de vue", false, true, true) == null,
            ReglesSurvie.DevenirDuFlicLoin("perdu de vue", false, true, true));
        Verifier("devenir : pas loin → rien", ReglesSurvie.DevenirDuFlicLoin(null, false, true, false) == null,
            ReglesSurvie.DevenirDuFlicLoin(null, false, true, false));
        // Point de fouille : à 40 m du centre de recherche, une direction par numéro (pas tous au même point).
        float fx1, fy1, fx2, fy2;
        ReglesSurvie.PointDeFouille(100f, 200f, 1, 40f, out fx1, out fy1);
        ReglesSurvie.PointDeFouille(100f, 200f, 2, 40f, out fx2, out fy2);
        float rayonFouille = (float)Math.Sqrt((fx1 - 100f) * (fx1 - 100f) + (fy1 - 200f) * (fy1 - 200f));   // r1 existe déjà dans TestsSurvie
        Verifier("fouille : à 40 m du centre", Math.Abs(rayonFouille - 40f) < 0.01f, rayonFouille + " m");
        float ecartFouille = (float)Math.Sqrt((fx1 - fx2) * (fx1 - fx2) + (fy1 - fy2) * (fy1 - fy2));
        Verifier("fouille : deux flics qui se suivent, loin l'un de l'autre (> 40 m)", ecartFouille > 40f, ecartFouille + " m");
```

- [ ] **Step 2 : Lancer les tests, vérifier l'échec**

Run : `cd app && node/node.exe --test test/plugin.test.js`
Attendu : ÉCHEC à la compilation du harnais, `error CS0117: 'GTAMax.ReglesSurvie' ne contient pas de définition pour 'DevenirDuFlicLoin'` (et `PointDeFouille`).

- [ ] **Step 3 : Écrire les règles pures**

Dans `scripts/Survie.cs`, juste après la méthode `SortDuFlicLoin` (celle qui finit par `return venuAuContact ? "semé" : "perdu de vue";` puis `}`), ajouter :

```csharp

        // Que devient un flic loin de lui ? loin = SortDuFlicLoin ; semeAssez = « semé » depuis
        // SEME_MS. Sans police du jeu : semé assez longtemps → « abandonne » (test n°5) ; perdu de
        // vue → reposé ailleurs. Avec (« casser la vue », 2026-09-24) : il n'abandonne plus — tant
        // que le joueur est vu, semé assez longtemps → reposé hors de sa vue (« distancé »), la
        // meute reste entière ; pendant la recherche (étoiles grises), rien : il fouille, personne
        // n'est reposé près du joueur. Rend « abandonne », une raison de recycler ou null.
        public static string DevenirDuFlicLoin(string loin, bool semeAssez, bool policeDuJeu, bool recherche)
        {
            if (loin == null || (policeDuJeu && recherche)) return null;
            if (loin == "perdu de vue") return loin;
            if (!semeAssez) return null;
            return policeDuJeu ? "distancé" : "abandonne";
        }

        // Recherche (étoiles grises) : chaque flic fouille autour du centre de recherche du jeu, à
        // rayonM, dans une direction propre à son numéro (angle d'or, 137,5° : deux numéros qui se
        // suivent tombent loin l'un de l'autre) — pas tous au même point.
        public static void PointDeFouille(float cx, float cy, int numero, float rayonM, out float x, out float y)
        {
            double a = numero * 137.5 * Math.PI / 180.0;
            x = cx + (float)Math.Cos(a) * rayonM;
            y = cy + (float)Math.Sin(a) * rayonM;
        }
```

Et dans le commentaire au-dessus de `RaisonDeRecycler`, remplacer :

```csharp
        // flic semé n'est plus recyclé, il abandonne (test n°5, EstSeme).
```

par :

```csharp
        // flic semé : DevenirDuFlicLoin (sans police du jeu, il abandonne).
```

- [ ] **Step 4 : Lancer les tests, vérifier qu'ils passent**

Run : `cd app && node/node.exe --test test/plugin.test.js`
Attendu : `# fail 0`, 206 tests (196 + 10).

- [ ] **Step 5 : Brancher en jeu — constantes et champs**

Dans la classe `Survie`, sous `const int SEME_MS = 5000;`, ajouter :

```csharp
        // Casser la vue (2026-09-24, avec la police du jeu) : un de nos flics le VOIT à 100 m au plus,
        // sans décor entre eux (17 = décor + objets : les voitures ne cachent pas) ; vérifié chaque
        // seconde. Étoiles grises : chaque flic fouille à 40 m du centre de recherche, à 20 m/s.
        const float VUE_M = 100f;
        const int VUE_MS = 1000, LIGNE_DE_VUE_DECOR = 17;
        const float FOUILLE_RAYON_M = 40f, VITESSE_FOUILLE_MS = 20f;
```

Sous le champ `bool aEuDesEtoiles;` (tâche 1), ajouter :

```csharp
        bool recherche;                 // avec la police du jeu : étoiles grises, plus personne ne le voit — nos flics fouillent
        int prochaineVue;
```

Dans `Commencer()`, sous `aEuDesEtoiles = false;`, ajouter :

```csharp
            recherche = false;
            prochaineVue = 0;
```

- [ ] **Step 6 : Brancher en jeu — Tick et les 3 nouvelles méthodes**

Dans `Tick()`, remplacer :

```csharp
            Ranger(joueur, maintenant);
            Recruter(joueur, maintenant);
```

par :

```csharp
            if (policeDuJeu) SuivreLaVue(joueur, maintenant);
            Ranger(joueur, maintenant);
            if (!recherche) Recruter(joueur, maintenant);   // étoiles grises : personne n'est posé près de lui
```

Juste avant la méthode `Abandonner` (et son commentaire « // Semé (test n°5 … »), ajouter :

```csharp
        // ─── CASSER LA VUE (2026-09-24, avec la police du jeu) ────────────
        // Un de nos flics le voit : le jeu le sait, les étoiles restent pleines. Plus personne ne
        // le voit : étoiles grises, la recherche du jeu — nos flics fouillent, ne sont plus reposés
        // près de lui ni rattrapés en douce. Plus d'étoile : « SEMÉS ! » (RaisonDeFin).
        void SuivreLaVue(Ped joueur, int maintenant)
        {
            if (maintenant >= prochaineVue)
            {
                prochaineVue = maintenant + VUE_MS;
                if (UnFlicLeVoit(joueur)) Function.Call(Hash.REPORT_POLICE_SPOTTED_PLAYER, Game.Player.Handle);
            }
            bool grises = EtoilesDuJeu() > 0 && Function.Call<bool>(Hash.ARE_PLAYER_STARS_GREYED_OUT, Game.Player.Handle);
            if (grises == recherche) return;
            recherche = grises;
            foreach (FlicSurvie f in flics) f.ProchainOrdre = 0;   // nouvel ordre tout de suite : fouille, ou la chasse reprend
            journal.Info(grises ? "Survie : PERDU DE VUE — étoiles grises, nos flics fouillent autour du centre de recherche"
                                : "Survie : REPÉRÉ — étoiles pleines, la chasse reprend");
        }

        bool UnFlicLeVoit(Ped joueur)
        {
            Vector3 pj = joueur.Position;
            foreach (FlicSurvie f in flics)
            {
                if (f.MorteA != 0 || !f.Voiture.Exists() || f.Conducteur == null || !f.Conducteur.Exists() || f.Conducteur.IsDead) continue;
                if (f.Voiture.Position.DistanceTo(pj) > VUE_M) continue;
                if (Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, f.Conducteur, joueur, LIGNE_DE_VUE_DECOR)) return true;
            }
            return false;
        }

        // Étoiles grises : il ne sait plus où est le joueur. Il roule vers le centre de recherche
        // du jeu, à son point de fouille (40 m du centre, direction selon son numéro).
        void Fouiller(FlicSurvie f)
        {
            Vector3 c = Function.Call<Vector3>(Hash.GET_PLAYER_WANTED_CENTRE_POSITION, Game.Player.Handle);
            float x, y;
            ReglesSurvie.PointDeFouille(c.X, c.Y, f.Numero, FOUILLE_RAYON_M, out x, out y);
            Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, f.Conducteur, f.Voiture, x, y, c.Z, VITESSE_FOUILLE_MS, 1, f.ModeleHash,
                Regles.CONDUITE_TRAQUE, 10f, 1f);
            f.Phase = "fouille";
        }

```

- [ ] **Step 7 : Brancher en jeu — Piloter**

Dans `Piloter()`, remplacer :

```csharp
            // Semé 4 s de suite (venu au contact, puis loin et hors de vue) : il abandonne (test n°5).
            // Jamais venu au contact : il le cherche encore, reposé ailleurs.
            if (distance <= COURSE_DISTANCE_M) f.VenuAuContact = true;
            string loin = ReglesSurvie.SortDuFlicLoin(visible, distance, derriere, f.VenuAuContact, SEME_DERRIERE_M, SEME_M);
            if (loin != "semé") f.SemeDepuis = 0;
            else if (f.SemeDepuis == 0) f.SemeDepuis = Math.Max(1, maintenant);
            else if (maintenant - f.SemeDepuis >= SEME_MS) { Abandonner(f, distance, derriere); return; }
            string raison = ReglesSurvie.RaisonDeRecycler(visible, planteeMs, horsDeCourse, PLANTEE_MAX_MS) ?? (loin == "perdu de vue" ? loin : null);
            if (raison != null && maintenant >= prochainPing && maintenant - f.DernierePoseA >= RECYCLAGE_MIN_MS
                && Recycler(f, joueur, raison, maintenant)) return;
```

par :

```csharp
            // Loin (semé 5 s de suite, ou jamais venu au contact) : DevenirDuFlicLoin. Sans police du
            // jeu, un flic semé abandonne (test n°5) ; avec (« casser la vue »), il est reposé hors de
            // sa vue tant qu'on le voit. Étoiles grises : aucun recyclage, il fouille.
            if (distance <= COURSE_DISTANCE_M) f.VenuAuContact = true;
            string loin = ReglesSurvie.SortDuFlicLoin(visible, distance, derriere, f.VenuAuContact, SEME_DERRIERE_M, SEME_M);
            if (loin != "semé") f.SemeDepuis = 0;
            else if (f.SemeDepuis == 0) f.SemeDepuis = Math.Max(1, maintenant);
            bool semeAssez = f.SemeDepuis != 0 && maintenant - f.SemeDepuis >= SEME_MS;
            string sort = ReglesSurvie.DevenirDuFlicLoin(loin, semeAssez, policeDuJeu, recherche);
            if (sort == "abandonne") { Abandonner(f, distance, derriere); return; }
            string raison = recherche ? null : (ReglesSurvie.RaisonDeRecycler(visible, planteeMs, horsDeCourse, PLANTEE_MAX_MS) ?? sort);
            if (raison != null && maintenant >= prochainPing && maintenant - f.DernierePoseA >= RECYCLAGE_MIN_MS
                && Recycler(f, joueur, raison, maintenant)) return;
```

Puis, toujours dans `Piloter()`, remplacer :

```csharp
            if (!aPied) Rattraper(f, joueur, visible, distance, maintenant);
            if (maintenant >= f.ProchainOrdre)
            {
                Ordonner(f, joueur, distance, aPied, maintenant);
                f.ProchainOrdre = maintenant + (distance <= COURSE_DISTANCE_M ? ORDRE_PRES_MS : ORDRE_LOIN_MS);
            }
```

par :

```csharp
            if (!aPied && !recherche) Rattraper(f, joueur, visible, distance, maintenant);   // étoiles grises : pas de triche
            if (maintenant >= f.ProchainOrdre)
            {
                if (recherche) Fouiller(f);
                else Ordonner(f, joueur, distance, aPied, maintenant);
                f.ProchainOrdre = maintenant + (!recherche && distance <= COURSE_DISTANCE_M ? ORDRE_PRES_MS : ORDRE_LOIN_MS);
            }
```

- [ ] **Step 8 : Abandonner — sans police du jeu seulement**

`Abandonner` n'est plus appelé qu'avec `POLICE_DU_JEU=0` : la branche « Tous semés ! Perds maintenant tes étoiles » devient morte. Remplacer :

```csharp
        // Semé (test n°5 : « il faut que je puisse les semer ») : il abandonne — rendu au jeu,
        // un flic de moins à ses trousses, une étoile de moins. Le dernier : « SEMÉS ! » (RaisonDeFin).
```

par :

```csharp
        // Semé, sans police du jeu (test n°5 : « il faut que je puisse les semer ») : il abandonne —
        // rendu au jeu, un flic de moins à ses trousses. Le dernier : « SEMÉS ! » (RaisonDeFin).
```

et remplacer :

```csharp
            if (reste > 0) Notifier("~g~Flic semé !~s~ Encore " + reste + " à tes trousses");
            else if (policeDuJeu && EtoilesDuJeu() > 0) Notifier("~g~Tous semés !~s~ Perds maintenant tes étoiles");
```

par :

```csharp
            if (reste > 0) Notifier("~g~Flic semé !~s~ Encore " + reste + " à tes trousses");
```

- [ ] **Step 9 : Remettre l'ini et le README d'aplomb**

Dans `scripts/GTAMax.ini`, remplacer :

```ini
; Tu peux aussi les SEMER : un flic loin derrière toi (150 m) et hors de vue
; pendant 4 s abandonne (une étoile de moins) ; tous semés : « SEMÉS ! ».
```

par :

```ini
; Tu peux aussi les SEMER (voir POLICE_DU_JEU).
```

et remplacer :

```ini
; sans armes, sans hélicos). Pour « SEMÉS ! », il faut aussi perdre tes étoiles.
; 0 = seulement les flics du plugin.
```

par :

```ini
; sans armes, sans hélicos). Semer = casser la vue, comme dans GTA : tant qu'un
; flic te voit, ceux que tu distances reviennent ; plus personne ne te voit =
; étoiles grises, ils fouillent ; plus d'étoile = « SEMÉS ! ».
; 0 = seulement les flics du plugin (un flic distancé 5 s abandonne).
```

Dans `README.md`, remplacer :

```markdown
cadeau). Tu peux les **semer** : un flic qui t'a approché, puis loin derrière toi
et hors de vue 5 s, abandonne ; tous semés **et** plus une étoile, « SEMÉS ! ».
```

par :

```markdown
cadeau). Pour les **semer**, casse la vue comme dans GTA : tant qu'un flic te voit,
ceux que tu distances reviennent ; étoiles grises, ils fouillent ; plus d'étoile, « SEMÉS ! ».
```

- [ ] **Step 10 : Lancer tous les tests**

Run : `cd app && node/node.exe --test "test/**/*.test.js"`
Attendu : `# fail 0`. Le test « compile contre le vrai ScriptHookVDotNet3.dll » passe, donc les 4 natives existent sous ces noms.

---

### Task 3 : Installer et vérifier en jeu

**Files:** aucun (installation de la DLL dans le dossier du jeu)

- [ ] **Step 1 : Installer**

Run : `cd app && node/node.exe outils/plugin.js installer`
Attendu : compilation OK, `GTAMax.dll` et `GTAMax.version` écrits dans `<GTA V>\scripts\` (`GTAMax.ini` du jeu jamais écrasé : il a déjà `POLICE_DU_JEU=1`).

- [ ] **Step 2 : Elliot teste (jeu ouvert : F8, `Reload()`, Entrée)**

1. Setup → « 🧪 Tester sans live » → une rafale de 3 cadeaux.
2. Rouler vite en ligne droite : les flics distancés doivent revenir.
3. Tourner plusieurs fois, passer par des ruelles : les étoiles doivent passer au gris.
4. Sortir du cercle de recherche jusqu'à « SEMÉS ! ».

- [ ] **Step 3 : Lire le journal**

`<GTA V>\scripts\GTAMax.log` doit contenir :
- des lignes `flic N distancé → réapparaît …` (et plus aucune ligne `SEMÉ (… il abandonne`) ;
- `PERDU DE VUE — étoiles grises` puis `REPÉRÉ` ou `FIN (semés)` ;
- pendant la recherche, la phase `fouille` dans le résumé des 10 s.

À régler selon le ressenti : `VUE_M` (100 m), `FOUILLE_RAYON_M` (40 m), `SEME_MS` (5 s).
