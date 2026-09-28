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

    static bool ChasseContinue()
    {
        for (int i = 0; i < 120; i++)
            if (ReglesSurvie.ReprendreChasse(true, 10, 10, i == 0 ? 0 : 1)) return false;
        return !ReglesSurvie.ReprendreChasse(true, 10, 10, 2);
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        var points = new ScoresImpact(); bool marque = false;
        Verifier("Points : aucun contact, aucun point", !points.Toucher("alice", false, ref marque) && points.Total("alice") == 0, "contact absent");
        Verifier("Points : premier choc +1", points.Toucher("alice", true, ref marque) && points.Total("alice") == 1, "premier choc");
        Verifier("Points : contact prolongé ou second choc même voiture sans doublon", !points.Toucher("alice", true, ref marque) && points.Total("alice") == 1, "doublon");
        var dons = new ScoresImpact();
        Verifier("Points : cadeau de 5 pièces = +5", dons.Offrir("bob", 5) && dons.Total("bob") == 5, dons.Total("bob").ToString());
        Verifier("Points : cadeau sans pièce = rien", !dons.Offrir("bob", 0) && !dons.Offrir(" ", 3) && dons.Total("bob") == 5, "vide");
        bool autreVoiture = false; points.Toucher("ALICE", true, ref autreVoiture);
        bool bob = false; points.Toucher("bob", true, ref bob);
        Verifier("Points : cumul par viewer et isolation", points.Total("alice") == 2 && points.Total("bob") == 1, "attribution");
        var recharge = new ScoresImpact(); recharge.Importer(points.Exporter());
        Verifier("Points : sauvegarde et rechargement", recharge.Total("alice") == 2 && recharge.Total("bob") == 1, "persistance");
        var garage = new ScoresImpact();
        garage.Importer("YWxpY2U=:30\nYm9i:2\n");
        Verifier("Garage : migration des anciens points", garage.Niveau("alice") == 4 && garage.Solde("alice") == 30, "migration");
        Verifier("Garage : modele verrouille", garage.Choisir("bob", "scooter") != null && garage.Profil("bob").Modele == "municipale", "verrou");
        var vieux = new ScoresImpact(); vieux.Importer("YWxpY2U=:30:0:0:0:0:police2\nYm9i:2:0:0:0:0:origine\n");
        Verifier("Garage : ancien catalogue ramene a la Municipale", vieux.Profil("alice").Modele == "municipale" && vieux.Profil("bob").Modele == "municipale" && ScoresImpact.Vehicule("scooter") == "mbkstunt" && ScoresImpact.Vehicule("municipale") == null, "catalogue");
        Verifier("Garage : lvlup debite sans reduire niveau ou total", garage.Lvlup("ALICE") == null && garage.Solde("alice") == 27 && garage.Total("alice") == 30 && garage.Niveau("alice") == 4, "achat");
        var pa = garage.Profil("alice");
        Verifier("Garage : lvlup ameliore les 3 d'un coup", pa.Moteur == 1 && pa.Transmission == 1 && pa.Freins == 1, "uniforme");
        Verifier("Garage : rangs et prix progressifs", garage.Lvlup("alice") == null && garage.Lvlup("alice") == null && garage.Solde("alice") == 4 && ScoresImpact.Rang(garage.Profil("alice")) == 3, "prix");
        string avantGarage = garage.Exporter();
        Verifier("Garage : rang maximum sans debit", garage.Lvlup("alice") != null && garage.Exporter() == avantGarage, "maximum");
        Verifier("Garage : niveau insuffisant sans debit", garage.Lvlup("bob") != null && garage.Solde("bob") == 2, "niveau");
        var ancien = new ScoresImpact(); ancien.Importer("YWxpY2U=:30:3:1:0:2:origine\n");
        Verifier("Garage : anciens rangs separes alignes sur le plus haut", ancien.Lvlup("alice") == null && ancien.Profil("alice").Freins == 3 && ancien.Profil("alice").Moteur == 3 && ancien.Solde("alice") == 12, "migration-rangs");
        Verifier("Garage : choix modele debloque", garage.Choisir("alice", "rs3") == null && garage.Choisir("alice", "m5") != null, "modele");
        var sorts = new ScoresImpact();
        sorts.Importer("YWxpY2U=:6\nYm9i:4\n");
        Verifier("Sort : barrage debite le solde, pas le total", sorts.Depenser("alice", ScoresImpact.PRIX_BARRAGE, ScoresImpact.NIVEAU_BARRAGE) == null && sorts.Solde("alice") == 1 && sorts.Total("alice") == 6, "sort");
        Verifier("Sort : solde insuffisant sans debit", sorts.Depenser("alice", ScoresImpact.PRIX_BARRAGE, ScoresImpact.NIVEAU_BARRAGE) != null && sorts.Solde("alice") == 1, "sort-solde");
        Verifier("Sort : niveau insuffisant sans debit", sorts.Depenser("bob", ScoresImpact.PRIX_BARRAGE, ScoresImpact.NIVEAU_BARRAGE) != null && sorts.Solde("bob") == 4, "sort-niveau");
        var renfort = new Poursuite(20, 120000, 20, 6, 2);
        Verifier("Sort : boost coute 3 pts au niveau 1", ScoresImpact.PRIX_BOOST == 3 && ScoresImpact.NIVEAU_BOOST == 1, "sort-boost");
        var stock = new ScoresImpact(); stock.Importer("YWxpY2U=:10\n");
        Verifier("Achat : boost debite 3 pts et part en stock", stock.Acheter("alice", "boost") == null && stock.Solde("alice") == 7 && stock.Profil("alice").Boosts == 1, "achat-boost");
        Verifier("Achat : barrage debite 5 pts", stock.Acheter("alice", "barrage") == null && stock.Solde("alice") == 2 && stock.Profil("alice").Barrages == 1, "achat-barrage");
        Verifier("Achat : solde insuffisant ou sort inconnu sans debit", stock.Acheter("alice", "boost") != null && stock.Acheter("alice", "moteur") != null && stock.Solde("alice") == 2, "achat-refus");
        var stockRecharge = new ScoresImpact(); stockRecharge.Importer(stock.Exporter());
        Verifier("Achat : stock sauvegarde", stockRecharge.Profil("alice").Boosts == 1 && stockRecharge.Profil("alice").Barrages == 1 && stockRecharge.Solde("alice") == 2, "achat-persistance");
        Verifier("Lancer : consomme le stock sans payer", stock.Utiliser("alice", "boost") && !stock.Utiliser("alice", "boost") && stock.Solde("alice") == 2, "utiliser");
        Verifier("Sort : barrage lance la poursuite avec 3 voitures", renfort.Renforcer(0, 3) && renfort.FlicsVoulus == 3, "sort-debut");
        Verifier("Sort : barrage ajoute 3 a une poursuite en cours", !renfort.Renforcer(1000, 3) && renfort.FlicsVoulus == 6, "sort-renfort");
        var garageRecharge = new ScoresImpact(); garageRecharge.Importer(garage.Exporter());
        Verifier("Garage : achats et modele conserves", garageRecharge.Solde("alice") == 4 && garageRecharge.Profil("alice").Moteur == 3 && garageRecharge.Profil("alice").Modele == "rs3", "persistance");
        var pauvre = new ScoresImpact(); pauvre.Importer("Ym9i:5\n");
        Verifier("Garage : solde insuffisant sans debit", pauvre.Lvlup("bob") == null && pauvre.Lvlup("bob") != null && pauvre.Solde("bob") == 2, "solde");
        string avantErreur = garage.Exporter(); bool refuse = false;
        try { garage.Importer("YWxpY2U=:30\ncorrompu\n"); } catch (FormatException) { refuse = true; }
        Verifier("Garage : import invalide preserve les donnees", refuse && garage.Exporter() == avantErreur, "import atomique");

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

        // Traque : viser où le joueur SERA.
        Verifier("anticipation : 30 m à 30 m/s → 0,9 s max", Math.Abs(Regles.Anticipation(30f, 30f) - 0.9f) < 1e-4, Regles.Anticipation(30f, 30f) + " s");
        Verifier("anticipation : à l'arrêt, la vitesse plancher (10 m/s) évite la division par 0", Math.Abs(Regles.Anticipation(5f, 0f) - 0.5f) < 1e-4, Regles.Anticipation(5f, 0f) + " s");
        Verifier("anticipation : plafonnée à 0,9 s même de loin", Regles.Anticipation(500f, 20f) == 0.9f, Regles.Anticipation(500f, 20f) + " s");
        Verifier("style : de loin, suit les routes sans contourner le joueur",
            Regles.StyleConduite(100f, 40f) == Regles.CONDUITE_TRAQUE && (Regles.CONDUITE_TRAQUE & 64) != 0 && (Regles.CONDUITE_TRAQUE & 1) == 0,
            Regles.StyleConduite(100f, 40f).ToString());
        Verifier("style : au contact, ligne droite forcée", (Regles.StyleConduite(25f, 40f) & Regles.CONDUITE_LIGNE_DROITE) != 0, "");
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
        var p12 = new Poursuite(12, 60000, 12);
        for (int i = 0; i < 20; i++) p12.Cadeau(1000);
        Verifier("poursuite : 12 flics au plus (test n°5 : « 4, c'est trop peu »)", p12.FlicsVoulus == 12, p12.FlicsVoulus.ToString());
        // Semer (test n°5 : « il faut que je puisse les semer ») : un flic semé abandonne.
        var ps = new Poursuite(12, 60000, 12);
        for (int i = 0; i < 3; i++) ps.Cadeau(1000 + i);
        Verifier("semer : 3 flics, un semé → 2 à ses trousses", ps.Semer() == 2 && ps.FlicsVoulus == 2, ps.FlicsVoulus.ToString());
        ps.Semer();
        Verifier("semer : le dernier semé → chaleur 0 (poursuite gagnée)", ps.Semer() == 0 && ps.Chaleur == 0 && ps.FlicsVoulus == 0, ps.Chaleur.ToString());
        var p4 = new Poursuite(4, 60000, 5);
        for (int i = 0; i < 9; i++) p4.Cadeau(1000);
        Verifier("semer : chaleur 5 pour 4 flics max → 3 flics après un semé (pas de remplaçant)", p4.Semer() == 3 && p4.FlicsVoulus == 3,
            p4.FlicsVoulus.ToString());
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

        float cx, cy;
        ReglesSurvie.CiblePoursuite(0f, 0f, 0f, 30f, 0f, -30f, 0.5f, out cx, out cy);
        Verifier("poursuite : cible sur la trajectoire, juste après le joueur", Math.Abs(cx) < 0.01f && cy > 15f, cx + ";" + cy);
        ReglesSurvie.CiblePoursuite(0f, 0f, 30f, 0f, -30f, 0f, 0.5f, out cx, out cy);
        Verifier("poursuite : changement de direction suivi", cx > 15f && Math.Abs(cy) < 0.01f, cx + ";" + cy);
        Verifier("poursuite : voiture plus rapide que le joueur", ReglesSurvie.VitesseDePoursuite(30f) == 48f
            && ReglesSurvie.VitesseDePoursuite(0f) == 30f && ReglesSurvie.VitesseDePoursuite(80f) == 85f, "vitesse incorrecte");
        Verifier("poursuite : bonus de vitesse limité", ReglesSurvie.BonusVitesse(40f, 35f) >= 20f
            && ReglesSurvie.BonusVitesse(60f, 30f) >= 150f && ReglesSurvie.BonusVitesse(30f, 60f) == 100f
            && ReglesSurvie.BonusVitesse(90f, 30f) == 180f, "bonus incorrect");
        Verifier("patrouille : rattrapable même à basse vitesse", ReglesSurvie.VitessePatrouille(8f) < 8f && ReglesSurvie.VitessePatrouille(40f) < 40f && ReglesSurvie.VitessePatrouille(100f) <= 40f, "trop rapide");
        Verifier("patrouille : pas de demi-tour avant dépassement", !ReglesSurvie.PatrouilleDepassee(0f, 30f, 0f, 1f) && ReglesSurvie.PatrouilleDepassee(0f, -10f, 0f, 1f), "transition incorrecte");
        Verifier("patrouille : dépassement dans le sens opposé", !ReglesSurvie.PatrouilleDepassee(0f, -30f, 0f, -1f) && ReglesSurvie.PatrouilleDepassee(0f, 10f, 0f, -1f), "transition incorrecte");
        Verifier("régression session : faux dépassements à 110 et 181 m rejetés même si le policier tourne",
            !ReglesSurvie.PatrouilleDepassee(0f, 110f, 0f, -1f) && !ReglesSurvie.PatrouilleDepassee(0f, 181f, 1f, 0f), "faux dépassement");
        Verifier("patrouille : vecteur nul et voiture éloignée sur le côté rejetés",
            !ReglesSurvie.PatrouilleDepassee(0f, 5f, 0f, 0f) && !ReglesSurvie.PatrouilleDepassee(60f, 0f, 0f, 1f), "faux dépassement");
        Verifier("chasse : tâche conservée sur 120 contrôles successifs",
            ChasseContinue(), "chasse réassignée");
        Verifier("chasse : reprise après manoeuvre, cible changée ou tâche terminée",
            ReglesSurvie.ReprendreChasse(false, 10, 10, 1) && ReglesSurvie.ReprendreChasse(true, 10, 11, 1)
            && ReglesSurvie.ReprendreChasse(true, 10, 10, 7) && ReglesSurvie.ReprendreChasse(true, 10, 10, 3), "reprise manquante");
        var max = new Poursuite(20, 120000, 20, 6, 2);
        max.Cadeau(0);
        Verifier("MAX : six policiers initialement", max.FlicsVoulus == 6, "effectif incorrect");
        max.Cadeau(1000);
        Verifier("MAX : deux renforts par cadeau", max.FlicsVoulus == 8, "effectif incorrect");
        for (int i = 0; i < 20; i++) max.Cadeau(2000 + i);
        Verifier("MAX : vingt au maximum et fuite possible", max.FlicsVoulus == 20 && max.Semer() == 19, "plafond incorrect");
        Verifier("apparition latérale : route du bon côté dans le même sens",
            ReglesSurvie.PlacementLateralOk(1, 50, -20, 0, 1, 54)
            && ReglesSurvie.PlacementLateralOk(2, 50, 20, 0, 1, 54)
            && !ReglesSurvie.PlacementLateralOk(1, 50, 20, 0, 1, 54), "côté incorrect");
        Verifier("apparition latérale : refuse centre, étage, contresens et proximité",
            !ReglesSurvie.PlacementLateralOk(1, 50, -2, 0, 1, 50)
            && !ReglesSurvie.PlacementLateralOk(1, 50, -20, 10, 1, 54)
            && !ReglesSurvie.PlacementLateralOk(1, 50, -20, 0, 0, 54)
            && !ReglesSurvie.PlacementLateralOk(1, 10, -20, 0, 1, 22), "placement invalide");
        Verifier("embuscade : une dernière chance puis abandon, même sans contact",
            ReglesSurvie.DecisionEmbuscade(false, true, 0, true, true) == 1
            && ReglesSurvie.DecisionEmbuscade(true, true, 0, true, true) == 2, "chance illimitée");
        Verifier("embuscade : une à la fois et huit secondes d'espacement",
            ReglesSurvie.DecisionEmbuscade(false, true, 5000, false, true) == 0
            && ReglesSurvie.DecisionEmbuscade(false, true, 5000, true, false) == 0, "déclenchement simultané");
        Verifier("embuscade : pas de route cachée ou joueur à pied, fuite possible",
            ReglesSurvie.DecisionEmbuscade(false, true, 20000, false, false) == 2
            && ReglesSurvie.DecisionEmbuscade(false, false, 0, true, true) == 2, "attente infinie");
        Verifier("tactique : trois places distinctes puis poursuivant",
            ReglesSurvie.ChoisirRole(0, 80, false, false, false) == 1
            && ReglesSurvie.ChoisirRole(0, 80, true, false, false) == 2
            && ReglesSurvie.ChoisirRole(0, 80, true, true, false) == 3
            && ReglesSurvie.ChoisirRole(0, 80, true, true, true) == 0, "répartition incorrecte");
        Verifier("tactique : rôle stable, libéré au loin, seuils distincts",
            ReglesSurvie.ChoisirRole(2, 200, true, false, true) == 2
            && ReglesSurvie.ChoisirRole(2, 221, false, false, false) == 0
            && ReglesSurvie.ChoisirRole(0, 200, false, false, false) == 0, "oscillation");
        Verifier("tactique : contact latéral à hauteur uniquement",
            ReglesSurvie.OccasionContact(1, 3, 5, 0, 30)
            && !ReglesSurvie.OccasionContact(2, -40, 5, 0, 30)
            && !ReglesSurvie.OccasionContact(1, 3, 5, 8, 30)
            && !ReglesSurvie.OccasionContact(1, 3, 5, 0, 0), "contact mal déclenché");
        Verifier("tactique : barrage seulement devant et sur la trajectoire",
            ReglesSurvie.OccasionContact(3, 12, 2, 0, 30)
            && !ReglesSurvie.OccasionContact(3, -12, 2, 0, 30)
            && !ReglesSurvie.OccasionContact(3, 12, 20, 0, 30), "barrage mal placé");
        Verifier("tactique : modes natifs gauche droite devant",
            ReglesSurvie.ModePlacement(1) == 1 && ReglesSurvie.ModePlacement(2) == 2 && ReglesSurvie.ModePlacement(3) == 0, "modes incorrects");
        Verifier("MAX v7 : couple fort loin derrière dans le même sens",
            ReglesSurvie.CoupleRattrapage(false, 25, 40, 180, 0.9f, true, true) == 1.8f
            && ReglesSurvie.CoupleRattrapage(false, 25, 40, 80, 0.9f, true, true) == 1.6f, "aide absente");
        Verifier("MAX v7 : couple refusé hors conditions sûres",
            ReglesSurvie.CoupleRattrapage(false, 25, 40, 80, 0.5f, true, true) == 1f
            && ReglesSurvie.CoupleRattrapage(false, 25, 40, 80, 0.9f, false, true) == 1f
            && ReglesSurvie.CoupleRattrapage(false, 25, 40, 80, 0.9f, true, false) == 1f
            && ReglesSurvie.CoupleRattrapage(false, 25, 40, 5, 0.9f, true, true) == 1f
            && ReglesSurvie.CoupleRattrapage(true, 25, 40, 80, 0.9f, true, true) == 1f, "aide dangereuse");
        Verifier("NATIF v2 : policiers, SWAT et armée reconnus pour le désarmement",
            ReglesSurvie.EstPoliceNative(6) && ReglesSurvie.EstPoliceNative(27)
            && ReglesSurvie.EstPoliceNative(29) && !ReglesSurvie.EstPoliceNative(4), "type incorrect");
        Verifier("MAX v9 : police native monte progressivement de deux à cinq étoiles",
            ReglesSurvie.NiveauPoliceNative(0) == 0
            && ReglesSurvie.NiveauPoliceNative(6) == 2
            && ReglesSurvie.NiveauPoliceNative(7) == 3
            && ReglesSurvie.NiveauPoliceNative(11) == 4
            && ReglesSurvie.NiveauPoliceNative(16) == 5, "niveau natif incorrect");
        Verifier("MAX v8 : interception après trois secondes loin derrière et hors caméra",
            ReglesSurvie.DoitIntercepter(false, true, true, 180f, 3000), "interception absente");
        Verifier("MAX v8 : jamais visible, trop proche, devant, à pied ou trop tôt",
            !ReglesSurvie.DoitIntercepter(true, true, true, 180f, 3000)
            && !ReglesSurvie.DoitIntercepter(false, true, true, 140f, 3000)
            && !ReglesSurvie.DoitIntercepter(false, true, false, 180f, 3000)
            && !ReglesSurvie.DoitIntercepter(false, false, true, 180f, 3000)
            && !ReglesSurvie.DoitIntercepter(false, true, true, 180f, 2999), "interception visible ou prématurée");
        Verifier("route : rejette voie parallèle, étage, demi-tour et rupture", ReglesSurvie.ContinuiteRoute(20f, 2f, 1f, 0.99f)
            && !ReglesSurvie.ContinuiteRoute(20f, 12f, 0f, 1f) && !ReglesSurvie.ContinuiteRoute(20f, 0f, 9f, 1f)
            && !ReglesSurvie.ContinuiteRoute(20f, 0f, 0f, -1f) && !ReglesSurvie.ContinuiteRoute(60f, 0f, 0f, 1f), "route invalide acceptée");
        Verifier("poursuite : ralentit pour tourner", ReglesSurvie.VitesseEnVirage(45f, 0f, 1f, 1f, 0f) == 24f
            && ReglesSurvie.VitesseEnVirage(45f, 0f, 1f, 0f, -1f) == 16f, "virage incorrect");
        Verifier("nom de viewer : lisible de près, discret de loin", ReglesSurvie.EchelleNom(10f) == 1f
            && ReglesSurvie.EchelleNom(30f) < 0.7f && ReglesSurvie.EchelleNom(85f) == 0.30f, "échelle incorrecte");
        Verifier("apparition hors champ : acceptée", ReglesSurvie.ApparitionAcceptee(false, false, 40f, 60f, 120f), "refusée");
        Verifier("apparition dans le champ, cachée par le décor à 70 m : acceptée", ReglesSurvie.ApparitionAcceptee(true, true, 70f, 60f, 120f), "refusée");
        Verifier("apparition dans le champ, cachée mais à 40 m : refusée", !ReglesSurvie.ApparitionAcceptee(true, true, 40f, 60f, 120f), "acceptée");
        Verifier("apparition dans le champ, visible à 80 m : refusée", !ReglesSurvie.ApparitionAcceptee(true, false, 80f, 60f, 120f), "acceptée");
        Verifier("apparition devant, visible mais au loin (150 m) : acceptée", ReglesSurvie.ApparitionAcceptee(true, false, 150f, 60f, 120f), "refusée");
        // Caps GTA : 0 = nord (+Y), 90 = ouest (-X), 180 = sud, 270 = est.
        Verifier("cap vers le nord = 0", Math.Abs(ReglesSurvie.CapVers(0f, 0f, 0f, 50f)) < 0.01f, ReglesSurvie.CapVers(0f, 0f, 0f, 50f).ToString());
        Verifier("cap vers l'ouest = 90", Math.Abs(ReglesSurvie.CapVers(0f, 0f, -50f, 0f) - 90f) < 0.01f, ReglesSurvie.CapVers(0f, 0f, -50f, 0f).ToString());
        Verifier("cap vers l'est = 270", Math.Abs(ReglesSurvie.CapVers(0f, 0f, 50f, 0f) - 270f) < 0.01f, ReglesSurvie.CapVers(0f, 0f, 50f, 0f).ToString());
        // Route nord-sud, joueur au sud du flic : le flic doit regarder le sud, quel que soit le sens rendu par le jeu.
        Verifier("sens de la route : retourné vers le joueur (0 → 180)", Math.Abs(ReglesSurvie.CapDeRouteVers(0f, 0f, 100f, 0f, 0f) - 180f) < 0.01f,
            ReglesSurvie.CapDeRouteVers(0f, 0f, 100f, 0f, 0f).ToString());
        Verifier("sens de la route : déjà vers le joueur, gardé (180)", Math.Abs(ReglesSurvie.CapDeRouteVers(180f, 0f, 100f, 0f, 0f) - 180f) < 0.01f,
            ReglesSurvie.CapDeRouteVers(180f, 0f, 100f, 0f, 0f).ToString());

        // Champ de la caméra : lacet 0 = elle regarde le nord (+Y), lacet 90 = l'ouest.
        Verifier("caméra : droit devant, dans le champ", ReglesSurvie.DansLeChamp(0f, 0f, 0f, 0f, 0f, 0f, 80f, 0f, 60f), "hors champ");
        Verifier("caméra : derrière, hors champ", !ReglesSurvie.DansLeChamp(0f, 0f, 0f, 0f, 0f, 0f, -80f, 0f, 60f), "dans le champ");
        Verifier("caméra : pile sur le côté, hors champ", !ReglesSurvie.DansLeChamp(0f, 0f, 0f, 0f, 0f, 80f, 0f, 0f, 60f), "dans le champ");
        Verifier("caméra : tournée à l'ouest, l'ouest est dans le champ", ReglesSurvie.DansLeChamp(0f, 0f, 0f, 0f, 90f, -80f, 0f, 0f, 60f), "hors champ");
        Verifier("caméra : tournée à l'ouest, le nord est hors champ", !ReglesSurvie.DansLeChamp(0f, 0f, 0f, 0f, 90f, 0f, 80f, 0f, 60f), "dans le champ");

        // Recycler un flic (le « ping out / ping in » de Driver 2) : planté ou retourné, jamais sous ses yeux.
        // RaisonDeRecycler(visible, plantée ms, retourné/à l'eau, plantée max). Semé : il abandonne (plus bas).
        Verifier("recyclage : jamais à la vue du joueur", ReglesSurvie.RaisonDeRecycler(true, 99999, true, 2500) == null, "recyclé");
        Verifier("recyclage : planté 2,5 s hors champ", ReglesSurvie.RaisonDeRecycler(false, 2500, false, 2500) == "planté",
            ReglesSurvie.RaisonDeRecycler(false, 2500, false, 2500));
        Verifier("recyclage : retourné ou à l'eau hors champ", ReglesSurvie.RaisonDeRecycler(false, 0, true, 2500) == "retourné ou à l'eau",
            ReglesSurvie.RaisonDeRecycler(false, 0, true, 2500));
        Verifier("recyclage : en pleine chasse, non", ReglesSurvie.RaisonDeRecycler(false, 1000, false, 2500) == null, "recyclé");
        // Semé (test n°5 : « il faut que je puisse les semer ») : loin derrière (150 m) ou très loin
        // partout (250 m), hors de vue ; au bout de 4 s (Survie), il abandonne.
        Verifier("semé : 160 m derrière, hors de vue", ReglesSurvie.EstSeme(false, 160f, true, 150f, 250f), "non");
        Verifier("semé : 160 m derrière mais à l'écran → non", !ReglesSurvie.EstSeme(true, 160f, true, 150f, 250f), "semé");
        Verifier("semé : 100 m derrière → non, il peut encore revenir", !ReglesSurvie.EstSeme(false, 100f, true, 150f, 250f), "semé");
        Verifier("semé : 170 m DEVANT → non, il arrive sur lui", !ReglesSurvie.EstSeme(false, 170f, false, 150f, 250f), "semé");
        Verifier("semé : 260 m, même devant → oui", ReglesSurvie.EstSeme(false, 260f, false, 150f, 250f), "non");
        // Seul un flic venu au contact peut être semé ; un flic jamais croisé
        // continue la poursuite depuis une autre route.
        Verifier("loin : venu au contact → semé", ReglesSurvie.SortDuFlicLoin(false, 160f, true, true, 150f, 250f) == "semé",
            ReglesSurvie.SortDuFlicLoin(false, 160f, true, true, 150f, 250f));
        Verifier("loin : jamais venu au contact → perdu de vue (reposé ailleurs)",
            ReglesSurvie.SortDuFlicLoin(false, 160f, true, false, 150f, 250f) == "perdu de vue",
            ReglesSurvie.SortDuFlicLoin(false, 160f, true, false, 150f, 250f));
        Verifier("loin : pas assez loin → rien", ReglesSurvie.SortDuFlicLoin(false, 100f, true, true, 150f, 250f) == null,
            ReglesSurvie.SortDuFlicLoin(false, 100f, true, true, 150f, 250f));
        // « SEMÉS ! » — sans police du jeu : nos flics tous semés. Avec (« casser la vue »,
        // 2026-09-24) : nos flics ne lâchent plus, plus une étoile suffit.
        // Fin de poursuite : survécu ou attrapé, les étoiles du jeu sont effacées (la poursuite est
        // finie) ; mort : le jeu s'en charge ; mission, changement de personnage : on n'y touche pas.

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
