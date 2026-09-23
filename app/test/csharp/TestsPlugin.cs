/*
 * Harnais de tests de la LOGIQUE PURE du plugin (scripts/GTAMax.cs).
 * Compilé avec /define:TESTS_SANS_JEU par app/test/plugin.test.js : la classe
 * Script (qui dépend du jeu) est exclue, tout le reste est testé ici, sans GTA V.
 * Sortie : une ligne « OK <nom> » ou « ECHEC <nom> : <détail> » par test ;
 * code de sortie 0 si tout passe. C# 5 (csc du .NET Framework).
 */
using System;
using System.Collections.Generic;
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

    static int Main()
    {
        Console.OutputEncoding = new UTF8Encoding(false);
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
        Verifier("distance : 20 m à l'arrêt", Regles.DistanceApparition(20f, 0f, 60f) == 20f, "");
        Verifier("distance : +1 s de trajet en roulant", Regles.DistanceApparition(20f, 15f, 60f) == 35f, "");
        Verifier("distance : plafonnée à 60 m", Regles.DistanceApparition(20f, 80f, 60f) == 60f, "");
        Verifier("distance : jamais sous la base (marche arrière)", Regles.DistanceApparition(20f, -10f, 60f) == 20f, "");

        return echecs == 0 ? 0 : 1;
    }
}
