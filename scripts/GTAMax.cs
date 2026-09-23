/*
 * GTAMax.cs — plugin GTA V de GTA Max (ScriptHookVDotNet v3).
 *
 * Lit les commandes que le serveur GTA Max dépose dans scripts\gtamax_queue.txt
 * et les exécute en jeu. En v1, une seule action : SPAWN_POLICE (un cadeau
 * TikTok = une voiture de police qui prend le joueur en chasse).
 *
 * COMPILATION : « Installer plugin GTA.bat » (app/outils/plugin.js) compile ce
 * fichier en scripts\GTAMax.dll avec le csc du .NET Framework 4.8, contre
 * ScriptHookVDotNet3.dll. Ce csc ne connaît que C# 5 : pas de $"", pas de ?.,
 * pas de nameof, pas d'initialiseur de propriété automatique.
 * Les tests (app/test/plugin.test.js) compilent ce même fichier avec
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

        // Distance d'apparition : la base (~20 m) + une seconde de trajet si le joueur
        // roule, pour que la voiture n'apparaisse pas dans son pare-chocs à 150 km/h.
        public static float DistanceApparition(float baseM, float vitesseMs, float plafondM)
        {
            float d = baseM + Math.Max(0f, vitesseMs);
            return Math.Min(Math.Max(d, baseM), Math.Max(baseM, plafondM));
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
    }

    sealed class ChargementEnCours
    {
        public DemandeSpawn Demande;
        public string Nom;
        public Model Voiture;
        public Model Agent;
        public int DebutA;
    }

    enum Poursuite { Aucune, Vehicule, APied }

    sealed class UnitePolice
    {
        public Vehicle Voiture;
        public Ped Conducteur;
        public Ped Passager;
        public int ModeleHash;       // garde-fou : un handle recyclé par le jeu n'est plus « notre » voiture
        public string Nom;
        public string Donateur;
        public int MorteA;           // Game.GameTime de la mort de la voiture (0 = vivante)
        public Poursuite Mode;
        public int DernierOrdreA;
    }

    public class GTAMax : Script
    {
        const string FICHIER = "gtamax_queue.txt";
        const string FICHIER_EN_COURS = "gtamax_queue.en-cours";
        const long TTL_MS = 60000;                 // même péremption que le serveur
        const long AGE_MAX_DEMANDE_MS = 60000;     // une demande qui attend (joueur mort, cinématique) n'est pas rattrapée après
        const int CHARGEMENT_MAX_MS = 5000;        // modèle qui ne se charge pas : on abandonne cette apparition
        const int EPAVE_MS = 8000;                 // une épave reste visible 8 s puis disparaît
        const int ENTRETIEN_MS = 1000;             // suivi des poursuites et du nettoyage
        const int ORDRE_A_PIED_MS = 3000;          // joueur à pied : la cible bouge, on réoriente toutes les 3 s
        const float PLAFOND_DISTANCE_M = 60f;
        const string PED_POLICE = "s_m_y_cop_01";
        const int CONDUITE_EVITE_TRAFIC = 786468;  // style de conduite « contourne le trafic »
        const int CONDUITE_NORMALE = 786603;
        const int MISSION_ALLER_VERS = 4;          // TASK_VEHICLE_MISSION_PED_TARGET : MISSION_GOTO

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
                + " — file : " + Path.Combine(dossierScripts, FICHIER));
        }

        void ChargerReglages()
        {
            string liste = "police,police2,police3,sheriff";
            int intervalle = 3000, maxVoitures = 8, maxAttente = 20;
            float distance = 20f;
            try
            {
                liste       = Settings.GetValue<string>("POLICE", "MODELES", liste);
                distance    = Settings.GetValue<float>("POLICE", "DISTANCE_M", distance);
                intervalle  = Settings.GetValue<int>("POLICE", "INTERVALLE_MS", intervalle);
                maxVoitures = Settings.GetValue<int>("POLICE", "MAX_VOITURES", maxVoitures);
                maxAttente  = Settings.GetValue<int>("POLICE", "MAX_EN_ATTENTE", maxAttente);
            }
            catch (Exception e) { journal.Erreur("GTAMax.ini illisible, réglages par défaut : " + e.Message); }

            modeles = Regles.ListeModeles(liste);
            if (modeles.Count == 0) modeles = Regles.ListeModeles("police,police2,police3,sheriff");
            distanceM = Regles.Borne(distance, 5f, PLAFOND_DISTANCE_M);
            fileSpawn = new FileSpawn<DemandeSpawn>(Regles.Borne(intervalle, 500, 60000), Regles.Borne(maxAttente, 1, 200), AGE_MAX_DEMANDE_MS);
            parc = new Parc<UnitePolice>(Regles.Borne(maxVoitures, 1, 30));
            journal.Info("Réglages : modèles " + string.Join(", ", modeles) + " ; " + distanceM + " m ; 1 voiture / "
                + Regles.Borne(intervalle, 500, 60000) + " ms ; " + parc.Max + " max");
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
        }

        // Script déchargé (fermeture du jeu, rechargement SHVDN) : on lâche tout ce
        // qu'on suivait, rien ne reste marqué « à nous ».
        void SurArret(object sender, EventArgs e)
        {
            try
            {
                AbandonnerChargement();
                if (parc != null) foreach (UnitePolice u in parc.Retirer(delegate (UnitePolice x) { return true; })) Lacher(u);
                journal.Info("Plugin GTA Max arrêté.");
            }
            catch (Exception) { }
        }

        // ─── FILE ─────────────────────────────────────────────────────────
        void LireFile()
        {
            string enCours = Path.Combine(dossierScripts, FICHIER_EN_COURS);
            string file = Path.Combine(dossierScripts, FICHIER);
            if (!File.Exists(enCours))
            {
                if (!File.Exists(file)) return;          // cas normal : rien à faire (serveur éteint ou aucun don)
                try { File.Move(file, enCours); }         // on PREND le fichier : le serveur peut en écrire un nouveau
                catch (IOException) { return; }           // verrouillé (antivirus, rename en cours) : tick suivant
                catch (UnauthorizedAccessException) { return; }
            }
            string contenu;
            try
            {
                contenu = File.ReadAllText(enCours, Encoding.UTF8);
                File.Delete(enCours);                     // supprimé AVANT d'exécuter : jamais joué deux fois
            }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }

            foreach (Commande c in LecteurFile.ParserContenu(contenu, MaintenantEpochMs(), TTL_MS, journal.Info))
            {
                journal.Info("Commande : " + c);
                registre.Executer(c, journal.Erreur);
            }
        }

        // ─── SPAWN_POLICE ─────────────────────────────────────────────────
        void SpawnPolice(Commande c)
        {
            var d = new DemandeSpawn { Donateur = c.Arg("donateur"), Cadeau = c.Arg("cadeau") };
            if (fileSpawn.Ajouter(d, Game.GameTime))
                journal.Info("Voiture de police en file (" + fileSpawn.Count + " en attente) — " + d.Donateur + " / " + d.Cadeau);
            else
                journal.Info("File des voitures pleine : cadeau de " + d.Donateur + " sans voiture");
        }

        bool JoueurDisponible()
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
            float cap = (joueur.Position - position).ToHeading();   // orientée vers le joueur

            foreach (UnitePolice vieille in parc.FaireDeLaPlace())
            {
                journal.Info("Plafond de " + parc.Max + " voitures : la plus ancienne (" + vieille.Nom + ") est retirée");
                Supprimer(vieille);
            }

            // Natives plutôt que les enveloppes SHVDN : World.CreateVehicle et
            // GetGroundHeight sont « obsolètes » dans les nightlies et leurs
            // remplaçantes n'existent pas en version stable. Une native, elle,
            // ne bouge pas d'une version de SHVDN à l'autre.
            Vehicle v = Function.Call<Vehicle>(Hash.CREATE_VEHICLE, c.Voiture.Hash, position.X, position.Y, position.Z, cap, false, false, false);
            if (v == null || v.Handle == 0 || !v.Exists()) { journal.Erreur("Le jeu a refusé de créer « " + c.Nom + " »"); return; }
            Function.Call<bool>(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, v, 5f);
            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, v, true, true, false);

            Ped conducteur = v.CreatePedOnSeat(VehicleSeat.Driver, c.Agent);
            Ped passager = v.CreatePedOnSeat(VehicleSeat.Passenger, c.Agent);
            PreparerAgent(conducteur);
            PreparerAgent(passager);

            // Gyrophares : si ELS est chargé ET a un VCF pour ce modèle, il prend la
            // main dès que la sirène est active (on ne touche pas au mute : ELS joue
            // ses propres tonalités). Sinon, feux vanilla : sirène active ET audible.
            bool els = GereParEls(c.Nom);
            v.IsSirenActive = true;
            if (!els) v.IsSirenSilent = false;

            var u = new UnitePolice
            {
                Voiture = v, Conducteur = conducteur, Passager = passager,
                ModeleHash = v.Model.Hash, Nom = c.Nom, Donateur = c.Demande.Donateur,
                Mode = Poursuite.Aucune,
            };
            DonnerOrdre(u, joueur);

            // Non persistant dès l'apparition : le jeu les ramasse quand il veut (loin,
            // hors champ). « Garde ta tâche » maintient la poursuite en attendant.
            if (conducteur != null && conducteur.Exists()) conducteur.MarkAsNoLongerNeeded();
            if (passager != null && passager.Exists()) passager.MarkAsNoLongerNeeded();
            v.MarkAsNoLongerNeeded();

            parc.Ajouter(u);
            journal.Info("Voiture « " + c.Nom + " » apparue pour " + (c.Demande.Donateur.Length > 0 ? c.Demande.Donateur : "?")
                + " (" + parc.Count + "/" + parc.Max + ", gyrophares " + (els ? "ELS" : "vanilla") + ")");
        }

        Vector3 PointDApparition(Ped joueur)
        {
            Vehicle vj = joueur.IsInVehicle() ? joueur.CurrentVehicle : null;
            Vector3 avant = vj != null ? vj.ForwardVector : joueur.ForwardVector;
            float vitesse = vj != null ? vj.Speed : 0f;
            float d = Regles.DistanceApparition(distanceM, vitesse, PLAFOND_DISTANCE_M);
            Vector3 cible = joueur.Position + avant * d;

            // Route la plus proche du point visé (place libre) ; repli sur le sol si
            // elle est trop loin (campagne, toit) ou collée au joueur.
            Vector3 route = World.GetNextPositionOnStreet(cible, true);
            float ecart = route.DistanceTo(joueur.Position);
            if (route != Vector3.Zero && ecart >= 8f && ecart <= PLAFOND_DISTANCE_M) return route;

            var sol = new OutputArgument();
            bool trouve = Function.Call<bool>(Hash.GET_GROUND_Z_FOR_3D_COORD, cible.X, cible.Y, cible.Z + 10f, sol, false, false);
            return new Vector3(cible.X, cible.Y, trouve ? sol.GetResult<float>() : cible.Z);
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

        static void PreparerAgent(Ped p)
        {
            if (p == null || !p.Exists()) return;
            p.BlockPermanentEvents = true;                     // ne réagit à rien (tirs, collisions) : ne fuit pas, ne descend pas se battre
            Function.Call(Hash.SET_PED_KEEP_TASK, p, true);    // garde la poursuite même une fois « non persistant »
            Function.Call(Hash.SET_DRIVER_ABILITY, p, 1f);
            Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, p, 1f);
        }

        // Joueur en véhicule : vraie poursuite (TASK_VEHICLE_CHASE). À pied : le
        // conducteur fonce vers lui, réorienté toutes les 3 s puisqu'il bouge.
        void DonnerOrdre(UnitePolice u, Ped joueur)
        {
            Ped c = u.Conducteur;
            if (c == null || !c.Exists() || c.IsDead || !u.Voiture.Exists() || !c.IsInVehicle(u.Voiture)) return;
            int maintenant = Game.GameTime;
            if (joueur.IsInVehicle())
            {
                if (u.Mode == Poursuite.Vehicule) return;
                Function.Call(Hash.TASK_VEHICLE_CHASE, c, joueur);
                u.Mode = Poursuite.Vehicule;
            }
            else
            {
                if (u.Mode == Poursuite.APied && maintenant - u.DernierOrdreA < ORDRE_A_PIED_MS) return;
                Function.Call(Hash.TASK_VEHICLE_MISSION_PED_TARGET, c, u.Voiture, joueur, MISSION_ALLER_VERS, 25f, CONDUITE_EVITE_TRAFIC, 6f, 10f, true);
                u.Mode = Poursuite.APied;
            }
            u.DernierOrdreA = maintenant;
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

            // 5. Poursuite : réorienter selon l'état du joueur.
            foreach (UnitePolice u in parc.Unites)
                if (u.MorteA == 0) DonnerOrdre(u, joueur);
        }

        // Supprime une unité (plafond atteint, épave). Jamais la voiture où se trouve
        // le joueur ; les agents encore vivants hors de la voiture sont seulement lâchés.
        void Supprimer(UnitePolice u)
        {
            try
            {
                Ped joueur = Game.Player.Character;
                if (u.Voiture.Exists() && joueur != null && joueur.Exists() && joueur.IsInVehicle(u.Voiture)) { Lacher(u); return; }
                foreach (Ped a in new[] { u.Conducteur, u.Passager })
                {
                    if (a == null || !a.Exists()) continue;
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
                LacherAgent(u.Passager);
            }
            catch (Exception ex) { journal.ErreurLimitee("lacher", "Libération d'une voiture : " + ex.Message); }
        }

        static void LacherAgent(Ped a)
        {
            if (a == null || !a.Exists()) return;
            a.BlockPermanentEvents = false;
            Function.Call(Hash.SET_PED_KEEP_TASK, a, false);
            a.MarkAsNoLongerNeeded();
        }
    }
#endif
}
