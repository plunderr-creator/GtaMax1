/*
 * Harnais de tests de la LOGIQUE PURE du plugin (scripts/GTAMax.cs).
 * Compilé avec /define:TESTS_SANS_JEU par app/test/plugin.test.js : la classe
 * Script (qui dépend du jeu) est exclue, tout le reste est testé ici, sans GTA V.
 * Sortie : une ligne « OK <nom> » ou « ECHEC <nom> : <détail> » par test ;
 * code de sortie 0 si tout passe. C# 5 (csc du .NET Framework).
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GTAMax;

static class TestsPlugin
{
    static int echecs = 0;

    static void Verifier(string nom, bool ok, string detail)
    {
        Console.WriteLine((ok ? "OK " : "ECHEC ") + nom + (ok ? "" : " : " + detail));
        if (!ok) echecs++;
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);

        // Mode « consommer <dossier> » : joue le plugin une fois sur une vraie file
        // écrite par le serveur Node (test de protocole de bout en bout, voir
        // plugin.test.js). Une ligne « CMD\tVERBE\tclé=valeur… » par commande.
        if (args.Length == 2 && args[0] == "consommer")
        {
            List<Commande> lotConso = ConsommateurFile.Prendre(args[1], DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 60000,
                delegate (string m) { Console.Error.WriteLine(m); });
            if (lotConso != null)
                foreach (Commande cmd in lotConso)
                {
                    var champs = new List<string> { "CMD", cmd.Verbe };
                    foreach (var kv in cmd.Args) champs.Add(kv.Key + "=" + kv.Value);
                    Console.WriteLine(string.Join("\t", champs.ToArray()));
                }
            return 0;
        }

        string err;

        // ── Parseur de ligne ──────────────────────────────────────────────
        Commande c = LecteurFile.ParserLigne("SPAWN_POLICE t=42 donateur=bob cadeau=Hand%20Hearts", out err);
        Verifier("ligne valide, valeur décodée (%20)",
            c != null && c.Verbe == "SPAWN_POLICE" && c.T == 42 && c.Arg("donateur") == "bob" && c.Arg("cadeau") == "Hand Hearts",
            c == null ? err : c.ToString());

        c = LecteurFile.ParserLigne("SPAWN_POLICE t=1 donateur=%F0%9F%8C%B9a%3Db\r", out err);
        Verifier("UTF-8 encodé et « = » dans une valeur", c != null && c.Arg("donateur") == "\U0001F339a=b",
            c == null ? err : c.Arg("donateur"));

        c = LecteurFile.ParserLigne("spawn_police t=1", out err);
        Verifier("verbe en minuscules refusé", c == null && err != null, "accepté");

        c = LecteurFile.ParserLigne("SPAWN_POLICE t=1 orphelin", out err);
        Verifier("argument sans « = » refusé", c == null && err != null, "accepté");

        c = LecteurFile.ParserLigne("SPAWN_POLICE t=abc", out err);
        Verifier("horodatage illisible refusé", c == null && err != null, "accepté");

        c = LecteurFile.ParserLigne("   ", out err);
        Verifier("ligne vide : ni commande ni erreur", c == null && err == null, err);
        c = LecteurFile.ParserLigne("# commentaire", out err);
        Verifier("commentaire : ni commande ni erreur", c == null && err == null, err);

        c = LecteurFile.ParserLigne("SPAWN_POLICE donateur=main", out err);
        Verifier("ligne sans t= (écrite à la main) acceptée", c != null && c.T == 0, err);
        Verifier("argument absent → chaîne vide", c != null && c.Arg("cadeau") == "", "non vide");

        // ── Contenu complet : péremption et journal ───────────────────────
        var journal = new List<string>();
        long maintenant = 1000000;
        List<Commande> lot = LecteurFile.ParserContenu(
            "SPAWN_POLICE t=" + (maintenant - 1000) + " donateur=frais\n" +
            "SPAWN_POLICE t=" + (maintenant - 61000) + " donateur=vieux\n" +
            "n'importe quoi\n" +
            "\n" +
            "SPAWN_POLICE donateur=main\n",
            maintenant, 60000, journal.Add);
        Verifier("fichier : les commandes fraîches passent, dans l'ordre",
            lot.Count == 2 && lot[0].Arg("donateur") == "frais" && lot[1].Arg("donateur") == "main",
            lot.Count + " commande(s)");
        Verifier("fichier : périmée et invalide journalisées", journal.Count == 2, string.Join(" | ", journal.ToArray()));
        Verifier("fichier vide ou null", LecteurFile.ParserContenu(null, 0, 1, null).Count == 0, "non vide");

        // ── Consommation de la file (vrais fichiers, dossier temporaire) ──
        string dossier = Path.Combine(Path.GetTempPath(), "gtamax-conso-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dossier);
        try
        {
            string cheminFile = Path.Combine(dossier, ConsommateurFile.FICHIER);
            string cheminEnCours = Path.Combine(dossier, ConsommateurFile.FICHIER_EN_COURS);
            Verifier("file absente → rien (serveur éteint)", ConsommateurFile.Prendre(dossier, maintenant, 60000, null) == null, "non null");

            File.WriteAllText(cheminFile, "SPAWN_POLICE t=" + maintenant + " donateur=a\nSPAWN_POLICE t=" + maintenant + " donateur=b\n");
            List<Commande> pris = ConsommateurFile.Prendre(dossier, maintenant, 60000, null);
            Verifier("file prise : commandes dans l'ordre, fichier supprimé",
                pris != null && pris.Count == 2 && pris[0].Arg("donateur") == "a" && !File.Exists(cheminFile) && !File.Exists(cheminEnCours),
                pris == null ? "null" : pris.Count + " commande(s)");
            Verifier("file déjà prise → rien au tick suivant (jamais deux fois)", ConsommateurFile.Prendre(dossier, maintenant, 60000, null) == null, "rejouée");

            File.WriteAllText(cheminEnCours, "SPAWN_POLICE t=" + maintenant + " donateur=reste\n");
            File.WriteAllText(cheminFile, "SPAWN_POLICE t=" + maintenant + " donateur=nouveau\n");
            pris = ConsommateurFile.Prendre(dossier, maintenant, 60000, null);
            Verifier("un .en-cours resté d'un essai interrompu passe en premier",
                pris != null && pris.Count == 1 && pris[0].Arg("donateur") == "reste" && File.Exists(cheminFile), "ordre faux");
            pris = ConsommateurFile.Prendre(dossier, maintenant, 60000, null);
            Verifier("puis le fichier suivant", pris != null && pris.Count == 1 && pris[0].Arg("donateur") == "nouveau", "absent");

            File.WriteAllText(cheminFile, "SPAWN_POLICE t=" + maintenant + " donateur=verrou\n");
            using (new FileStream(cheminFile, FileMode.Open, FileAccess.Read, FileShare.None))
                Verifier("fichier verrouillé → on réessaiera (null, rien perdu)", ConsommateurFile.Prendre(dossier, maintenant, 60000, null) == null && File.Exists(cheminFile), "pris quand même");
            pris = ConsommateurFile.Prendre(dossier, maintenant, 60000, null);
            Verifier("verrou levé → commande reçue au tick suivant", pris != null && pris.Count == 1 && pris[0].Arg("donateur") == "verrou", "perdue");
        }
        finally { try { Directory.Delete(dossier, true); } catch (Exception) { } }

        // ── Registre d'actions ────────────────────────────────────────────
        var registre = new RegistreActions();
        var recues = new List<string>();
        registre.Enregistrer("SPAWN_POLICE", delegate (Commande x) { recues.Add(x.Arg("donateur")); });
        registre.Enregistrer("BOUM", delegate (Commande x) { throw new InvalidOperationException("bug"); });
        journal.Clear();
        bool ok1 = registre.Executer(LecteurFile.ParserLigne("SPAWN_POLICE t=1 donateur=bob", out err), journal.Add);
        Verifier("registre : action enregistrée exécutée", ok1 && recues.Count == 1 && recues[0] == "bob", "non exécutée");
        bool ok2 = registre.Executer(LecteurFile.ParserLigne("INCONNU t=1", out err), journal.Add);
        Verifier("registre : verbe inconnu journalisé, sans exception", !ok2 && journal.Count == 1 && journal[0].Contains("inconnu"),
            string.Join(" | ", journal.ToArray()));
        bool ok3 = true;
        try { ok3 = registre.Executer(LecteurFile.ParserLigne("BOUM t=1", out err), journal.Add); }
        catch (Exception e) { Verifier("registre : action qui lève attrapée", false, "exception remontée : " + e.Message); }
        Verifier("registre : action qui lève attrapée et journalisée", !ok3 && journal.Count == 2 && journal[1].Contains("bug"),
            string.Join(" | ", journal.ToArray()));

        // ── File de spawn : 1 toutes les 3 s, plafond, âge max ────────────
        var file = new FileSpawn<string>(3000, 20, 60000);
        for (int i = 0; i < 10; i++) file.Ajouter("don" + i, 0);
        Verifier("file : première apparition immédiate", file.Pret(0) && file.Prendre(0) == "don0", "pas prête");
        Verifier("file : pas de deuxième avant 3 s", !file.Pret(2999), "prête trop tôt");
        Verifier("file : deuxième à 3 s, dans l'ordre", file.Pret(3000) && file.Prendre(3000) == "don1", "pas prête");
        int sorties = 2;
        for (long t = 3100; t <= 30000; t += 100) if (file.Pret(t)) { file.Prendre(t); sorties++; }
        Verifier("file : 10 dons d'un coup → 10 apparitions en 27 s, pas plus vite", sorties == 10 && file.Count == 0,
            sorties + " sorties");

        var pleine = new FileSpawn<int>(3000, 20, 60000);
        int acceptes = 0;
        for (int i = 0; i < 25; i++) if (pleine.Ajouter(i, 0)) acceptes++;
        Verifier("file : plafond de 20 demandes en attente", acceptes == 20 && pleine.Count == 20, acceptes + " acceptées");

        var agee = new FileSpawn<string>(3000, 20, 60000);
        agee.Ajouter("vieille", 0);
        agee.Ajouter("recente", 50000);
        int jetees = agee.Purger(61000);
        Verifier("file : demande de plus de 60 s jetée (pas de rattrapage)", jetees == 1 && agee.Count == 1 && agee.Prendre(61000) == "recente",
            jetees + " jetée(s)");

        // ── Parc : 8 voitures max, la plus ancienne sort ──────────────────
        var parc = new Parc<string>(8);
        for (int i = 1; i <= 8; i++) { Verifier("parc : place pour la voiture " + i, parc.FaireDeLaPlace().Count == 0, "éviction prématurée"); parc.Ajouter("v" + i); }
        List<string> sortantes = parc.FaireDeLaPlace();
        parc.Ajouter("v9");
        Verifier("parc : la 9e fait sortir la plus ancienne", sortantes.Count == 1 && sortantes[0] == "v1" && parc.Count == 8 && parc.Unites[0] == "v2",
            string.Join(",", sortantes.ToArray()));
        List<string> retirees = parc.Retirer(delegate (string u) { return u == "v4" || u == "v6"; });
        Verifier("parc : retrait ciblé, ordre conservé", retirees.Count == 2 && parc.Count == 6 && parc.Unites[2] == "v5",
            string.Join(",", parc.Unites.ToArray()));
        int appels = 0;
        parc.Retirer(delegate (string u) { appels++; return false; });
        Verifier("parc : condition évaluée une fois par unité", appels == 6, appels + " appels");

        // ── Règles ────────────────────────────────────────────────────────
        List<string> modeles = Regles.ListeModeles(" Police, police2 ;POLICE3  sheriff,police ,, ");
        Verifier("ini : liste de modèles nettoyée, sans doublon", string.Join(",", modeles.ToArray()) == "police,police2,police3,sheriff",
            string.Join(",", modeles.ToArray()));
        // Apparition derrière le joueur : il regarde vers +Y.
        Verifier("derrière : un point au sud d'un joueur tourné au nord", Regles.EstDerriere(0f, -20f, 0f, 1f), "devant");
        Verifier("derrière : un point devant est refusé", !Regles.EstDerriere(3f, 15f, 0f, 1f), "derrière");
        Verifier("derrière : pile sur le côté n'est pas derrière", !Regles.EstDerriere(20f, 0f, 0f, 1f), "derrière");

        // Voiture plantée = ne roule pas loin du joueur ; au contact, l'arrêt est normal.
        Verifier("plantée : à l'arrêt à 40 m", Regles.EstPlantee(0.5f, 40f, 2f, 12f), "non plantée");
        Verifier("plantée : au contact après une percussion, non", !Regles.EstPlantee(0f, 5f, 2f, 12f), "plantée");
        Verifier("plantée : elle roule, non", !Regles.EstPlantee(15f, 40f, 2f, 12f), "plantée");

        // Mode : survie par défaut, étoiles, voitures, ou les deux.
        Verifier("mode survie", Regles.LireMode("survie") == ModeJeu.Survie, Regles.LireMode("survie").ToString());
        Verifier("mode Survie (casse, espace)", Regles.LireMode(" Survie ") == ModeJeu.Survie, Regles.LireMode(" Survie ").ToString());
        Verifier("mode etoiles", Regles.LireMode("etoiles") == ModeJeu.Etoiles, Regles.LireMode("etoiles").ToString());
        Verifier("mode Étoiles (accent, casse, espace)", Regles.LireMode("Étoiles ") == ModeJeu.Etoiles, Regles.LireMode("Étoiles ").ToString());
        Verifier("mode voitures", Regles.LireMode("voitures") == ModeJeu.Voitures, Regles.LireMode("voitures").ToString());
        Verifier("mode les-deux", Regles.LireMode("les-deux") == ModeJeu.LesDeux, Regles.LireMode("les-deux").ToString());
        Verifier("mode inconnu ou absent → survie", Regles.LireMode("n'importe") == ModeJeu.Survie && Regles.LireMode(null) == ModeJeu.Survie,
            Regles.LireMode("n'importe").ToString());

        // Étoiles : +1 par cadeau, plafond, jamais à la baisse.
        Verifier("étoiles : 0 → 1", Regles.EtoilesVisees(0, 0, 1, 3) == 1, Regles.EtoilesVisees(0, 0, 1, 3).ToString());
        Verifier("étoiles : rafale 1 → 2 → 3", Regles.EtoilesVisees(1, 1, 1, 3) == 2 && Regles.EtoilesVisees(2, 2, 1, 3) == 3, "");
        Verifier("étoiles : plafonnées à 3", Regles.EtoilesVisees(3, 3, 1, 3) == 3, Regles.EtoilesVisees(3, 3, 1, 3).ToString());
        Verifier("étoiles : la cible en cours compte même si le jeu a baissé", Regles.EtoilesVisees(1, 2, 1, 3) == 3, Regles.EtoilesVisees(1, 2, 1, 3).ToString());
        Verifier("étoiles : 5 gagnées en jouant restent 5", Regles.EtoilesVisees(5, 0, 1, 3) == 5, Regles.EtoilesVisees(5, 0, 1, 3).ToString());
        Verifier("police : cop, SWAT, armée ; pas un civil", Regles.EstPolice(6) && Regles.EstPolice(27) && Regles.EstPolice(29) && !Regles.EstPolice(4), "");

        // Traque : viser où le joueur SERA.
        Verifier("anticipation : 30 m à 30 m/s → 1 s", Math.Abs(Regles.Anticipation(30f, 30f) - 1f) < 1e-4, Regles.Anticipation(30f, 30f) + " s");
        Verifier("anticipation : à l'arrêt, la vitesse plancher (10 m/s) évite la division par 0", Math.Abs(Regles.Anticipation(5f, 0f) - 0.5f) < 1e-4, Regles.Anticipation(5f, 0f) + " s");
        Verifier("anticipation : plafonnée à 1,5 s même de loin", Regles.Anticipation(500f, 20f) == 1.5f, Regles.Anticipation(500f, 20f) + " s");
        Verifier("style : de loin, suit les routes sans contourner le joueur",
            Regles.StyleConduite(100f, 40f) == Regles.CONDUITE_TRAQUE && (Regles.CONDUITE_TRAQUE & 64) != 0 && (Regles.CONDUITE_TRAQUE & 1) == 0,
            Regles.StyleConduite(100f, 40f).ToString());
        Verifier("style : au contact, ligne droite forcée", (Regles.StyleConduite(25f, 40f) & Regles.CONDUITE_LIGNE_DROITE) != 0, "");
        Verifier("face au joueur : droit devant", Regles.EstFaceA(0f, 1f, 0f, 10f, 0.87f), "non");
        Verifier("face au joueur : à 45°, non", !Regles.EstFaceA(0f, 1f, 10f, 10f, 0.87f), "oui");
        Verifier("face au joueur : dans le dos, non", !Regles.EstFaceA(0f, 1f, 0f, -10f, 0.87f), "oui");

        // Semée : loin ET hors écran (sinon on la verrait disparaître).
        Verifier("semée : à 150 m hors écran", Regles.EstSemee(150f, false, 120f), "non semée");
        Verifier("semée : à 150 m mais visible, non", !Regles.EstSemee(150f, true, 120f), "semée");
        Verifier("semée : à 60 m hors écran, non", !Regles.EstSemee(60f, false, 120f), "semée");

        TestsSurvie();

        return echecs == 0 ? 0 : 1;
    }

    // ── Mode survie (façon Driver 2) ──────────────────────────────────────
    static void TestsSurvie()
    {
        // La poursuite : la chaleur (le « felony » de Driver 2) monte d'un cran par cadeau.
        var p = new Poursuite(4, 60000, 5);
        Verifier("poursuite : inactive au départ, 0 flic", !p.Active && p.FlicsVoulus == 0, p.FlicsVoulus.ToString());
        Verifier("poursuite : 1er cadeau = début, 1 flic", p.Cadeau(1000) && p.Active && p.FlicsVoulus == 1 && p.DebutA == 1000, p.FlicsVoulus.ToString());
        Verifier("poursuite : 2e cadeau = même poursuite, 2 flics", !p.Cadeau(5000) && p.FlicsVoulus == 2 && p.DebutA == 1000, p.FlicsVoulus.ToString());
        for (int i = 0; i < 10; i++) p.Cadeau(6000);
        Verifier("poursuite : 4 flics au plus (Driver 2 : 4)", p.FlicsVoulus == 4, p.FlicsVoulus.ToString());
        Verifier("poursuite : chaleur plafonnée à 5", p.Chaleur == 5, p.Chaleur.ToString());
        Verifier("poursuite : dure 60 s après le DERNIER cadeau", !p.Expiree(65999) && p.Expiree(66000), p.FinA.ToString());
        Verifier("poursuite : durée comptée depuis le 1er cadeau", p.DureeMs(66000) == 65000, p.DureeMs(66000).ToString());
        Verifier("poursuite : plus de chaleur = rattrapage plus fort",
            new Poursuite(4, 60000, 5).FacteurRattrapage < p.FacteurRattrapage && Math.Abs(p.FacteurRattrapage - 1.2f) < 1e-4, p.FacteurRattrapage.ToString());
        p.Terminer();
        Verifier("poursuite : terminée → 0 flic, chaleur à 0", !p.Active && p.FlicsVoulus == 0 && p.Chaleur == 0, p.Chaleur.ToString());
        Verifier("poursuite : un cadeau après la fin en relance une neuve", p.Cadeau(100000) && p.DebutA == 100000 && p.FlicsVoulus == 1, p.DebutA.ToString());

        // Où faire apparaître : jamais sous les yeux du joueur.
        float[] premiers = ReglesSurvie.AnglesCandidats(0, true);
        Verifier("apparition : un nouveau flic arrive d'abord par derrière", Math.Abs(Math.Abs(premiers[0]) - 180f) < 1f, premiers[0].ToString());
        bool jamaisDevant = true;
        foreach (float a in premiers) if (Math.Abs(a) < 90f) jamaisDevant = false;
        Verifier("apparition : un nouveau flic ne vient jamais de devant", jamaisDevant, string.Join(",", Array.ConvertAll(premiers, x => x.ToString())));
        float[] r0 = ReglesSurvie.AnglesCandidats(0, false), r1 = ReglesSurvie.AnglesCandidats(1, false);
        var directions = new HashSet<int>();
        foreach (float a in r0) directions.Add((int)Math.Round(((a % 360f) + 360f) % 360f));
        Verifier("recyclage : 8 directions, tout le tour (Driver 2 : tout le cercle)", r0.Length == 8 && directions.Count == 8,
            string.Join(",", Array.ConvertAll(r0, x => x.ToString())));
        Verifier("recyclage : la direction de départ change d'un flic à l'autre", Math.Abs(r0[0] - r1[0]) > 1f, r0[0] + " / " + r1[0]);
        Verifier("secteur : derrière, sur le côté, devant",
            ReglesSurvie.Secteur(180f) == "derrière" && ReglesSurvie.Secteur(-150f) == "derrière" && ReglesSurvie.Secteur(90f) == "sur le côté"
            && ReglesSurvie.Secteur(-60f) == "sur le côté" && ReglesSurvie.Secteur(10f) == "devant",
            ReglesSurvie.Secteur(180f) + "/" + ReglesSurvie.Secteur(90f) + "/" + ReglesSurvie.Secteur(10f));
        Verifier("apparition : 80 m pour un rayon de 80 m", ReglesSurvie.DistanceDApparitionOk(80f, 2f, 80f), "refusé");
        Verifier("apparition : trop près (30 m), refusé", !ReglesSurvie.DistanceDApparitionOk(30f, 0f, 80f), "accepté");
        Verifier("apparition : trop loin (200 m), refusé", !ReglesSurvie.DistanceDApparitionOk(200f, 0f, 80f), "accepté");
        Verifier("apparition : 15 m plus bas (sous un pont), refusé", !ReglesSurvie.DistanceDApparitionOk(80f, -15f, 80f), "accepté");

        // Champ de la caméra : lacet 0 = elle regarde le nord (+Y), lacet 90 = l'ouest.
        Verifier("caméra : droit devant, dans le champ", ReglesSurvie.DansLeChamp(0f, 0f, 0f, 0f, 0f, 0f, 80f, 0f, 60f), "hors champ");
        Verifier("caméra : derrière, hors champ", !ReglesSurvie.DansLeChamp(0f, 0f, 0f, 0f, 0f, 0f, -80f, 0f, 60f), "dans le champ");
        Verifier("caméra : pile sur le côté, hors champ", !ReglesSurvie.DansLeChamp(0f, 0f, 0f, 0f, 0f, 80f, 0f, 0f, 60f), "dans le champ");
        Verifier("caméra : tournée à l'ouest, l'ouest est dans le champ", ReglesSurvie.DansLeChamp(0f, 0f, 0f, 0f, 90f, -80f, 0f, 0f, 60f), "hors champ");
        Verifier("caméra : tournée à l'ouest, le nord est hors champ", !ReglesSurvie.DansLeChamp(0f, 0f, 0f, 0f, 90f, 0f, 80f, 0f, 60f), "dans le champ");

        // Recycler un flic (le « ping out / ping in » de Driver 2) : jamais sous ses yeux.
        Verifier("recyclage : jamais à la vue du joueur", ReglesSurvie.RaisonDeRecycler(true, 500f, 99999, true, 150f, 2500) == null, "recyclé");
        Verifier("recyclage : semé à 160 m hors champ", ReglesSurvie.RaisonDeRecycler(false, 160f, 0, false, 150f, 2500) == "semé",
            ReglesSurvie.RaisonDeRecycler(false, 160f, 0, false, 150f, 2500));
        Verifier("recyclage : planté 2,5 s hors champ", ReglesSurvie.RaisonDeRecycler(false, 60f, 2500, false, 150f, 2500) == "planté",
            ReglesSurvie.RaisonDeRecycler(false, 60f, 2500, false, 150f, 2500));
        Verifier("recyclage : retourné ou à l'eau hors champ", ReglesSurvie.RaisonDeRecycler(false, 20f, 0, true, 150f, 2500) == "retourné ou à l'eau",
            ReglesSurvie.RaisonDeRecycler(false, 20f, 0, true, 150f, 2500));
        Verifier("recyclage : en pleine chasse, non", ReglesSurvie.RaisonDeRecycler(false, 60f, 1000, false, 150f, 2500) == null, "recyclé");

        // Conduite au contact (le « batter » de Driver 2) : r = joueur - flic, w = vitesse joueur - vitesse flic.
        float t = ReglesSurvie.TempsDInterception(-2f, 30f, 0f, -10f, 1f, 2f);
        Verifier("interception : 30 m d'écart, 10 m/s de plus → plafonnée à 2 s", Math.Abs(t - 2f) < 1e-4, t + " s");
        t = ReglesSurvie.TempsDInterception(0f, 20f, 0f, -20f, 1f, 2f);
        Verifier("interception : 20 m d'écart, 20 m/s de plus → 1 s", Math.Abs(t - 1f) < 1e-4, t + " s");
        t = ReglesSurvie.TempsDInterception(0f, 20f, 0f, 5f, 0.7f, 2f);
        Verifier("interception : il ne se rapproche pas → anticipation de secours", Math.Abs(t - 0.7f) < 1e-4, t + " s");
        float vx, vy;
        // Joueur à l'origine vers le nord à 30 m/s, sa droite = +X ; flic 30 m derrière, un peu à droite.
        ReglesSurvie.PointVise(0f, 0f, 0f, 30f, 2f, -30f, 1f, 1f, 0f, 1.5f, 20f, out vx, out vy);
        Verifier("visée : sur le flanc droit, côté du flic (percussion par le côté)", vx > 0.5f, vx + " ; " + vy);
        Verifier("visée : au-delà du joueur (il ne freine pas en arrivant)", vy > 45f, vx + " ; " + vy);
        ReglesSurvie.PointVise(0f, 0f, 0f, 30f, -3f, -30f, 1f, 1f, 0f, 1.5f, 20f, out vx, out vy);
        Verifier("visée : flic à gauche → flanc gauche", vx < -0.5f, vx + " ; " + vy);

        // Rattrapage invisible (la puissance « auto » de Driver 2), seulement hors champ.
        Verifier("rattrapage : plus lent que le joueur → 10 % + 3 m/s plus vite que lui", Math.Abs(ReglesSurvie.VitesseRattrapage(30f, 20f, 1.1f, 65f) - 36f) < 1e-3,
            ReglesSurvie.VitesseRattrapage(30f, 20f, 1.1f, 65f).ToString());
        Verifier("rattrapage : déjà plus rapide → rien", ReglesSurvie.VitesseRattrapage(30f, 40f, 1.1f, 65f) == 0f,
            ReglesSurvie.VitesseRattrapage(30f, 40f, 1.1f, 65f).ToString());
        Verifier("rattrapage : joueur arrêté → 20 m/s pour arriver vite", ReglesSurvie.VitesseRattrapage(0f, 5f, 1.1f, 65f) == 20f,
            ReglesSurvie.VitesseRattrapage(0f, 5f, 1.1f, 65f).ToString());
        Verifier("rattrapage : plafonné", ReglesSurvie.VitesseRattrapage(70f, 10f, 1.2f, 65f) == 65f,
            ReglesSurvie.VitesseRattrapage(70f, 10f, 1.2f, 65f).ToString());

        // Textes affichés en jeu.
        Verifier("durée : 45 s", ReglesSurvie.Duree(45400) == "45 s", ReglesSurvie.Duree(45400));
        Verifier("durée : 2 min 05 s", ReglesSurvie.Duree(125000) == "2 min 05 s", ReglesSurvie.Duree(125000));
        Verifier("texte en jeu : pseudo sans ~ (codes couleur de GTA)", ReglesSurvie.TexteJeu("~r~bob~s~", 30).IndexOf('~') < 0,
            ReglesSurvie.TexteJeu("~r~bob~s~", 30));
        Verifier("texte en jeu : coupé à la longueur max", ReglesSurvie.TexteJeu(new string('a', 50), 20).Length == 20, "");
        Verifier("texte en jeu : vide ou null → ?", ReglesSurvie.TexteJeu(null, 20) == "?" && ReglesSurvie.TexteJeu(" ~ ", 20) == "?",
            ReglesSurvie.TexteJeu(" ~ ", 20));
    }
}
