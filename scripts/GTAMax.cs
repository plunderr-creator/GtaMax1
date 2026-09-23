/*
 * GTAMax.cs — plugin GTA V de GTA Max (ScriptHookVDotNet v3).
 *
 * Lit les commandes que le serveur GTA Max dépose dans scripts\gtamax_queue.txt
 * et les exécute en jeu. En v1, une seule action : SPAWN_POLICE (un cadeau
 * TikTok = la police prend le joueur en chasse ; le MODE de GTAMax.ini dit
 * comment — par défaut le mode « survie » façon Driver 2, dans Survie.cs).
 *
 * COMPILATION : « Installer plugin GTA.bat » (app/outils/plugin.js) compile ce
 * fichier et Survie.cs en scripts\GTAMax.dll avec le csc du .NET Framework 4.8,
 * contre ScriptHookVDotNet3.dll. Ce csc ne connaît que C# 5 : pas de $"", pas
 * de ?., pas de nameof, pas d'initialiseur de propriété automatique.
 * Les tests (app/test/plugin.test.js) compilent ces mêmes fichiers avec
 * /define:TESTS_SANS_JEU : seule la LOGIQUE PURE (en haut) est alors compilée,
 * sans le jeu ni SHVDN.
 *
 * PROTOCOLE DE LA FILE (voir app/gta-bridge.js) :
 *   - le serveur CRÉE gtamax_queue.txt d'un coup (tmp complet + rename) ;
 *   - le plugin le CONSOMME : rename en gtamax_queue.en-cours (atomique ; fichier
 *     verrouillé = on réessaie au tick suivant), lecture, suppression, puis
 *     exécution. Jamais de fichier lu à moitié, jamais de commande jouée deux fois.
 *   - une ligne = VERBE t=<ms epoch> clé=valeur… (valeurs encodées façon URL) ;
 *     une commande de plus de 60 s est jetée : on ne rattrape jamais.
 *
 * AJOUTER UNE ACTION EN JEU = une méthode void MonAction(Commande c) et une ligne
 * registre.Enregistrer("MON_VERBE", MonAction) dans le constructeur. Côté serveur :
 * envoyerCommande('MON_VERBE', { cle: valeur }). Un verbe inconnu est journalisé
 * et ignoré, jamais une erreur.
 *
 * RÈGLE D'OR : ce script ne doit JAMAIS faire tomber ni figer le jeu. Chaque étape
 * du tick est sous try/catch (une exception non attrapée ferait décharger le script
 * par SHVDN), aucun appel bloquant (les modèles se chargent sur plusieurs ticks), et
 * sans serveur GTA Max il n'y a simplement pas de fichier : rien ne se passe.
 */
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

    /// <summary>Registre verbe → action. Ajouter une action = une entrée, pas un switch.</summary>
    public sealed class RegistreActions
    {
        readonly Dictionary<string, Action<Commande>> actions = new Dictionary<string, Action<Commande>>(StringComparer.Ordinal);

        public void Enregistrer(string verbe, Action<Commande> action) { actions[verbe] = action; }

        public IEnumerable<string> Verbes { get { return actions.Keys; } }

        // true si l'action a tourné sans lever. Verbe inconnu ou action qui lève :
        // une ligne de journal, jamais une exception qui remonte dans le tick.
        public bool Executer(Commande c, Action<string> log)
        {
            Action<Commande> action;
            if (c == null) return false;
            if (!actions.TryGetValue(c.Verbe, out action))
            {
                if (log != null) log("Verbe inconnu « " + c.Verbe + " » — ignoré");
                return false;
            }
            try { action(c); return true; }
            catch (Exception e)
            {
                if (log != null) log("Action " + c.Verbe + " en erreur (ignorée) : " + e);
                return false;
            }
        }
    }

    /// <summary>
    /// File d'apparitions : une sortie toutes les intervalleMs au plus, plafond de
    /// demandes en attente, demandes trop vieilles jetées (pas de rattrapage).
    /// Le temps est celui du jeu (Game.GameTime) : il s'arrête avec le jeu.
    /// </summary>
    public sealed class FileSpawn<T>
    {
        sealed class Entree { public T Valeur; public long RecueA; }

        readonly Queue<Entree> attente = new Queue<Entree>();
        readonly long intervalleMs, ageMaxMs;
        readonly int maxEnAttente;
        long prochaineA = long.MinValue;

        public FileSpawn(long intervalleMs, int maxEnAttente, long ageMaxMs)
        {
            this.intervalleMs = Math.Max(0, intervalleMs);
            this.maxEnAttente = Math.Max(1, maxEnAttente);
            this.ageMaxMs = Math.Max(1, ageMaxMs);
        }

        public int Count { get { return attente.Count; } }

        public bool Ajouter(T valeur, long maintenantMs)
        {
            if (attente.Count >= maxEnAttente) return false;
            attente.Enqueue(new Entree { Valeur = valeur, RecueA = maintenantMs });
            return true;
        }

        // Jette les demandes plus vieilles que ageMaxMs ; rend combien.
        public int Purger(long maintenantMs)
        {
            int n = 0;
            while (attente.Count > 0 && maintenantMs - attente.Peek().RecueA > ageMaxMs) { attente.Dequeue(); n++; }
            return n;
        }

        public bool Pret(long maintenantMs) { return attente.Count > 0 && maintenantMs >= prochaineA; }

        // Sort la plus ancienne demande et arme le délai jusqu'à la suivante.
        public T Prendre(long maintenantMs)
        {
            Entree e = attente.Dequeue();
            prochaineA = maintenantMs + intervalleMs;
            return e.Valeur;
        }
    }

    /// <summary>Unités vivantes, de la plus ancienne à la plus récente, avec plafond.</summary>
    public sealed class Parc<T>
    {
        readonly List<T> unites = new List<T>();
        readonly int max;

        public Parc(int max) { this.max = Math.Max(1, max); }

        public int Count { get { return unites.Count; } }
        public int Max { get { return max; } }

        // Copie : on peut retirer des unités pendant qu'on parcourt la liste rendue.
        public List<T> Unites { get { return new List<T>(unites); } }

        // Sort les plus anciennes jusqu'à laisser la place à UNE nouvelle unité.
        public List<T> FaireDeLaPlace()
        {
            var sortantes = new List<T>();
            while (unites.Count >= max) { sortantes.Add(unites[0]); unites.RemoveAt(0); }
            return sortantes;
        }

        public void Ajouter(T unite) { unites.Add(unite); }

        // Sort les unités qui vérifient la condition (évaluée UNE fois par unité).
        public List<T> Retirer(Predicate<T> condition)
        {
            var sortantes = new List<T>();
            var restantes = new List<T>();
            foreach (T u in unites) (condition(u) ? sortantes : restantes).Add(u);
            unites.Clear();
            unites.AddRange(restantes);
            return sortantes;
        }
    }

    /// <summary>Ce que fait un cadeau (GTAMax.ini, [MODE] MODE).</summary>
    public enum ModeJeu
    {
        Survie,     // façon Driver 2 : une meute de flics du script à semer (Survie.cs)
        Etoiles,    // étoiles de recherche : la vraie police du jeu, sans tirs ni hélicos
        Voitures,   // une voiture de police du script par cadeau
        LesDeux,    // étoiles + voitures
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
        // joueur SERA, et l'ordre est redonné chaque seconde.

        // Anticipation (secondes) : le temps qu'il faut au policier pour couvrir la
        // distance, borné à 1,5 s. Au contact, presque rien : on vise le joueur lui-même.
        public static float Anticipation(float distanceM, float vitesseChasseurMs)
        {
            return Math.Min(1.5f, Math.Max(0f, distanceM) / Math.Max(10f, vitesseChasseurMs));
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

        // Le policier regarde-t-il le joueur (à cosMin près) ? Produit scalaire entre
        // son « avant » et la direction du joueur, tous deux ramenés au plan.
        public static bool EstFaceA(float avantX, float avantY, float versX, float versY, float cosMin)
        {
            double na = Math.Sqrt(avantX * avantX + avantY * avantY), nv = Math.Sqrt(versX * versX + versY * versY);
            if (na < 1e-6 || nv < 1e-6) return false;
            return (avantX * versX + avantY * versY) / (na * nv) >= cosMin;
        }

        // MODE de GTAMax.ini : « survie », « etoiles », « voitures » ou « les_deux »
        // (casse, accents, espaces et tirets ignorés). Inconnu → survie, le défaut.
        public static ModeJeu LireMode(string texte)
        {
            string m = (texte ?? "").Trim().ToLowerInvariant()
                .Replace("é", "e").Replace("è", "e").Replace(" ", "_").Replace("-", "_");
            if (m == "etoiles") return ModeJeu.Etoiles;
            if (m == "voitures") return ModeJeu.Voitures;
            if (m == "les_deux") return ModeJeu.LesDeux;
            return ModeJeu.Survie;
        }

        // Étoiles visées après un cadeau : on part du plus haut entre le niveau
        // actuel et la cible en cours, on ajoute, on plafonne à max — et on ne
        // baisse jamais (5 étoiles gagnées en jouant restent 5 étoiles).
        public static int EtoilesVisees(int niveauActuel, int viseesAvant, int parCadeau, int max)
        {
            int depart = Math.Max(Math.Max(0, niveauActuel), Math.Max(0, viseesAvant));
            return Math.Max(depart, Math.Min(Math.Max(0, max), depart + Math.Max(0, parCadeau)));
        }

        // Ped de police au sens du jeu (GET_PED_TYPE) : COP 6, SWAT 27, ARMY 29.
        public static bool EstPolice(int typePed) { return typePed == 6 || typePed == 27 || typePed == 29; }

        // Semée = loin du joueur ET hors de l'écran : on peut la faire réapparaître
        // derrière lui sans qu'il la voie disparaître.
        public static bool EstSemee(float distanceM, bool visible, float distanceMin)
        {
            return distanceM > distanceMin && !visible;
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
    // ═════════════════════════════════════════════════════════════════════
    //  LE SCRIPT — dépend du jeu (ScriptHookVDotNet v3)
    // ═════════════════════════════════════════════════════════════════════

    sealed class DemandeSpawn
    {
        public string Donateur;
        public string Cadeau;
        public bool Renfort;         // mode étoiles : la voiture est confiée à l'IA de poursuite du jeu
    }

    sealed class ChargementEnCours
    {
        public DemandeSpawn Demande;
        public string Nom;
        public Model Voiture;
        public Model Agent;
        public int DebutA;
    }

    sealed class UnitePolice
    {
        public Vehicle Voiture;
        public Ped Conducteur;       // un seul policier par voiture
        public int ModeleHash;       // garde-fou : un handle recyclé par le jeu n'est plus « notre » voiture
        public string Nom;
        public string Donateur;
        public int NeeA;             // Game.GameTime de l'apparition
        public int MorteA;           // Game.GameTime de la mort de la voiture (0 = vivante)
        public int LoinDepuis;       // Game.GameTime depuis lequel elle est distancée (0 = au contact)
        public int SemeeDepuis;      // Game.GameTime depuis lequel elle est semée (loin et hors écran)
        public int Rattrapages;      // réapparitions derrière le joueur déjà faites
        public int ImmobileDepuis;   // Game.GameTime depuis lequel elle ne roule plus loin du joueur (0 = elle roule)
        public bool DiagnostiqueFait;
        public bool Renfort;         // policier du jeu (TASK_COMBAT_PED), pas notre traque
    }

    public class GTAMax : Script
    {
        const long TTL_MS = 60000;                 // même péremption que le serveur
        const long AGE_MAX_DEMANDE_MS = 60000;     // une demande qui attend (joueur mort, cinématique) n'est pas rattrapée après
        const int CHARGEMENT_MAX_MS = 5000;        // modèle qui ne se charge pas : on abandonne cette apparition
        const int EPAVE_MS = 8000;                 // une épave reste visible 8 s puis disparaît
        const int ENTRETIEN_MS = 1000;             // suivi des poursuites et du nettoyage
        const float DISTANCE_LIGNE_DROITE_M = 40f; // plus près que ça, elle fonce en ligne droite sur le joueur
        const float DISTANCE_COUP_M = 15f;
        const float DISTANCE_COMBAT_RENFORT_M = 60f; // renfort : plus près, l'IA police du jeu prend le relais         // plus près que ça, face à lui : coup d'accélérateur pour percuter
        const float COS_FACE = 0.87f;              // « face au joueur » : à 30° près
        const float ELAN_COUP_MS = 6f;             // le coup d'accélérateur : sa vitesse + 6 m/s (≈ 22 km/h)
        const float DISTANCE_SEMEE_M = 120f;       // plus loin que ça, hors écran…
        const int SEMEE_MS = 2000;                 // …depuis 2 s : elle réapparaît derrière le joueur
        const float RECUL_RATTRAPAGE_M = 15f;      // la réapparition se fait un peu plus loin que l'apparition (moins visible)
        const int IMMOBILE_MS = 3000;              // plantée 3 s loin du joueur et hors écran : elle réapparaît derrière lui
        const float VITESSE_IMMOBILE = 2f;         // m/s : en dessous, elle ne roule pas
        const float DISTANCE_CONTACT_M = 12f;      // au contact du joueur, être à l'arrêt est normal (elle vient de percuter)
        const int DIAGNOSTIC_MS = 5000;            // une ligne de diagnostic 5 s après l'apparition
        const float DISTANCE_LACHER_M = 250f;      // distancée à plus de 250 m…
        const int LOIN_MS = 15000;                 // …pendant 15 s : la poursuite est perdue, la voiture est lâchée
        const float PLAFOND_DISTANCE_M = 60f;
        internal const string PED_POLICE = "s_m_y_cop_01";  // tenue Police Nationale posée sur ce ped
        const string MODELES_DEFAUT = "pranger";
        internal const float VITESSE_TRAQUE = 60f;          // m/s demandés : c'est la voiture qui plafonne
        internal const int CONDUITE_NORMALE = 786603;       // voiture rendue au jeu : balade tranquille
        const int FLAG_PEUT_TRAVERSER_PARE_BRISE = 32;   // SET_PED_CONFIG_FLAG : coupé, le policier reste au volant après un choc

        readonly Journal journal;
        readonly RegistreActions registre = new RegistreActions();
        readonly Random hasard = new Random();
        readonly string dossierScripts;
        readonly string dossierJeu;

        FileSpawn<DemandeSpawn> fileSpawn;
        Parc<UnitePolice> parc;
        List<string> modeles;
        bool modelesVerifies;
        float distanceM;
        float bonusMoteur;       // % de puissance et de vitesse de pointe en plus (MODIFY_VEHICLE_TOP_SPEED)
        int rattrapagesMax;      // réapparitions derrière le joueur par voiture semée

        // Mode « survie » (défaut) : tout est dans Survie.cs ; null dans les autres modes.
        Survie survie;

        // Mode « étoiles » : un cadeau = une étoile de recherche, la vraie police du jeu.
        bool modeEtoiles, modeVoitures;
        int etoilesParCadeau, etoilesMax, tenueMs;
        bool tirsAutorises, helicosAutorises;
        int etoilesVisees;       // niveau que les cadeaux imposent (0 = aucun)
        int tenueJusquA;         // Game.GameTime : jusque-là, la police sait toujours où est le joueur
        bool renfortParCadeau;   // mode étoiles : une voiture apparaît aussi derrière le joueur
        float distanceRenfortsM; // distance idéale des renforts du jeu (SET_DISPATCH_IDEAL_SPAWN_DISTANCE)
        readonly HashSet<int> voituresBoostees = new HashSet<int>();
        int prochainDesarmement, prochaineTenue, prochainDispatch, prochainBoost;
        bool dispatchModifie;
        ChargementEnCours chargement;
        int prochainEntretien;

        public GTAMax()
        {
            dossierScripts = BaseDirectory;
            dossierJeu = Path.GetDirectoryName(dossierScripts.TrimEnd('\\', '/'));
            journal = new Journal(Path.Combine(dossierScripts, "GTAMax.log"), 512 * 1024);

            ChargerReglages();
            registre.Enregistrer("SPAWN_POLICE", SpawnPolice);
            // ← les actions futures s'enregistrent ici, une ligne chacune.

            Interval = 100;
            Tick += SurTick;
            Aborted += SurArret;
            journal.Info("Plugin GTA Max chargé — actions : " + string.Join(", ", registre.Verbes)
                + " — file : " + Path.Combine(dossierScripts, ConsommateurFile.FICHIER));
        }

        void ChargerReglages()
        {
            string liste = MODELES_DEFAUT, mode = "survie";
            int intervalle = 3000, maxVoitures = 8, maxAttente = 20, rattrapages = 3;
            int parCadeau = 1, maxEtoiles = 3, tenueS = 45, tirs = 0, helicos = 0, renfort = 1;
            int maxFlics = 4, dureeS = 60, indestructibles = 1;
            float distanceRenforts = 0f, rayonSurvie = 60f;
            float distance = 20f, bonus = 40f;
            try
            {
                mode        = Settings.GetValue<string>("MODE", "MODE", mode);
                maxFlics    = Settings.GetValue<int>("SURVIE", "MAX_FLICS", maxFlics);
                dureeS      = Settings.GetValue<int>("SURVIE", "DUREE_S", dureeS);
                rayonSurvie = Settings.GetValue<float>("SURVIE", "DISTANCE_M", rayonSurvie);
                indestructibles = Settings.GetValue<int>("SURVIE", "INDESTRUCTIBLES", indestructibles);
                parCadeau   = Settings.GetValue<int>("ETOILES", "PAR_CADEAU", parCadeau);
                maxEtoiles  = Settings.GetValue<int>("ETOILES", "MAX", maxEtoiles);
                tenueS      = Settings.GetValue<int>("ETOILES", "TENUE_S", tenueS);
                tirs        = Settings.GetValue<int>("ETOILES", "TIRS", tirs);
                helicos     = Settings.GetValue<int>("ETOILES", "HELICOS", helicos);
                renfort     = Settings.GetValue<int>("ETOILES", "RENFORT", renfort);
                distanceRenforts = Settings.GetValue<float>("ETOILES", "DISTANCE_RENFORTS_M", distanceRenforts);
                liste       = Settings.GetValue<string>("POLICE", "MODELES", liste);
                distance    = Settings.GetValue<float>("POLICE", "DISTANCE_M", distance);
                intervalle  = Settings.GetValue<int>("POLICE", "INTERVALLE_MS", intervalle);
                maxVoitures = Settings.GetValue<int>("POLICE", "MAX_VOITURES", maxVoitures);
                maxAttente  = Settings.GetValue<int>("POLICE", "MAX_EN_ATTENTE", maxAttente);
                bonus       = Settings.GetValue<float>("POLICE", "BONUS_MOTEUR", bonus);
                rattrapages = Settings.GetValue<int>("POLICE", "RATTRAPAGES", rattrapages);
            }
            catch (Exception e) { journal.Erreur("GTAMax.ini illisible, réglages par défaut : " + e.Message); }

            modeles = Regles.ListeModeles(liste);
            if (modeles.Count == 0) modeles = Regles.ListeModeles(MODELES_DEFAUT);
            distanceM = Regles.Borne(distance, 5f, PLAFOND_DISTANCE_M);
            bonusMoteur = Regles.Borne(bonus, 0f, 200f);
            rattrapagesMax = Regles.Borne(rattrapages, 0, 20);
            // Toujours créées (le tick les parcourt) ; vides hors du mode « voitures ».
            fileSpawn = new FileSpawn<DemandeSpawn>(Regles.Borne(intervalle, 500, 60000), Regles.Borne(maxAttente, 1, 200), AGE_MAX_DEMANDE_MS);
            parc = new Parc<UnitePolice>(Regles.Borne(maxVoitures, 1, 30));

            ModeJeu modeJeu = Regles.LireMode(mode);
            modeEtoiles = modeJeu == ModeJeu.Etoiles || modeJeu == ModeJeu.LesDeux;
            modeVoitures = modeJeu == ModeJeu.Voitures || modeJeu == ModeJeu.LesDeux;
            if (modeJeu == ModeJeu.Survie)
            {
                maxFlics = Regles.Borne(maxFlics, 1, 6);
                dureeS = Regles.Borne(dureeS, 10, 600);
                rayonSurvie = Regles.Borne(rayonSurvie, 40f, 200f);
                survie = new Survie(journal, dossierScripts, GereParEls, modeles, maxFlics, dureeS, rayonSurvie, bonusMoteur, indestructibles != 0);
                journal.Info("Mode : survie (façon Driver 2) — " + maxFlics + " flics max, 1 de plus par cadeau, poursuite jusqu'à "
                    + dureeS + " s après le dernier cadeau ; joueur rapide : flics DEVANT sur sa route ; sinon à " + rayonSurvie + " m hors de vue ; flics "
                    + (indestructibles != 0 ? "indestructibles" : "destructibles") + ", moteur +" + bonusMoteur + " %, modèles " + string.Join(", ", modeles));
                return;
            }
            etoilesParCadeau = Regles.Borne(parCadeau, 1, 5);
            etoilesMax = Regles.Borne(maxEtoiles, 1, 5);
            tenueMs = Regles.Borne(tenueS, 0, 600) * 1000;
            tirsAutorises = tirs != 0;
            helicosAutorises = helicos != 0;
            renfortParCadeau = renfort != 0;
            // 0 = dispatch du jeu intact (défaut depuis le crash du 2026-09-23, 2 s après
            // un renfort à 3 étoiles avec renforts à 60 m et cadence ×4).
            distanceRenfortsM = distanceRenforts <= 0f ? 0f : Regles.Borne(distanceRenforts, 30f, 300f);
            journal.Info("Mode : " + (modeEtoiles && modeVoitures ? "les deux" : modeEtoiles ? "étoiles" : "voitures")
                + (modeEtoiles ? " — +" + etoilesParCadeau + " étoile(s) par cadeau, " + etoilesMax + " max, tenue " + (tenueMs / 1000)
                    + " s, police " + (tirsAutorises ? "armée" : "SANS armes") + ", hélicos " + (helicosAutorises ? "oui" : "NON")
                    + (distanceRenfortsM > 0f ? ", renforts du jeu à " + distanceRenfortsM + " m (cadence ×4)" : ", dispatch du jeu intact") + (renfortParCadeau ? ", + 1 renfort derrière par cadeau" : "") : ""));
            journal.Info("Réglages : modèles " + string.Join(", ", modeles) + " ; " + distanceM + " m derrière ; 1 voiture / "
                + Regles.Borne(intervalle, 500, 60000) + " ms ; " + parc.Max + " max ; moteur +" + bonusMoteur + " % ; "
                + rattrapagesMax + " réapparition(s) si semée");
        }

        static long MaintenantEpochMs() { return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); }

        // ─── TICK ─────────────────────────────────────────────────────────
        void SurTick(object sender, EventArgs e)
        {
            try { LireFile(); }
            catch (Exception ex) { journal.ErreurLimitee("file", "Lecture de la file : " + ex); }

            try { AvancerApparition(); }
            catch (Exception ex) { journal.ErreurLimitee("spawn", "Apparition : " + ex); AbandonnerChargement(); }

            try
            {
                if (Game.GameTime >= prochainEntretien)
                {
                    prochainEntretien = Game.GameTime + ENTRETIEN_MS;
                    Entretenir();
                }
            }
            catch (Exception ex) { journal.ErreurLimitee("entretien", "Entretien des poursuites : " + ex); }

            if (modeEtoiles)
            {
                try { EntretenirEtoiles(); }
                catch (Exception ex) { journal.ErreurLimitee("etoiles", "Mode étoiles : " + ex); }
            }

            if (survie != null)
            {
                try { survie.Tick(); }
                catch (Exception ex) { journal.ErreurLimitee("survie", "Mode survie : " + ex); }
            }
        }

        // Script déchargé (fermeture du jeu, rechargement SHVDN) : on lâche tout ce
        // qu'on suivait, rien ne reste marqué « à nous », et le jeu retrouve ses
        // hélicoptères de police.
        void SurArret(object sender, EventArgs e)
        {
            try
            {
                AbandonnerChargement();
                if (parc != null) foreach (UnitePolice u in parc.Retirer(delegate (UnitePolice x) { return true; })) Lacher(u);
                if (survie != null) survie.Liberer();
                if (dispatchModifie)
                {
                    Function.Call(Hash.ENABLE_DISPATCH_SERVICE, DISPATCH_HELICO_POLICE, true);
                    Function.Call(Hash.ENABLE_DISPATCH_SERVICE, DISPATCH_HELICO_SWAT, true);
                    Function.Call(Hash.RESET_DISPATCH_IDEAL_SPAWN_DISTANCE);
                    Function.Call(Hash.SET_DISPATCH_TIME_BETWEEN_SPAWN_ATTEMPTS_MULTIPLIER, 1f);
                }
                journal.Info("Plugin GTA Max arrêté.");
            }
            catch (Exception) { }
        }

        // ─── FILE ─────────────────────────────────────────────────────────
        void LireFile()
        {
            List<Commande> lot = ConsommateurFile.Prendre(dossierScripts, MaintenantEpochMs(), TTL_MS, journal.Info);
            if (lot == null) return;
            foreach (Commande c in lot)
            {
                journal.Info("Commande : " + c);
                registre.Executer(c, journal.Erreur);
            }
        }

        // ─── SPAWN_POLICE ─────────────────────────────────────────────────
        // Le serveur ne sait pas COMMENT la police arrive : c'est le MODE de
        // GTAMax.ini qui décide (survie façon Driver 2, étoiles de recherche,
        // voitures du script, ou étoiles + voitures).
        void SpawnPolice(Commande c)
        {
            if (survie != null) { survie.Cadeau(c.Arg("donateur"), c.Arg("cadeau")); return; }
            if (modeEtoiles) AjouterEtoiles(c.Arg("donateur"));
            // Mode étoiles seul : la police du jeu arrivait de très loin (test du
            // 2026-09-23). Un renfort apparaît donc derrière le joueur, confié à l'IA du jeu.
            bool renfort = !modeVoitures;
            if (renfort && (!renfortParCadeau || etoilesVisees == 0)) return;
            var d = new DemandeSpawn { Donateur = c.Arg("donateur"), Cadeau = c.Arg("cadeau"), Renfort = renfort };
            if (fileSpawn.Ajouter(d, Game.GameTime))
                journal.Info("Voiture de police en file (" + fileSpawn.Count + " en attente) — " + d.Donateur + " / " + d.Cadeau);
            else
                journal.Info("File des voitures pleine : cadeau de " + d.Donateur + " sans voiture");
        }

        // ─── MODE ÉTOILES ─────────────────────────────────────────────────
        // Un cadeau = une étoile de recherche : c'est la VRAIE police du jeu qui
        // poursuit (dispatch.meta : voitures pranger Police Nationale jusqu'à 3
        // étoiles), avec ses vraies manœuvres (percussions, PIT, barrages). Trois
        // réglages en font un jeu de cadeaux : police sans armes (elle ne tire
        // pas, elle percute et arrête), pas d'hélicoptères, et une TENUE — tant
        // qu'elle dure, la police sait toujours où est le joueur.
        const int DISPATCH_HELICO_POLICE = 2, DISPATCH_HELICO_SWAT = 12;   // ENABLE_DISPATCH_SERVICE
        const float DISPATCH_CADENCE = 0.25f;      // renforts du jeu 4× plus fréquents
        const int CA_UTILISE_VEHICULE = 1, CA_POURSUIT_A_PIED = 21, CA_ATTAQUE_EN_VEHICULE = 52;   // SET_PED_COMBAT_ATTRIBUTES
        const int DESARMEMENT_MS = 500, TENUE_VERIF_MS = 1000, DISPATCH_VERIF_MS = 5000;
        const float RAYON_DESARMEMENT_M = 300f;
        const int ARME_A_FEU_OU_EXPLOSIF = 6;      // IS_PED_ARMED : 2 explosifs + 4 armes à feu (la matraque reste)

        void AjouterEtoiles(string donateur)
        {
            if (!JoueurDisponible()) { journal.Info("Étoile de " + donateur + " ignorée : joueur mort, en chargement ou en cinématique"); return; }
            if (Function.Call<bool>(Hash.GET_MISSION_FLAG)) { journal.Info("Étoile de " + donateur + " ignorée : mission en cours (on ne casse pas la mission)"); return; }
            int actuel = Function.Call<int>(Hash.GET_PLAYER_WANTED_LEVEL, Game.Player.Handle);
            etoilesVisees = Regles.EtoilesVisees(actuel, etoilesVisees, etoilesParCadeau, etoilesMax);
            if (actuel < etoilesVisees) AppliquerEtoiles(etoilesVisees);
            tenueJusquA = Game.GameTime + tenueMs;
            Function.Call(Hash.REPORT_POLICE_SPOTTED_PLAYER, Game.Player.Handle);
            journal.Info("Étoiles : " + actuel + " → " + Math.Max(actuel, etoilesVisees) + " (cadeau de " + donateur
                + "), la police sait où est le joueur pendant " + (tenueMs / 1000) + " s");
        }

        static void AppliquerEtoiles(int n)
        {
            Function.Call(Hash.SET_PLAYER_WANTED_LEVEL, Game.Player.Handle, n, false);
            Function.Call(Hash.SET_PLAYER_WANTED_LEVEL_NOW, Game.Player.Handle, false);
        }

        void EntretenirEtoiles()
        {
            int maintenant = Game.GameTime;

            // Hélicoptères : coupés dans le dispatch, revérifié toutes les 5 s (une
            // mission ou un autre script peut les rallumer), rendus à l'arrêt du script.
            // Renforts du jeu : plus près et plus souvent (au test, la police
            // arrivait de très loin et la poursuite était trop facile).
            if (maintenant >= prochainDispatch)
            {
                prochainDispatch = maintenant + DISPATCH_VERIF_MS;
                if (!helicosAutorises)
                {
                    Function.Call(Hash.ENABLE_DISPATCH_SERVICE, DISPATCH_HELICO_POLICE, false);
                    Function.Call(Hash.ENABLE_DISPATCH_SERVICE, DISPATCH_HELICO_SWAT, false);
                }
                if (distanceRenfortsM > 0f)
                {
                    Function.Call(Hash.SET_DISPATCH_IDEAL_SPAWN_DISTANCE, distanceRenfortsM);
                    Function.Call(Hash.SET_DISPATCH_TIME_BETWEEN_SPAWN_ATTEMPTS_MULTIPLIER, DISPATCH_CADENCE);
                }
                dispatchModifie = true;
            }

            Ped joueur = Game.Player.Character;
            if (joueur == null || !joueur.Exists()) return;

            // Police sans armes : toutes les 0,5 s, tout policier armé à moins de
            // 300 m perd ses armes à feu. Ceux que le jeu vient de faire apparaître
            // n'ont donc jamais le temps de tirer.
            if (!tirsAutorises && maintenant >= prochainDesarmement)
            {
                prochainDesarmement = maintenant + DESARMEMENT_MS;
                foreach (Ped p in World.GetNearbyPeds(joueur, RAYON_DESARMEMENT_M))
                {
                    if (p == null || !p.Exists() || p.IsDead) continue;
                    if (!Regles.EstPolice(Function.Call<int>(Hash.GET_PED_TYPE, p))) continue;
                    if (Function.Call<bool>(Hash.IS_PED_ARMED, p, ARME_A_FEU_OU_EXPLOSIF))
                        Function.Call(Hash.REMOVE_ALL_PED_WEAPONS, p, true);
                }
            }
            if (maintenant >= prochainBoost)
            {
                prochainBoost = maintenant + DESARMEMENT_MS;
                BoosterPoliceDuJeu(joueur);
            }

            // Tenue : tant qu'elle dure, les étoiles ne descendent pas sous la cible
            // et la police revoit le joueur dès que ses étoiles grisent. Mort ou
            // arrêté : le jeu a effacé les étoiles, la tenue s'arrête avec.
            if (etoilesVisees > 0 && maintenant >= prochaineTenue)
            {
                prochaineTenue = maintenant + TENUE_VERIF_MS;
                if (joueur.IsDead || Function.Call<bool>(Hash.IS_PLAYER_BEING_ARRESTED, Game.Player.Handle, true))
                {
                    journal.Info("Joueur mort ou arrêté : fin de la tenue des étoiles");
                    etoilesVisees = 0; tenueJusquA = 0;
                }
                else if (maintenant >= tenueJusquA)
                {
                    journal.Info("Fin de la tenue : le joueur peut semer la police");
                    etoilesVisees = 0;
                }
                else
                {
                    if (Function.Call<int>(Hash.GET_PLAYER_WANTED_LEVEL, Game.Player.Handle) < etoilesVisees) AppliquerEtoiles(etoilesVisees);
                    if (Function.Call<bool>(Hash.ARE_PLAYER_STARS_GREYED_OUT, Game.Player.Handle))
                        Function.Call(Hash.REPORT_POLICE_SPOTTED_PLAYER, Game.Player.Handle);
                }
            }
        }

        // Voitures de police du jeu autour du joueur : pilotes au maximum, moteur
        // relevé comme nos voitures, carrosserie renforcée. Une fois par voiture.
        void BoosterPoliceDuJeu(Ped joueur)
        {
            if (voituresBoostees.Count > 500) voituresBoostees.Clear();
            foreach (Vehicle v in World.GetNearbyVehicles(joueur, RAYON_DESARMEMENT_M))
            {
                if (v == null || !v.Exists() || v.IsDead || voituresBoostees.Contains(v.Handle)) continue;
                Ped c = v.Driver;
                if (c == null || !c.Exists() || c.IsDead || c == joueur) continue;
                if (!Regles.EstPolice(Function.Call<int>(Hash.GET_PED_TYPE, c))) continue;
                voituresBoostees.Add(v.Handle);
                if (bonusMoteur > 0f) Function.Call(Hash.MODIFY_VEHICLE_TOP_SPEED, v, bonusMoteur);
                Function.Call(Hash.SET_VEHICLE_STRONG, v, true);
                Function.Call(Hash.SET_DRIVER_ABILITY, c, 1f);
                Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, c, 1f);
                Function.Call(Hash.SET_DRIVER_RACING_MODIFIER, c, 1f);
            }
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

        // Machine à états sur plusieurs ticks : demande des modèles, puis création
        // quand ils sont chargés. Jamais d'attente bloquante dans le tick.
        void AvancerApparition()
        {
            int maintenant = Game.GameTime;
            if (chargement == null)
            {
                int jetees = fileSpawn.Purger(maintenant);
                if (jetees > 0) journal.Info(jetees + " demande(s) de voiture de plus de 60 s jetée(s)");
                if (!fileSpawn.Pret(maintenant) || !JoueurDisponible()) return;
                if (!modelesVerifies) VerifierModeles();
                if (modeles.Count == 0) { fileSpawn.Prendre(maintenant); journal.Erreur("Aucun modèle de police valide dans GTAMax.ini"); return; }

                DemandeSpawn demande = fileSpawn.Prendre(maintenant);
                string nom = modeles[hasard.Next(modeles.Count)];
                var voiture = new Model(nom);
                var agent = new Model(PED_POLICE);
                voiture.Request();
                agent.Request();
                chargement = new ChargementEnCours { Demande = demande, Nom = nom, Voiture = voiture, Agent = agent, DebutA = maintenant };
                return;
            }
            if (!chargement.Voiture.IsLoaded || !chargement.Agent.IsLoaded)
            {
                if (maintenant - chargement.DebutA > CHARGEMENT_MAX_MS)
                {
                    journal.Info("Chargement de « " + chargement.Nom + " » trop long : voiture abandonnée");
                    AbandonnerChargement();
                }
                return;
            }
            ChargementEnCours c = chargement;
            chargement = null;
            try { Apparaitre(c); }
            finally { c.Voiture.MarkAsNoLongerNeeded(); c.Agent.MarkAsNoLongerNeeded(); }
        }

        // Un modèle absent du jeu (faute de frappe dans l'ini) est écarté une fois
        // pour toutes, au premier spawn — pas pendant le chargement du script.
        void VerifierModeles()
        {
            modelesVerifies = true;
            var valides = new List<string>();
            foreach (string nom in modeles)
            {
                var m = new Model(nom);
                if (m.IsInCdImage && m.IsVehicle) valides.Add(nom);
                else journal.Erreur("Modèle « " + nom + " » absent du jeu ou pas un véhicule — ignoré");
            }
            modeles = valides;
        }

        void AbandonnerChargement()
        {
            if (chargement == null) return;
            try { chargement.Voiture.MarkAsNoLongerNeeded(); chargement.Agent.MarkAsNoLongerNeeded(); }
            catch (Exception) { }
            chargement = null;
        }

        void Apparaitre(ChargementEnCours c)
        {
            Ped joueur = Game.Player.Character;
            if (joueur == null || !joueur.Exists()) return;
            Vector3 position = PointDApparition(joueur);
            if (c.Demande.Renfort && !SurRouteDerriere(joueur, position))
            {
                journal.Info("Renfort pour " + c.Demande.Donateur + " : pas de route derrière le joueur, pas d'apparition");
                return;
            }
            float cap = (joueur.Position - position).ToHeading();   // derrière lui, orientée vers lui : elle fonce dans son dos
            Vehicle vj = joueur.IsInVehicle() ? joueur.CurrentVehicle : null;
            float vitesseJoueur = vj != null ? vj.Speed : 0f;

            // Natives plutôt que les enveloppes SHVDN : World.CreateVehicle et
            // GetGroundHeight sont « obsolètes » dans les nightlies et leurs
            // remplaçantes n'existent pas en version stable. Une native, elle,
            // ne bouge pas d'une version de SHVDN à l'autre.
            Vehicle v = Function.Call<Vehicle>(Hash.CREATE_VEHICLE, c.Voiture.Hash, position.X, position.Y, position.Z, cap, false, false, false);
            if (v == null || v.Handle == 0 || !v.Exists()) { journal.Erreur("Le jeu a refusé de créer « " + c.Nom + " »"); return; }
            Function.Call<bool>(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, v, 5f);
            Function.Call(Hash.FREEZE_ENTITY_POSITION, v, false);
            Function.Call(Hash.SET_ENTITY_LOAD_COLLISION_FLAG, v, true);   // jamais figée en attendant le sol
            Function.Call(Hash.SET_VEHICLE_HANDBRAKE, v, false);
            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, v, true, true, false);
            // Trop lentes au test : la voiture de police de base (pranger) est un 4x4
            // poussif. Moteur + vitesse de pointe relevés, carrosserie renforcée pour
            // encaisser ses propres percussions.
            if (bonusMoteur > 0f) Function.Call(Hash.MODIFY_VEHICLE_TOP_SPEED, v, bonusMoteur);
            Function.Call(Hash.SET_VEHICLE_STRONG, v, true);

            // Un seul policier par voiture, au volant.
            Ped conducteur = v.CreatePedOnSeat(VehicleSeat.Driver, c.Agent);
            if (c.Demande.Renfort) PreparerRenfort(conducteur); else PreparerAgent(conducteur);
            bool auVolant = conducteur != null && conducteur.Exists() && conducteur.IsInVehicle(v);
            if (!auVolant) journal.Erreur("« " + c.Nom + " » : le policier n'a pas pu prendre le volant (siège conducteur du modèle ?)");

            // Le joueur roule : la voiture part lancée un peu plus vite que lui, sinon
            // elle démarrerait à l'arrêt 20 m derrière et ne le rattraperait jamais.
            Lancer(v, vitesseJoueur);

            // Gyrophares : si ELS est chargé ET a un VCF pour ce modèle, il prend la
            // main dès que la sirène est active (on ne touche pas au mute : ELS joue
            // ses propres tonalités). Sinon, feux vanilla : sirène active ET audible.
            bool els = GereParEls(c.Nom);
            v.IsSirenActive = true;
            if (!els) v.IsSirenSilent = false;

            var u = new UnitePolice
            {
                Voiture = v, Conducteur = conducteur,
                ModeleHash = v.Model.Hash, Nom = c.Nom, Donateur = c.Demande.Donateur,
                NeeA = Game.GameTime, Renfort = c.Demande.Renfort,
            };
            DonnerOrdre(u, joueur);

            // Voiture et policier restent « à nous » (persistants) TANT QU'ILS
            // POURSUIVENT : marqués non persistants dès l'apparition, le jeu les
            // effaçait 15 à 80 s plus tard dès qu'ils passaient hors champ derrière
            // le joueur (constaté en jeu le 2026-09-23). Ils sont rendus au jeu
            // (Lacher) quand ils sont distancés, à la mort du joueur ou à l'arrêt du
            // script ; épaves et plafond les suppriment. Rien ne fuit.

            // Plafond : la plus ancienne ne part qu'une fois la nouvelle bien créée
            // (un refus du jeu ne doit pas faire disparaître une voiture pour rien).
            foreach (UnitePolice vieille in parc.FaireDeLaPlace())
            {
                journal.Info("Plafond de " + parc.Max + " voitures : la plus ancienne (" + vieille.Nom + ") est retirée");
                Supprimer(vieille);
            }
            parc.Ajouter(u);
            journal.Info((u.Renfort ? "Renfort « " : "Voiture « ") + c.Nom + " » apparue derrière le joueur pour " + (c.Demande.Donateur.Length > 0 ? c.Demande.Donateur : "?")
                + " (" + parc.Count + "/" + parc.Max + ", " + Math.Round(position.DistanceTo(joueur.Position)) + " m, au volant : "
                + (auVolant ? "oui" : "NON") + ", gyrophares " + (els ? "ELS" : "vanilla") + ")");
        }

        static void Lancer(Vehicle v, float vitesseJoueur)
        {
            if (vitesseJoueur > 3f) Function.Call(Hash.SET_VEHICLE_FORWARD_SPEED, v, Math.Min(vitesseJoueur * 1.15f, 60f));
        }

        // DERRIÈRE le joueur, à `recul` mètres, sur la route la plus proche : elle
        // arrive dans son dos. Une route trouvée devant lui (virage, carrefour) est
        // refusée ; repli sur le sol derrière lui s'il n'y a pas de route correcte.
        Vector3 PointDApparition(Ped joueur) { return PointDApparition(joueur, distanceM); }

        Vector3 PointDApparition(Ped joueur, float recul)
        {
            Vehicle vj = joueur.IsInVehicle() ? joueur.CurrentVehicle : null;
            Vector3 avant = vj != null ? vj.ForwardVector : joueur.ForwardVector;
            Vector3 cible = joueur.Position - avant * recul;

            Vector3 route = World.GetNextPositionOnStreet(cible, true);
            float ecart = route.DistanceTo(joueur.Position);
            Vector3 rel = route - joueur.Position;
            if (route != Vector3.Zero && ecart >= 8f && ecart <= PLAFOND_DISTANCE_M
                && Regles.EstDerriere(rel.X, rel.Y, avant.X, avant.Y)) return route;

            var sol = new OutputArgument();
            bool trouve = Function.Call<bool>(Hash.GET_GROUND_Z_FOR_3D_COORD, cible.X, cible.Y, cible.Z + 10f, sol, false, false);
            return new Vector3(cible.X, cible.Y, trouve ? sol.GetResult<float>() : cible.Z);
        }

        static bool SurRouteDerriere(Ped joueur, Vector3 position)
        {
            Vehicle vj = joueur.IsInVehicle() ? joueur.CurrentVehicle : null;
            Vector3 avant = vj != null ? vj.ForwardVector : joueur.ForwardVector;
            Vector3 rel = position - joueur.Position;
            return Function.Call<bool>(Hash.IS_POINT_ON_ROAD, position.X, position.Y, position.Z, 0)
                && Regles.EstDerriere(rel.X, rel.Y, avant.X, avant.Y);
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

        // Renfort (mode étoiles) : un vrai policier du jeu, qui poursuit le joueur
        // recherché avec l'IA de poursuite de la police (percussions, PIT) ;
        // sans arme, le combat en voiture = foncer dedans.
        void PreparerRenfort(Ped p)
        {
            if (p == null || !p.Exists()) return;
            Function.Call(Hash.SET_PED_AS_COP, p, true);
            Function.Call(Hash.SET_DRIVER_ABILITY, p, 1f);
            Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, p, 1f);
            Function.Call(Hash.SET_DRIVER_RACING_MODIFIER, p, 1f);
            Function.Call(Hash.SET_PED_CONFIG_FLAG, p, FLAG_PEUT_TRAVERSER_PARE_BRISE, false);
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, p, CA_UTILISE_VEHICULE, true);
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, p, CA_ATTAQUE_EN_VEHICULE, true);
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, p, CA_POURSUIT_A_PIED, true);
            if (!tirsAutorises) Function.Call(Hash.REMOVE_ALL_PED_WEAPONS, p, true);
        }

        // TRAQUE, redonnée à chaque entretien (1 s) : rouler vers l'endroit où le
        // joueur SERA (position + vitesse × anticipation), à fond, sans le
        // contourner ; au contact (< 40 m), en ligne droite. Face à lui à moins de
        // 15 m et pas plus vite que lui : coup d'accélérateur pour percuter.
        void DonnerOrdre(UnitePolice u, Ped joueur)
        {
            Ped c = u.Conducteur;
            // Renfort : loin (> 60 m) et en voiture, notre traque (elle roule vite
            // droit sur lui) ; près, ou à pied, l'IA de la police du jeu (percussions,
            // arrestation). Au test, le combat seul conduisait mal de loin.
            bool auVolantRenfort = c != null && c.Exists() && !c.IsDead && u.Voiture.Exists() && c.IsInVehicle(u.Voiture);
            if (u.Renfort && (!auVolantRenfort || u.Voiture.Position.DistanceTo(joueur.Position) <= DISTANCE_COMBAT_RENFORT_M))
            {
                if (c != null && c.Exists() && !c.IsDead && !Function.Call<bool>(Hash.IS_PED_IN_COMBAT, c, joueur))
                    Function.Call(Hash.TASK_COMBAT_PED, c, joueur, 0, 16);
                if (auVolantRenfort) CoupDAccelerateur(u, joueur);
                return;
            }
            if (c == null || !c.Exists() || c.IsDead || !u.Voiture.Exists() || !c.IsInVehicle(u.Voiture)) return;
            Vehicle vj = joueur.IsInVehicle() ? joueur.CurrentVehicle : null;
            Vector3 vitesseJoueur = vj != null ? vj.Velocity : joueur.Velocity;
            Vector3 versJoueur = joueur.Position - u.Voiture.Position;
            float distance = versJoueur.Length();
            float anticipe = Regles.Anticipation(distance, u.Voiture.Speed);
            Vector3 vise = joueur.Position + vitesseJoueur * anticipe;
            Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, c, u.Voiture, vise.X, vise.Y, vise.Z, VITESSE_TRAQUE, 1,
                u.ModeleHash, Regles.StyleConduite(distance, DISTANCE_LIGNE_DROITE_M), 0.5f, 1f);

            CoupDAccelerateur(u, joueur);
        }

        // Face au joueur à moins de 15 m et pas plus vite que lui : coup d'accélérateur pour percuter.
        static void CoupDAccelerateur(UnitePolice u, Ped joueur)
        {
            Vehicle vj = joueur.IsInVehicle() ? joueur.CurrentVehicle : null;
            Vector3 vitesseJoueur = vj != null ? vj.Velocity : joueur.Velocity;
            Vector3 versJoueur = joueur.Position - u.Voiture.Position;
            float distance = versJoueur.Length();
            float vitesseCible = vitesseJoueur.Length();
            Vector3 avant = u.Voiture.ForwardVector;
            if (distance < DISTANCE_COUP_M && u.Voiture.Speed < vitesseCible + 3f
                && Regles.EstFaceA(avant.X, avant.Y, versJoueur.X, versJoueur.Y, COS_FACE))
                Function.Call(Hash.SET_VEHICLE_FORWARD_SPEED, u.Voiture, Math.Min(vitesseCible + ELAN_COUP_MS, 60f));
        }

        // ─── ENTRETIEN (1 fois par seconde) ───────────────────────────────
        void Entretenir()
        {
            if (parc.Count == 0) return;
            Ped joueur = Game.Player.Character;
            int maintenant = Game.GameTime;

            // 1. Unités que le jeu a déjà ramassées (ou dont le handle sert désormais à autre chose).
            foreach (UnitePolice u in parc.Retirer(delegate (UnitePolice x) { return !x.Voiture.Exists() || x.Voiture.Model.Hash != x.ModeleHash; }))
                journal.Info("Voiture « " + u.Nom + " » ramassée par le jeu");

            // 2. Joueur mort ou arrêté : fin de poursuite, tout le monde est lâché.
            bool joueurHorsJeu = joueur == null || !joueur.Exists() || joueur.IsDead
                || Function.Call<bool>(Hash.IS_PLAYER_BEING_ARRESTED, Game.Player.Handle, true);
            if (joueurHorsJeu)
            {
                List<UnitePolice> toutes = parc.Retirer(delegate (UnitePolice x) { return true; });
                if (toutes.Count > 0) journal.Info("Joueur mort ou arrêté : fin de poursuite, " + toutes.Count + " voiture(s) lâchée(s)");
                foreach (UnitePolice u in toutes) Lacher(u);
                return;
            }

            // 3. Le joueur a pris une de nos voitures : elle n'est plus une unité de police.
            foreach (UnitePolice u in parc.Retirer(delegate (UnitePolice x) { return joueur.IsInVehicle(x.Voiture); }))
            {
                journal.Info("Le joueur a pris la voiture « " + u.Nom + " » : elle n'est plus suivie");
                Lacher(u);
            }

            // 4. Épaves : visibles quelques secondes, puis supprimées.
            foreach (UnitePolice u in parc.Unites)
                if (u.MorteA == 0 && (u.Voiture.IsDead || !u.Voiture.IsDriveable)) u.MorteA = Math.Max(1, maintenant);
            foreach (UnitePolice u in parc.Retirer(delegate (UnitePolice x) { return x.MorteA != 0 && maintenant - x.MorteA > EPAVE_MS; }))
            {
                Supprimer(u);
                journal.Info("Épave « " + u.Nom + " » supprimée");
            }

            // 5. Semée (à plus de 120 m, hors écran, depuis 2 s) : elle réapparaît
            // derrière le joueur, lancée à sa vitesse, RATTRAPAGES fois au plus —
            // « beaucoup trop facile à esquiver » au test en jeu. Au-delà, distancée
            // à plus de 250 m pendant 15 s : la poursuite est perdue, elle est lâchée.
            foreach (UnitePolice u in parc.Unites)
            {
                if (u.MorteA != 0) continue;
                float distance = u.Voiture.Position.DistanceTo(joueur.Position);
                bool visible = Function.Call<bool>(Hash.IS_ENTITY_ON_SCREEN, u.Voiture);
                if (!Regles.EstSemee(distance, visible, DISTANCE_SEMEE_M)) u.SemeeDepuis = 0;
                else if (u.SemeeDepuis == 0) u.SemeeDepuis = Math.Max(1, maintenant);
                if (!Regles.EstPlantee(u.Voiture.Speed, distance, VITESSE_IMMOBILE, DISTANCE_CONTACT_M)) u.ImmobileDepuis = 0;
                else if (u.ImmobileDepuis == 0) u.ImmobileDepuis = Math.Max(1, maintenant);
                bool semee = u.SemeeDepuis != 0 && maintenant - u.SemeeDepuis >= SEMEE_MS;
                bool plantee = !visible && u.ImmobileDepuis != 0 && maintenant - u.ImmobileDepuis >= IMMOBILE_MS;
                if ((semee || plantee) && !u.Renfort && u.Rattrapages < rattrapagesMax)
                {
                    Rattraper(u, joueur, semee ? "semée" : "plantée hors écran");
                    continue;
                }
                if (distance <= DISTANCE_LACHER_M) u.LoinDepuis = 0;
                else if (u.LoinDepuis == 0) u.LoinDepuis = Math.Max(1, maintenant);
            }
            foreach (UnitePolice u in parc.Retirer(delegate (UnitePolice x) { return x.MorteA == 0 && x.LoinDepuis != 0 && maintenant - x.LoinDepuis > LOIN_MS; }))
            {
                journal.Info("Voiture « " + u.Nom + " » distancée : lâchée");
                Lacher(u);
            }

            // 6. Traque redonnée à chaque entretien ; une ligne de diagnostic 5 s
            // après l'apparition.
            foreach (UnitePolice u in parc.Unites)
            {
                if (u.MorteA != 0) continue;
                if (!u.DiagnostiqueFait && maintenant - u.NeeA >= DIAGNOSTIC_MS)
                {
                    u.DiagnostiqueFait = true;
                    Diagnostiquer(u, u.Voiture.Speed, u.Voiture.Position.DistanceTo(joueur.Position));
                }
                DonnerOrdre(u, joueur);
            }
        }

        // Semée ou plantée hors écran : la voiture est replacée derrière le joueur
        // (un peu plus loin qu'à l'apparition, pour qu'on la voie moins surgir),
        // orientée vers lui et lancée à sa vitesse, puis la traque reprend.
        void Rattraper(UnitePolice u, Ped joueur, string raison)
        {
            Vector3 position = PointDApparition(joueur, Math.Min(distanceM + RECUL_RATTRAPAGE_M, PLAFOND_DISTANCE_M));
            float cap = (joueur.Position - position).ToHeading();
            Function.Call(Hash.SET_ENTITY_COORDS, u.Voiture, position.X, position.Y, position.Z, false, false, false, false);
            Function.Call(Hash.SET_ENTITY_HEADING, u.Voiture, cap);
            Function.Call<bool>(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, u.Voiture, 5f);
            Vehicle vj = joueur.IsInVehicle() ? joueur.CurrentVehicle : null;
            Lancer(u.Voiture, vj != null ? vj.Speed : 0f);
            u.Rattrapages++;
            u.SemeeDepuis = 0; u.LoinDepuis = 0; u.ImmobileDepuis = 0;
            DonnerOrdre(u, joueur);
            journal.Info("« " + u.Nom + " » " + raison + " : réapparaît derrière le joueur (" + u.Rattrapages + "/" + rattrapagesMax + ")");
        }

        // Ce que le journal ne disait pas quand le fourgon CRS restait sur place.
        void Diagnostiquer(UnitePolice u, float vitesse, float distance)
        {
            Ped c = u.Conducteur;
            bool auVolant = c != null && c.Exists() && !c.IsDead && c.IsInVehicle(u.Voiture);
            journal.Info("Diagnostic « " + u.Nom + " » à 5 s : " + Math.Round(vitesse * 3.6f) + " km/h, "
                + Math.Round(distance) + " m du joueur, au volant : " + (auVolant ? "oui" : "NON")
                + ", moteur : " + (Function.Call<bool>(Hash.GET_IS_VEHICLE_ENGINE_RUNNING, u.Voiture) ? "oui" : "NON")
                + ", 4 roues au sol : " + (Function.Call<bool>(Hash.IS_VEHICLE_ON_ALL_WHEELS, u.Voiture) ? "oui" : "NON")
                + ", attend le sol : " + (Function.Call<bool>(Hash.IS_ENTITY_WAITING_FOR_WORLD_COLLISION, u.Voiture) ? "OUI" : "non"));
        }

        // Supprime une unité (plafond atteint, épave). Jamais la voiture où se trouve
        // le joueur ; les agents encore vivants hors de la voiture sont seulement lâchés.
        void Supprimer(UnitePolice u)
        {
            try
            {
                Ped joueur = Game.Player.Character;
                if (u.Voiture.Exists() && joueur != null && joueur.Exists() && joueur.IsInVehicle(u.Voiture)) { Lacher(u); return; }
                Ped a = u.Conducteur;
                if (a != null && a.Exists())
                {
                    if (a.IsDead || (u.Voiture.Exists() && a.IsInVehicle(u.Voiture))) a.Delete();
                    else LacherAgent(a);
                }
                if (u.Voiture.Exists()) u.Voiture.Delete();
            }
            catch (Exception ex) { journal.ErreurLimitee("supprimer", "Suppression d'une voiture : " + ex.Message); }
        }

        // Rend une unité au jeu : sirène coupée, balade tranquille, plus rien de « à nous ».
        void Lacher(UnitePolice u)
        {
            try
            {
                if (u.Voiture.Exists())
                {
                    Ped c = u.Conducteur;
                    if (c != null && c.Exists() && !c.IsDead && c.IsInVehicle(u.Voiture))
                        Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, c, u.Voiture, 15f, CONDUITE_NORMALE);
                    u.Voiture.IsSirenActive = false;
                    u.Voiture.MarkAsNoLongerNeeded();
                }
                LacherAgent(u.Conducteur);
            }
            catch (Exception ex) { journal.ErreurLimitee("lacher", "Libération d'une voiture : " + ex.Message); }
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
