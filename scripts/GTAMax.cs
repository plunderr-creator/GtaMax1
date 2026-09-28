/* Plugin survie GTA V : consommation atomique des commandes et poursuites.
 * Compilation C# 5 via app/outils/plugin.js, logique pure testée sans le jeu. */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
#if !TESTS_SANS_JEU
using GTA;
using GTA.Math;
using GTA.Native;
#endif

namespace GTAMax
{
    // ═════════════════════════════════════════════════════════════════════
    //  LOGIQUE PURE — sans le jeu, testée par app/test/plugin.test.js
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Une commande lue dans la file.</summary>
    public sealed class Commande
    {
        public string Verbe;
        public Dictionary<string, string> Args;
        public long T;   // horodatage posé par le serveur (ms epoch) ; 0 = ligne écrite à la main

        public string Arg(string cle)
        {
            string v;
            return (Args != null && Args.TryGetValue(cle, out v)) ? v : "";
        }

        public override string ToString()
        {
            var sb = new StringBuilder(Verbe);
            if (Args != null) foreach (var kv in Args) sb.Append(' ').Append(kv.Key).Append('=').Append(kv.Value);
            return sb.ToString();
        }
    }

    /// <summary>Lecture du format de la file : VERBE t=… clé=valeur…</summary>
    public static class LecteurFile
    {
        // Rend null pour une ligne vide ou un commentaire (#) — erreur reste null —,
        // null avec une erreur pour une ligne invalide.
        public static Commande ParserLigne(string ligne, out string erreur)
        {
            erreur = null;
            string s = ligne == null ? "" : ligne.Trim();
            if (s.Length == 0 || s[0] == '#') return null;
            string[] morceaux = s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            string verbe = morceaux[0];
            if (!VerbeValide(verbe)) { erreur = "verbe invalide « " + verbe + " »"; return null; }
            var args = new Dictionary<string, string>(StringComparer.Ordinal);
            long t = 0;
            for (int i = 1; i < morceaux.Length; i++)
            {
                int egal = morceaux[i].IndexOf('=');
                if (egal <= 0) { erreur = "argument sans « = » : " + morceaux[i]; return null; }
                string cle = morceaux[i].Substring(0, egal);
                string valeur = Uri.UnescapeDataString(morceaux[i].Substring(egal + 1));
                if (cle == "t")
                {
                    if (!long.TryParse(valeur, NumberStyles.Integer, CultureInfo.InvariantCulture, out t))
                    { erreur = "horodatage illisible « " + valeur + " »"; return null; }
                }
                else args[cle] = valeur;
            }
            return new Commande { Verbe = verbe, Args = args, T = t };
        }

        static bool VerbeValide(string v)
        {
            if (v.Length == 0 || v[0] < 'A' || v[0] > 'Z') return false;
            foreach (char c in v)
                if (!((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_')) return false;
            return true;
        }

        // Tout un fichier : lignes invalides et commandes périmées journalisées puis
        // jetées. Une ligne sans t= (écrite à la main pour tester) est gardée.
        public static List<Commande> ParserContenu(string contenu, long maintenantMs, long ttlMs, Action<string> log)
        {
            var sortie = new List<Commande>();
            if (string.IsNullOrEmpty(contenu)) return sortie;
            foreach (string ligne in contenu.Split('\n'))
            {
                string erreur;
                Commande c = ParserLigne(ligne, out erreur);
                if (c == null)
                {
                    if (erreur != null && log != null) log("Ligne ignorée (" + erreur + ") : " + ligne.Trim());
                    continue;
                }
                if (c.T > 0 && maintenantMs - c.T > ttlMs)
                {
                    if (log != null) log("Commande périmée ignorée (" + ((maintenantMs - c.T) / 1000) + " s) : " + c);
                    continue;
                }
                sortie.Add(c);
            }
            return sortie;
        }
    }

    /// <summary>
    /// Côté plugin du protocole de la file : PREND le fichier du serveur par un
    /// rename (atomique), le lit, le supprime, puis rend les commandes fraîches.
    /// Le fichier est supprimé AVANT que les commandes soient jouées : jamais deux fois.
    /// </summary>
    public static class ConsommateurFile
    {
        public const string FICHIER = "gtamax_queue.txt";
        public const string FICHIER_EN_COURS = "gtamax_queue.en-cours";

        // null = rien à lire, ou fichier verrouillé (antivirus, rename du serveur en
        // cours) : on réessaiera au tick suivant. Un .en-cours resté d'un essai
        // interrompu passe en premier.
        public static List<Commande> Prendre(string dossier, long maintenantMs, long ttlMs, Action<string> log)
        {
            string enCours = Path.Combine(dossier, FICHIER_EN_COURS);
            string file = Path.Combine(dossier, FICHIER);
            if (!File.Exists(enCours))
            {
                if (!File.Exists(file)) return null;      // cas normal : serveur éteint ou aucun don
                try { File.Move(file, enCours); }         // on PREND le fichier : le serveur peut en écrire un nouveau
                catch (IOException) { return null; }
                catch (UnauthorizedAccessException) { return null; }
            }
            string contenu;
            try
            {
                contenu = File.ReadAllText(enCours, Encoding.UTF8);
                File.Delete(enCours);
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            return LecteurFile.ParserContenu(contenu, maintenantMs, ttlMs, log);
        }
    }

    /// <summary>Petites règles de calcul, sorties du jeu pour être testées.</summary>
    public static class Regles
    {
        // Liste de modèles de l'ini : virgules/points-virgules/espaces, minuscules, sans doublon.
        public static List<string> ListeModeles(string texte)
        {
            var sortie = new List<string>();
            foreach (string brut in (texte ?? "").Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string m = brut.Trim().ToLowerInvariant();
                if (m.Length > 0 && !sortie.Contains(m)) sortie.Add(m);
            }
            return sortie;
        }

        // TRAQUE — leçons des tests en jeu du 2026-09-23 : la mission RAM freinait
        // les voitures (0-27 km/h), la poursuite du jeu (TASK_VEHICLE_CHASE) les
        // laissait plantées, et « rouler vers la position du joueur » réémis toutes
        // les 4 s s'arrêtait sur une position périmée. On vise donc l'endroit où le
        // joueur SERA, et l'ordre est redonné toutes les 0,5 s.

        // Anticipation (secondes) : le temps qu'il faut au policier pour couvrir la
        // distance, borné à 0,9 s pour ne pas viser trop loin après un virage.
        public static float Anticipation(float distanceM, float vitesseChasseurMs)
        {
            return Math.Min(0.9f, Math.Max(0f, distanceM) / Math.Max(10f, vitesseChasseurMs));
        }

        // Style de conduite : de base, contourne voitures et obstacles, prend les
        // voies d'en face, change de file, ne s'arrête ni aux feux ni pour personne,
        // et NE contourne PAS le joueur (bit 64). Au contact (< 40 m), ligne droite
        // forcée (16777216) : il fonce dessus au lieu de suivre la route.
        public const int CONDUITE_TRAQUE = 4 | 32 | 64 | 512 | 262144 | 524288;
        public const int CONDUITE_LIGNE_DROITE = 16777216;
        public static int StyleConduite(float distanceM, float distanceLigneDroiteM)
        {
            return distanceM < distanceLigneDroiteM ? (CONDUITE_TRAQUE | CONDUITE_LIGNE_DROITE) : CONDUITE_TRAQUE;
        }

        // Plantée = ne roule pas alors qu'elle est loin du joueur. Au contact, être à
        // l'arrêt est normal : elle vient de percuter.
        public static bool EstPlantee(float vitesseMs, float distanceM, float vitesseMin, float distanceContact)
        {
            return vitesseMs < vitesseMin && distanceM > distanceContact;
        }

        // Un point (relatif au joueur : dx, dy) est-il derrière lui ? Produit scalaire
        // avec la direction où il regarde (ou roule).
        public static bool EstDerriere(float dx, float dy, float avantX, float avantY)
        {
            return dx * avantX + dy * avantY < 0f;
        }

        public static int Borne(int valeur, int min, int max) { return Math.Min(max, Math.Max(min, valeur)); }
        public static float Borne(float valeur, float min, float max) { return Math.Min(max, Math.Max(min, valeur)); }
    }

    /// <summary>Journal texte (scripts\GTAMax.log, rotation simple). N'échoue jamais.</summary>
    public sealed class Journal
    {
        readonly string chemin;
        readonly long tailleMax;
        readonly Dictionary<string, DateTime> dernieres = new Dictionary<string, DateTime>();
        readonly object verrou = new object();

        public Journal(string chemin, long tailleMax) { this.chemin = chemin; this.tailleMax = tailleMax; }

        public void Info(string message) { Ecrire("INFO", message); }
        public void Erreur(string message) { Ecrire("ERREUR", message); }

        // Une erreur qui se répète à chaque tick (100 ms) noierait le journal :
        // une seule ligne par clé toutes les 10 s.
        public void ErreurLimitee(string cle, string message)
        {
            lock (verrou)
            {
                DateTime avant;
                if (dernieres.TryGetValue(cle, out avant) && (DateTime.UtcNow - avant).TotalSeconds < 10) return;
                dernieres[cle] = DateTime.UtcNow;
            }
            Ecrire("ERREUR", message);
        }

        void Ecrire(string niveau, string message)
        {
            try
            {
                lock (verrou)
                {
                    var fi = new FileInfo(chemin);
                    if (fi.Exists && fi.Length > tailleMax)
                    {
                        string ancien = chemin + ".1";
                        if (File.Exists(ancien)) File.Delete(ancien);
                        File.Move(chemin, ancien);
                    }
                    File.AppendAllText(chemin,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " [" + niveau + "] " + message + Environment.NewLine,
                        new UTF8Encoding(false));
                }
            }
            catch (Exception) { /* un journal qui échoue ne doit jamais faire tomber le script */ }
        }
    }

#if !TESTS_SANS_JEU
    public class GTAMax : Script
    {
        const long TTL_MS = 60000;
        const int FLAG_PEUT_TRAVERSER_PARE_BRISE = 32;
        internal const string PED_POLICE = "s_m_y_sheriff_01";
        internal const int CONDUITE_NORMALE = 786603;
        readonly Journal journal;
        readonly string dossierScripts, dossierJeu;
        readonly Survie survie;

        public GTAMax()
        {
            dossierScripts = BaseDirectory;
            dossierJeu = Path.GetDirectoryName(dossierScripts.TrimEnd('\\', '/'));
            journal = new Journal(Path.Combine(dossierScripts, "GTAMax.log"), 512 * 1024);
            var modeles = Regles.ListeModeles(Reglage("POLICE", "MODELES", "pranger"));
            if (modeles.Count == 0) modeles.Add("pranger");
            survie = new Survie(journal, dossierScripts, GereParEls, modeles,
                Reglage("POLICE", "PED", PED_POLICE),
                Regles.Borne(Reglage("SURVIE", "MAX_FLICS", 20), 1, 20),
                Regles.Borne(Reglage("SURVIE", "DUREE_S", 120), 10, 600),
                Regles.Borne(Reglage("SURVIE", "DISTANCE_M", 60f), 40f, 200f),
                Reglage("SURVIE", "INDESTRUCTIBLES", 1) != 0);
            Interval = 100;
            Tick += SurTick;
            Aborted += delegate { survie.Liberer(); };
            journal.Info("Plugin GTA Max chargé — survie, poursuite directe et bonus de vitesse.");
        }

        T Reglage<T>(string section, string cle, T defaut)
        {
            try { return Settings.GetValue<T>(section, cle, defaut); }
            catch (Exception ex)
            {
                journal.ErreurLimitee("ini-" + cle, "Réglage " + cle + " ignoré : " + ex.Message);
                return defaut;
            }
        }

        void SurTick(object sender, EventArgs e)
        {
            try
            {
                var lot = ConsommateurFile.Prendre(dossierScripts, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), TTL_MS, journal.Info);
                if (lot != null) foreach (Commande c in lot)
                {
                    try
                    {
                        if (c.Verbe == "SPAWN_POLICE")
                        {
                            int points; int.TryParse(c.Arg("points"), out points);
                            survie.Cadeau(c.Arg("donateur"), c.Arg("cadeau"), points, c.Arg("voiture"));
                        }
                        else if (c.Verbe == "GARAGE") survie.Garage(c.Arg("donateur"), c.Arg("action"), c.Arg("valeur"));
                        else journal.Info("Commande inconnue ignorée : " + c.Verbe);
                    }
                    catch (Exception ex) { journal.ErreurLimitee("commande", "Commande ignorée : " + ex); }
                }
            }
            catch (Exception ex) { journal.ErreurLimitee("file", "Lecture de la file : " + ex); }
            try { survie.Tick(); }
            catch (Exception ex) { journal.ErreurLimitee("survie", "Poursuite : " + ex); }
        }

        internal static bool JoueurDisponible()
        {
            Ped joueur = Game.Player.Character;
            if (joueur == null || !joueur.Exists() || joueur.IsDead) return false;
            if (Function.Call<bool>(Hash.GET_IS_LOADING_SCREEN_ACTIVE)) return false;
            if (Function.Call<bool>(Hash.IS_CUTSCENE_ACTIVE)) return false;
            if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS)) return false;
            return true;
        }

        bool GereParEls(string modele)
        {
            try
            {
                // ELS n'agit que si le jeu charge son .asi, donc posé à la racine du jeu.
                if (!File.Exists(Path.Combine(dossierJeu, "ELS.asi"))) return false;
                return File.Exists(Path.Combine(Path.Combine(Path.Combine(dossierJeu, "ELS"), "pack_default"), modele + ".xml"));
            }
            catch (Exception) { return false; }
        }

        internal static void PreparerAgent(Ped p)
        {
            if (p == null || !p.Exists()) return;
            p.BlockPermanentEvents = true;                     // ne réagit à rien (tirs, collisions) : ne fuit pas, ne descend pas se battre
            Function.Call(Hash.SET_PED_KEEP_TASK, p, true);    // garde sa tâche, même une fois rendu au jeu
            Function.Call(Hash.SET_DRIVER_ABILITY, p, 1f);
            Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, p, 1f);
            Function.Call(Hash.SET_DRIVER_RACING_MODIFIER, p, 1f);                               // prend les virages comme un pilote
            Function.Call(Hash.SET_PED_CONFIG_FLAG, p, FLAG_PEUT_TRAVERSER_PARE_BRISE, false);   // il percute : qu'il ne passe pas par le pare-brise
        }

        internal static void LacherAgent(Ped a)
        {
            if (a == null || !a.Exists()) return;
            a.BlockPermanentEvents = false;
            Function.Call(Hash.SET_PED_KEEP_TASK, a, false);
            a.MarkAsNoLongerNeeded();
        }
    }
#endif
}
