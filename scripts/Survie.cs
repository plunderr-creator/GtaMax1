/*
 * Survie.cs — mode « survie » du plugin GTA Max, façon Driver 2 (PlayStation, 2000).
 *
 * Les cadeaux TikTok lâchent une meute de flics sur le joueur : la poursuite
 * dure tant que les cadeaux tombent, et le temps tenu est son score (record
 * gardé dans scripts\GTAMax.record).
 *
 * CE QU'ON REPREND DE DRIVER 2 — lu dans son code décompilé (projet REDRIVER2 :
 * cop_ai.c, civ_ai.c, mission.c). Une PlayStation y tenait une poursuite sans
 * fin avec 20 voitures en tout autour du joueur, dont 4 flics au plus :
 *   1. Un parc FIXE (car_data[20]) : rien n'est créé en plus, rien n'est
 *      détruit. Ici : 4 flics au plus, créés une fois, jamais supprimés pendant
 *      la poursuite → pas de va-et-vient d'entités ni de modèles à charger.
 *   2. Le « ping out / ping in » : un flic semé, planté ou retourné n'est pas
 *      détruit, il est REPLACÉ au bord de la bulle autour du joueur, sur une
 *      route, tourné vers lui — et jamais sous ses yeux (sur PS1, le brouillard
 *      le cachait ; ici : hors du champ de la caméra, ou caché par le décor).
 *   3. Des flics indestructibles (gCopData.immortal en survie) : pas d'épave à
 *      nettoyer, la pression ne retombe jamais.
 *   4. La chaleur (le « felony ») fixe le nombre de flics et leur puissance
 *      (autoMaxPowerScaleLimit) : ici, un cran par cadeau.
 *   5. Au contact, le flic vise le FLANC du joueur là où il sera (gBatterPlayer :
 *      il percute par le côté) ; de loin il suit les routes ; planté, il recule
 *      en braquant (la « recovery » de CopControl1).
 *   6. Hors champ, les flics trichent un peu pour recoller (rattrapage).
 * Et pendant la poursuite, AUCUNE police du jeu (recherche max 0) : ni renforts,
 * ni hélicos, ni tirs — c'est elle qui chargeait le jeu jusqu'au crash au test du
 * 2026-09-23. Les étoiles affichées sont factices (SET_FAKE_WANTED_LEVEL).
 *
 * Même règle d'or que GTAMax.cs : ne jamais faire tomber ni figer le jeu.
 * La logique pure (en haut) est testée par app/test/plugin.test.js.
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

    /// <summary>
    /// Une poursuite : la chaleur (le « felony » de Driver 2) monte d'un cran par
    /// cadeau et fixe le nombre de flics et leur rattrapage ; la poursuite
    /// s'arrête dureeMs après le DERNIER cadeau. Temps = Game.GameTime (ms).
    /// </summary>
    public sealed class Poursuite
    {
        readonly int maxFlics, dureeMs, chaleurMax;
        bool active;
        int chaleur, debutA, finA;

        public Poursuite(int maxFlics, int dureeMs, int chaleurMax)
        {
            this.maxFlics = Math.Max(1, maxFlics);
            this.dureeMs = Math.Max(1000, dureeMs);
            this.chaleurMax = Math.Max(1, chaleurMax);
        }

        public bool Active { get { return active; } }
        public int Chaleur { get { return chaleur; } }
        public int DebutA { get { return debutA; } }
        public int FinA { get { return finA; } }
        public int FlicsVoulus { get { return active ? Math.Min(maxFlics, chaleur) : 0; } }

        // Driver 2 en survie : puissance des flics ×1,12, puis jusqu'à +25 % avec
        // le felony. Ici : 5 % de plus que le joueur, + 3 % par cran de chaleur.
        public float FacteurRattrapage { get { return 1.05f + 0.03f * chaleur; } }

        // true si ce cadeau COMMENCE une poursuite.
        public bool Cadeau(int maintenant)
        {
            bool debut = !active;
            if (debut) { active = true; debutA = maintenant; chaleur = 0; }
            chaleur = Math.Min(chaleurMax, chaleur + 1);
            finA = maintenant + dureeMs;
            return debut;
        }

        public bool Expiree(int maintenant) { return active && maintenant >= finA; }
        public int DureeMs(int maintenant) { return active ? maintenant - debutA : 0; }
        public int RestantMs(int maintenant) { return active ? Math.Max(0, finA - maintenant) : 0; }
        public void Terminer() { active = false; chaleur = 0; }
    }

    /// <summary>Règles de calcul du mode survie, sorties du jeu pour être testées.</summary>
    public static class ReglesSurvie
    {
        // Directions où chercher une route pour faire apparaître un flic, en degrés
        // par rapport à la direction où va le joueur (0 = devant, 180 = derrière,
        // positif = à gauche). Un NOUVEAU flic arrive par derrière puis par les
        // côtés, jamais de devant : le cadeau se voit tout de suite dans le
        // rétro. Un flic RECYCLÉ peut revenir de partout, comme dans Driver 2
        // (56 positions sur le cercle) : ici 8, en partant d'un angle qui tourne.
        static readonly float[] DERRIERE_D_ABORD = { 180f, 150f, -150f, 120f, -120f, 90f, -90f };

        public static float[] AnglesCandidats(int cookie, bool nouveau)
        {
            if (nouveau)
            {
                var a = (float[])DERRIERE_D_ABORD.Clone();
                if ((cookie & 1) == 1)   // une fois sur deux, la gauche avant la droite
                    for (int i = 1; i + 1 < a.Length; i += 2) { float x = a[i]; a[i] = a[i + 1]; a[i + 1] = x; }
                return a;
            }
            var r = new float[8];
            int depart = (Math.Abs(cookie) * 135) % 360;   // 0, 135, 270, 45… : les 8 départs y passent
            for (int i = 0; i < 8; i++) r[i] = Normaliser(depart + i * 45f);
            return r;
        }

        // ]-180 ; 180]
        public static float Normaliser(float angle)
        {
            float a = angle % 360f;
            if (a > 180f) a -= 360f;
            if (a <= -180f) a += 360f;
            return a;
        }

        public static string Secteur(float angle)
        {
            float a = Math.Abs(Normaliser(angle));
            return a >= 135f ? "derrière" : a >= 45f ? "sur le côté" : "devant";
        }

        // Joueur RAPIDE : la COURSE. Test n°1 : placés derrière ou sur les côtés, loin,
        // les flics ne le rattrapaient jamais ; test n°2 : placés devant et tournés
        // vers lui, c'étaient des face-à-face. Demande du streamer : qu'ils fassent la
        // course avec lui. Repère du joueur : L = combien le flic est devant (+) ou
        // derrière (−), S = de quel côté. Derrière : il remonte sur SON côté, plus vite
        // que le joueur ; à sa hauteur : il serre vers lui (coup d'épaule, PIT), à
        // peine plus vite ; devant : il se rabat devant lui, à peine moins vite (il lui
        // barre la route). La cible est TOUJOURS devant le flic dans le sens de la
        // course : il ne fait jamais demi-tour. Rend la phase ; x, y = cible ; vitesse
        // = vitesse de croisière voulue (m/s).
        public const float COURSE_HAUTEUR_M = 8f;
        public static string ConsigneCourse(float jx, float jy, float vjx, float vjy, float cx, float cy,
                                            float droiteX, float droiteY, out float x, out float y, out float vitesse)
        {
            float vj = (float)Math.Sqrt(vjx * vjx + vjy * vjy);
            float fx = vj > 0.1f ? vjx / vj : -droiteY, fy = vj > 0.1f ? vjy / vj : droiteX;   // sinon : « devant » tiré de sa droite
            float dx = cx - jx, dy = cy - jy;
            float l = dx * fx + dy * fy, s = dx * droiteX + dy * droiteY;
            float cote = s >= 0f ? 1f : -1f;
            float avance = 12f + vj * 0.5f;   // la cible court devant le joueur
            float devant, lateral;
            string phase;
            if (l < -COURSE_HAUTEUR_M) { phase = "rattrape"; devant = avance + 3f; lateral = cote * 2.5f; vitesse = vj + 8f; }
            else if (l <= COURSE_HAUTEUR_M) { phase = "à sa hauteur"; devant = Math.Max(avance, l + 12f); lateral = cote * 0.3f; vitesse = vj + 2f; }
            else { phase = "devant"; devant = l + avance; lateral = 0f; vitesse = Math.Max(10f, vj - 2f); }
            x = jx + fx * devant + droiteX * lateral;
            y = jy + fy * devant + droiteY * lateral;
            return phase;
        }

        // Vitesse de pointe en plus (%, MODIFY_VEHICLE_TOP_SPEED) pour que le flic
        // puisse suivre la voiture du joueur (+8 %) : une supercar à 250 km/h
        // distançait la voiture de police. Jamais moins que le réglage, plafonnée.
        public static float BonusPourSuivre(float vmaxJoueurMs, float vmaxFlicMs, float bonusMin, float bonusMax)
        {
            if (vmaxJoueurMs <= 1f || vmaxFlicMs <= 1f) return Math.Min(bonusMax, Math.Max(0f, bonusMin));
            float besoin = (vmaxJoueurMs * 1.08f / vmaxFlicMs - 1f) * 100f;
            return Math.Min(bonusMax, Math.Max(bonusMin, besoin));
        }

        // Peut-on poser un flic à cet endroit ? Hors du champ de la caméra : oui.
        // Dans le champ : seulement caché par le décor et assez loin, ou (devant un
        // joueur rapide) assez loin pour n'être qu'un point sur la route, comme la
        // circulation du jeu qui apparaît au loin.
        public static bool ApparitionAcceptee(bool dansLeChamp, bool cacheParLeDecor, float distanceM,
                                              float distanceMinCacheM, float distanceMinVisibleM)
        {
            if (!dansLeChamp) return true;
            if (cacheParLeDecor && distanceM >= distanceMinCacheM) return true;
            return distanceM >= distanceMinVisibleM;
        }

        // Cap GTA (degrés, 0 = nord +Y, 90 = ouest -X) pour regarder de (dx, dy) vers (vx, vy).
        public static float CapVers(float deX, float deY, float versX, float versY)
        {
            double cap = Math.Atan2(-(versX - deX), versY - deY) * 180.0 / Math.PI;
            if (cap < 0) cap += 360.0;
            return (float)cap;
        }

        // Le sens d'une route rendu par le jeu peut tourner le dos au joueur (voie
        // d'en face d'une route à chaussées séparées) : on garde ce cap ou son
        // opposé, celui qui regarde le joueur.
        public static float CapDeRouteVers(float capRoute, float deX, float deY, float versX, float versY)
        {
            double r = capRoute * Math.PI / 180.0;
            double ax = -Math.Sin(r), ay = Math.Cos(r);
            double scal = ax * (versX - deX) + ay * (versY - deY);
            float cap = scal >= 0 ? capRoute : capRoute + 180f;
            cap %= 360f;
            if (cap < 0f) cap += 360f;
            return cap;
        }

        // Un point de route trouvé est-il bon ? Ni trop près (on le verrait
        // surgir), ni trop loin (il ne servirait à rien), ni sur un autre étage
        // (pont, tunnel : Driver 2 vérifie aussi la hauteur).
        public static bool DistanceDApparitionOk(float distanceM, float ecartHauteurM, float rayonM)
        {
            return distanceM >= rayonM * 0.6f && distanceM <= rayonM * 1.6f && Math.Abs(ecartHauteurM) <= 12f;
        }

        // Le point est-il dans le cône de la caméra ? tangage/lacet en degrés
        // (rotation « 2 » de GTA : lacet 0 = nord, +Y ; 90 = ouest). Le cône est
        // volontairement large (demi-angle 60° pour ≈ 40° réels à l'écran) : on ne
        // fait jamais apparaître un flic au bord de l'image.
        public static bool DansLeChamp(float camX, float camY, float camZ, float tangageDeg, float lacetDeg,
                                       float px, float py, float pz, float demiAngleDeg)
        {
            double t = tangageDeg * Math.PI / 180.0, l = lacetDeg * Math.PI / 180.0;
            double dx = -Math.Sin(l) * Math.Cos(t), dy = Math.Cos(l) * Math.Cos(t), dz = Math.Sin(t);
            double vx = px - camX, vy = py - camY, vz = pz - camZ;
            double n = Math.Sqrt(vx * vx + vy * vy + vz * vz);
            if (n < 1e-3) return true;
            return (dx * vx + dy * vy + dz * vz) / n >= Math.Cos(demiAngleDeg * Math.PI / 180.0);
        }

        // Recycler (le « ping out / ping in » de Driver 2) : jamais sous les yeux du
        // joueur ; sinon s'il est retourné ou à l'eau, semé, ou planté trop longtemps.
        // Semé = loin DERRIÈRE le joueur (il ne le rattrapera plus), ou très loin
        // partout. Loin DEVANT, il arrive sur lui : on le laisse.
        // null = on le laisse chasser.
        public static string RaisonDeRecycler(bool visible, float distanceM, bool derriereLeJoueur, int planteeMs, bool horsDeCourse,
                                              float semeDerriereM, float semeM, int planteeMaxMs)
        {
            if (visible) return null;
            if (horsDeCourse) return "retourné ou à l'eau";
            if (distanceM > semeM || (derriereLeJoueur && distanceM > semeDerriereM)) return "semé";
            if (planteeMs >= planteeMaxMs) return "planté";
            return null;
        }

        // Dans combien de secondes le flic sera-t-il au plus près du joueur s'ils
        // gardent leurs vitesses ? r = joueur - flic, w = vitesse joueur - vitesse
        // flic (au sol). Driver 2 fait ce calcul au contact, borné à 60 images
        // (2 s). S'il ne se rapproche pas, anticipation de secours.
        public static float TempsDInterception(float rx, float ry, float wx, float wy, float secoursS, float maxS)
        {
            float ww = wx * wx + wy * wy;
            float t = ww < 1f ? secoursS : -(rx * wx + ry * wy) / ww;
            if (t <= 0f) t = secoursS;
            return Math.Min(maxS, Math.Max(0f, t));
        }

        // Point visé au contact (le « batter » de Driver 2) : là où sera le joueur
        // dans t s, décalé sur le flanc où se trouve le flic (il percute par le
        // côté, comme un PIT), puis prolongé de `depassementM` dans l'axe d'arrivée :
        // l'IA de GTA ralentit en approchant de son but — visé au-delà du joueur,
        // le flic arrive sur lui à pleine vitesse.
        public static void PointVise(float jx, float jy, float vjx, float vjy, float cx, float cy, float t,
                                     float droiteX, float droiteY, float flancM, float depassementM,
                                     out float x, out float y)
        {
            float px = jx + vjx * t, py = jy + vjy * t;
            float cote = (cx - jx) * droiteX + (cy - jy) * droiteY >= 0f ? 1f : -1f;
            px += droiteX * flancM * cote;
            py += droiteY * flancM * cote;
            float dx = px - cx, dy = py - cy;
            float n = (float)Math.Sqrt(dx * dx + dy * dy);
            if (n > 1e-3f) { px += dx / n * depassementM; py += dy / n * depassementM; }
            x = px; y = py;
        }

        // Rattrapage hors champ (la puissance « auto » de Driver 2) : vitesse à
        // donner au flic, ou 0 s'il va déjà assez vite. Au moins 20 m/s pour
        // arriver vite sur un joueur arrêté ; plafonnée.
        public static float VitesseRattrapage(float vitesseJoueurMs, float vitesseFlicMs, float facteur, float maxMs)
        {
            float cible = Math.Min(maxMs, Math.Max(20f, vitesseJoueurMs * facteur + 3f));
            return vitesseFlicMs < cible - 1f ? cible : 0f;
        }

        public static string Duree(int ms)
        {
            int s = Math.Max(0, ms) / 1000;
            if (s < 60) return s + " s";
            return (s / 60) + " min " + (s % 60).ToString("00", CultureInfo.InvariantCulture) + " s";
        }

        // Texte affiché en jeu (pseudo TikTok) : sans « ~ » (codes couleur de GTA,
        // un pseudo pourrait casser l'affichage), sans caractères de contrôle, coupé.
        public static string TexteJeu(string texte, int max)
        {
            var sb = new StringBuilder();
            foreach (char c in texte ?? "") if (c != '~' && !char.IsControl(c)) sb.Append(c);
            string s = sb.ToString().Trim();
            if (s.Length > max) s = s.Substring(0, max).TrimEnd();
            return s.Length == 0 ? "?" : s;
        }
    }

#if !TESTS_SANS_JEU
    // ═════════════════════════════════════════════════════════════════════
    //  LE MODE SURVIE EN JEU — dépend du jeu (ScriptHookVDotNet v3)
    // ═════════════════════════════════════════════════════════════════════

    sealed class FlicSurvie
    {
        public int Numero;
        public Vehicle Voiture;
        public Ped Conducteur;
        public int ModeleHash;          // garde-fou : un handle recyclé par le jeu n'est plus « notre » voiture
        public string Donateur;
        public int NeeA;
        public int MorteA;              // épave (flics destructibles seulement) ; 0 = roule
        public int PlanteeDepuis;       // 0 = elle roule (ou elle est au contact)
        public int ManoeuvreJusquA;     // marche arrière de dégagement en cours jusqu'à…
        public int Manoeuvres;          // dégagements depuis la dernière fois qu'elle a roulé
        public int ProchainOrdre, ProchainCoup, ProchaineReparation;
        public int Recyclages;
        public int DernierePoseA;       // apparition ou dernier recyclage : pas de nouveau recyclage avant 3 s
        public int SuiviA;              // contrôle 2 s après la pose : se rapproche-t-il ? (0 = fait)
        public float SuiviDistance;
        public string SuiviOu;
        public int Contacts;            // fois où il est arrivé au contact du joueur
        public bool AuContact;
        public string Phase = "";       // course : « rattrape », « à sa hauteur », « devant »
        public float Bonus;             // vitesse de pointe en plus appliquée (%)
        public bool DiagnostiqueFait;
    }

    /// <summary>Le mode survie en jeu. Appelé par GTAMax : Cadeau(), Tick(), Liberer().</summary>
    sealed class Survie
    {
        // ── Réglages fixes (les réglables sont dans GTAMax.ini, [SURVIE]) ──
        const int CHALEUR_MAX = 5;
        const float DEMI_CHAMP_DEG = 60f;           // cône de la caméra, large marge comprise
        const float DISTANCE_MIN_CACHE_M = 60f;      // dans le cône : seulement caché par le décor, et loin
        const float DISTANCE_ENTRE_FLICS_M = 20f;
        const float VITESSE_RAPIDE_MS = 15f;         // au-dessus (54 km/h) : la course (flics à côté de lui, dans son sens)
        const float A_COTE_RECUL_M = 15f;            // posé sur la voie d'à côté, 15 m derrière lui : hors de sa vue
        const float DISTANCE_DERRIERE_ROUTE_M = 60f; // à défaut, derrière, sur sa route
        const float LANCER_PLUS_MS = 6f;             // posé derrière : lancé 6 m/s plus vite que lui, à sa hauteur en 1-2 s
        const float VITESSE_LANCER_AUTRE_MS = 15f;   // posé ailleurs (joueur lent) : il démarre
        const float COURSE_DISTANCE_M = 60f;         // plus près, joueur rapide : consignes de course
        const float BONUS_MAX = 150f;                // vitesse de pointe en plus, au plus (%)
        // Coup d'épaule : ne contourne pas le joueur (64), voies d'en face permises (512), ligne droite (16777216).
        const int CONDUITE_COUP_D_EPAULE = 64 | 512 | 16777216;
        const float SEME_DERRIERE_M = 120f, SEME_M = 200f;  // Driver 2 : « ping out » au bord de la bulle
        const int RECYCLAGE_MIN_MS = 3000;           // un même flic n'est pas replacé plus d'une fois toutes les 3 s
        const int SUIVI_MS = 2000;
        const float DISTANCE_CONTACT_COMPTE_M = 8f;
        const int PLANTEE_MAX_MS = 2500;             // plantée hors champ : recyclée
        const int PLANTEE_AVANT_MANOEUVRE_MS = 1200; // plantée sous les yeux du joueur : elle recule en braquant
        const float VITESSE_PLANTEE = 1.5f;          // m/s
        const float DISTANCE_CONTACT_M = 12f;        // au contact, être arrêté est normal : elle vient de percuter
        const int MANOEUVRE_MS = 1200;
        const int MANOEUVRE_TOURNE_RECULE = 13, MANOEUVRE_RECULE_FORT = 28;   // TASK_VEHICLE_TEMP_ACTION ; 16 à 18 font planter le jeu : jamais
        const float DISTANCE_PERCUTER_M = 50f;       // plus près : on vise son flanc, au-delà de lui
        const float DISTANCE_LIGNE_DROITE_M = 40f;
        const float FLANC_M = 1.5f, DEPASSEMENT_M = 20f, INTERCEPTION_MAX_S = 2f;
        const float DISTANCE_COUP_M = 15f, COS_FACE = 0.87f, ELAN_COUP_MS = 6f;
        const int ORDRE_PRES_MS = 500, ORDRE_LOIN_MS = 1000, COUP_MS = 500;
        // Rattrapage invisible seulement bien aligné et 4 roues au sol : poussés en
        // travers à 234 km/h au test n°1, ils se retournaient (12 fois en 1 min 45).
        // Plafond relevé à 250 km/h au test n°2 : la course doit suivre une supercar.
        const float DISTANCE_MIN_RATTRAPAGE_M = 35f, COS_RATTRAPAGE = 0.9f, VITESSE_MAX_MS = 70f;
        const float VITESSE_MAX_COUP_MS = 70f;       // coup d'accélérateur au contact (< 15 m, face au joueur)
        const int PING_MS = 750;                     // un recyclage toutes les 0,75 s au plus, tous flics confondus
        const int CREATION_MS = 3000;                // une création toutes les 3 s au plus
        const int NOUVEL_ESSAI_MS = 500;             // pas de route cachée trouvée : on réessaie dans 0,5 s
        const int CHARGEMENT_MAX_MS = 5000;
        const int ENTRETIEN_MS = 250;
        const int REPARATION_MS = 2000;
        const int DIAGNOSTIC_MS = 5000, RESUME_MS = 10000, ETOILES_MS = 5000;
        const int EPAVE_MS = 8000;
        const float VITESSE_A_PIED_MS = 14f, DISTANCE_ATTRAPE_M = 8f;
        const int ATTRAPE_MS = 2000;
        // À pied, le flic s'approche sans l'écraser : il contourne le joueur (bit 64 absent) et les piétons (16).
        const int CONDUITE_A_PIED = 4 | 16 | 32 | 512 | 262144 | 524288;
        const int NOEUD_ROUTE_GOUDRONNEE = 0;        // GET_NTH_CLOSEST_VEHICLE_NODE_FAVOUR_DIRECTION

        readonly Journal journal;
        readonly Func<string, bool> gereParEls;
        readonly string cheminRecord;
        readonly Poursuite poursuite;
        readonly List<FlicSurvie> flics = new List<FlicSurvie>();
        readonly Random hasard = new Random();
        readonly List<string> modelesVoulus;
        readonly float rayonM, bonusMoteur;
        readonly bool indestructibles;

        string nomModele;               // premier modèle valide de [POLICE] MODELES
        bool modelesVerifies, els;
        Model modeleVoiture, modeleAgent;
        bool modelesDemandes;
        int chargementDepuis;
        int prochaineCreation, prochainPing, prochainEntretien, prochainResume, prochainesEtoiles;
        int attrapeDepuis;
        int cookie, numero;
        int maxEtoilesAvant = -1;       // niveau de recherche max d'avant la poursuite, rendu à la fin
        int recordMs;
        int contacts;                   // arrivées au contact du joueur, tous flics confondus, sur la poursuite
        string dernierDonateur = "";

        public Survie(Journal journal, string dossierScripts, Func<string, bool> gereParEls, List<string> modeles,
                      int maxFlics, int dureeS, float rayonM, float bonusMoteur, bool indestructibles)
        {
            this.journal = journal;
            this.gereParEls = gereParEls;
            cheminRecord = Path.Combine(dossierScripts, "GTAMax.record");
            modelesVoulus = modeles;
            this.rayonM = rayonM;
            this.bonusMoteur = bonusMoteur;
            this.indestructibles = indestructibles;
            poursuite = new Poursuite(maxFlics, dureeS * 1000, Math.Max(CHALEUR_MAX, maxFlics));
            recordMs = LireRecord();
        }

        public bool Active { get { return poursuite.Active; } }

        // ─── UN CADEAU ────────────────────────────────────────────────────
        public void Cadeau(string donateur, string cadeau)
        {
            string nom = ReglesSurvie.TexteJeu(donateur, 24);
            if (!GTAMax.JoueurDisponible()) { journal.Info("Survie : cadeau de " + nom + " ignoré (joueur mort, en chargement ou en cinématique)"); return; }
            if (Function.Call<bool>(Hash.GET_MISSION_FLAG)) { journal.Info("Survie : cadeau de " + nom + " ignoré (mission en cours, on ne la casse pas)"); return; }

            int maintenant = Game.GameTime;
            int avant = poursuite.FlicsVoulus;
            dernierDonateur = nom;
            bool debut = poursuite.Cadeau(maintenant);
            if (debut) Commencer();
            MettreLesEtoiles();
            prochainesEtoiles = maintenant + ETOILES_MS;

            journal.Info("Survie : " + (debut ? "DÉBUT — " : "") + "cadeau de " + nom + (string.IsNullOrEmpty(cadeau) ? "" : " (" + cadeau + ")")
                + " — chaleur " + poursuite.Chaleur + ", " + poursuite.FlicsVoulus + " flic(s), fin dans " + ReglesSurvie.Duree(poursuite.RestantMs(maintenant)));
            if (debut) Notifier("~r~SURVIE !~s~ " + nom + " lâche la police sur toi");
            else if (poursuite.FlicsVoulus > avant) Notifier("~r~+1 flic~s~ grâce à " + nom + " : " + poursuite.FlicsVoulus + " à tes trousses");
            else Notifier(nom + " : ~r~la police accélère !~s~");
        }

        void Commencer()
        {
            flics.Clear();
            numero = 0;
            contacts = 0;
            attrapeDepuis = 0;
            prochaineCreation = prochainPing = prochainEntretien = 0;
            prochainResume = Game.GameTime + RESUME_MS;
            // Aucune police du jeu pendant la poursuite : pas de renforts, d'hélicos,
            // de barrages ni de tirs. On retient le niveau max d'avant pour le rendre.
            maxEtoilesAvant = Function.Call<int>(Hash.GET_MAX_WANTED_LEVEL);
            Function.Call(Hash.CLEAR_PLAYER_WANTED_LEVEL, Game.Player.Handle);
            Function.Call(Hash.SET_MAX_WANTED_LEVEL, 0);
            DemanderModeles();
        }

        void MettreLesEtoiles()
        {
            Function.Call(Hash.SET_FAKE_WANTED_LEVEL, Math.Min(5, poursuite.Chaleur));
            if (Function.Call<int>(Hash.GET_MAX_WANTED_LEVEL) != 0) Function.Call(Hash.SET_MAX_WANTED_LEVEL, 0);   // une mission a pu le remettre
        }

        // ─── TICK (appelé toutes les 100 ms par GTAMax) ───────────────────
        public void Tick()
        {
            if (!poursuite.Active) return;
            int maintenant = Game.GameTime;
            Ped joueur = Game.Player.Character;

            string fin = RaisonDeFin(joueur, maintenant);
            if (fin != null) { Arreter(fin); return; }

            if (maintenant < prochainEntretien) return;
            prochainEntretien = maintenant + ENTRETIEN_MS;
            if (!GTAMax.JoueurDisponible()) return;   // chargement, cinématique : on attend

            Ranger(joueur, maintenant);
            Recruter(joueur, maintenant);
            foreach (FlicSurvie f in flics.ToArray())
            {
                try { Piloter(f, joueur, maintenant); }
                catch (Exception ex) { journal.ErreurLimitee("survie-flic", "Survie, flic " + f.Numero + " : " + ex.Message); }
            }
            if (maintenant >= prochainesEtoiles) { prochainesEtoiles = maintenant + ETOILES_MS; MettreLesEtoiles(); }
            if (maintenant >= prochainResume) { prochainResume = maintenant + RESUME_MS; Resumer(joueur, maintenant); }
        }

        string RaisonDeFin(Ped joueur, int maintenant)
        {
            if (joueur == null || !joueur.Exists() || joueur.IsDead) return "mort";
            if (Function.Call<bool>(Hash.GET_MISSION_FLAG)) return "mission";
            if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS)) return "changement de personnage";
            if (poursuite.Expiree(maintenant)) return "survécu";   // plus de cadeau depuis DUREE_S : il a tenu

            // À pied, un flic arrêté à moins de 8 m pendant 2 s : attrapé (Driver 2
            // le fait aussi quand Tanner sort de sa voiture).
            bool cerne = false;
            if (!joueur.IsInVehicle())
                foreach (FlicSurvie f in flics)
                    if (f.MorteA == 0 && f.Voiture.Exists() && f.Voiture.Speed < 3f && f.Voiture.Position.DistanceTo(joueur.Position) < DISTANCE_ATTRAPE_M)
                    { cerne = true; break; }
            if (!cerne) attrapeDepuis = 0;
            else if (attrapeDepuis == 0) attrapeDepuis = Math.Max(1, maintenant);
            else if (maintenant - attrapeDepuis >= ATTRAPE_MS) return "attrapé";
            return null;
        }

        // ─── FIN DE POURSUITE ─────────────────────────────────────────────
        public void Arreter(string raison)
        {
            if (!poursuite.Active) return;
            int duree = poursuite.DureeMs(Game.GameTime);
            poursuite.Terminer();
            foreach (FlicSurvie f in flics) Lacher(f);
            flics.Clear();
            LibererModeles();
            RendreLaPolice();

            bool compte = raison == "survécu" || raison == "attrapé" || raison == "mort";
            bool record = compte && duree > recordMs;
            if (record) { recordMs = duree; EcrireRecord(duree); }
            string temps = ReglesSurvie.Duree(duree);
            string suffixe = !compte ? "" : record ? " — ~y~nouveau record !~s~" : " (record : " + ReglesSurvie.Duree(recordMs) + ")";
            journal.Info("Survie : FIN (" + raison + ") — " + temps + ", " + contacts + " contact(s) des flics"
                + (record ? ", nouveau record" : compte ? ", record " + ReglesSurvie.Duree(recordMs) : ""));
            if (raison == "survécu") Notifier("~g~SURVÉCU !~s~ " + temps + " de poursuite" + suffixe);
            else if (raison == "attrapé") Notifier("~r~ATTRAPÉ !~s~ Survie : " + temps + suffixe);
            else if (raison == "mort") Notifier("~r~Survie terminée~s~ : " + temps + suffixe);
            else Notifier("Poursuite arrêtée (" + raison + ")");
        }

        // Script déchargé (Reload(), fermeture du jeu) : rien ne reste « à nous ».
        public void Liberer()
        {
            try
            {
                if (poursuite.Active) { poursuite.Terminer(); journal.Info("Survie : poursuite interrompue (script arrêté)"); }
                foreach (FlicSurvie f in flics) Lacher(f);
                flics.Clear();
                LibererModeles();
                RendreLaPolice();
            }
            catch (Exception) { }
        }

        void RendreLaPolice()
        {
            Function.Call(Hash.SET_FAKE_WANTED_LEVEL, 0);
            if (maxEtoilesAvant >= 0) Function.Call(Hash.SET_MAX_WANTED_LEVEL, maxEtoilesAvant == 0 ? 5 : maxEtoilesAvant);
            maxEtoilesAvant = -1;
        }

        // ─── LE PARC ──────────────────────────────────────────────────────
        // Flics que le jeu a ramassés, épaves (flics destructibles), voiture prise
        // par le joueur : retirés. Les manquants seront recréés par Recruter.
        void Ranger(Ped joueur, int maintenant)
        {
            foreach (FlicSurvie f in flics.ToArray())
            {
                if (!f.Voiture.Exists() || f.Voiture.Model.Hash != f.ModeleHash)
                {
                    journal.Info("Survie : flic " + f.Numero + " ramassé par le jeu");
                    flics.Remove(f);
                    continue;
                }
                if (joueur.IsInVehicle(f.Voiture))
                {
                    journal.Info("Survie : le joueur a pris la voiture du flic " + f.Numero);
                    Lacher(f);
                    flics.Remove(f);
                    continue;
                }
                if (f.MorteA == 0 && (f.Voiture.IsDead || !f.Voiture.IsDriveable)) f.MorteA = Math.Max(1, maintenant);
                if (f.MorteA != 0 && maintenant - f.MorteA > EPAVE_MS && !Function.Call<bool>(Hash.IS_ENTITY_ON_SCREEN, f.Voiture))
                {
                    journal.Info("Survie : épave du flic " + f.Numero + " retirée");
                    Supprimer(f);
                    flics.Remove(f);
                }
            }
        }

        void Recruter(Ped joueur, int maintenant)
        {
            if (flics.Count >= poursuite.FlicsVoulus || maintenant < prochaineCreation) return;
            if (!modelesDemandes) DemanderModeles();
            if (nomModele == null) return;
            if (!modeleVoiture.IsLoaded || !modeleAgent.IsLoaded)
            {
                if (maintenant - chargementDepuis > CHARGEMENT_MAX_MS)
                {
                    journal.Info("Survie : « " + nomModele + " » toujours pas chargé, nouvel essai");
                    LibererModeles();
                    prochaineCreation = maintenant + CREATION_MS;
                }
                return;
            }

            Vector3 position; float cap; string ou;
            if (!TrouverPoint(joueur, true, out position, out cap, out ou))
            {
                journal.ErreurLimitee("survie-point", "Survie : pas de route cachée autour du joueur pour un flic, nouvel essai");
                prochaineCreation = maintenant + NOUVEL_ESSAI_MS;
                return;
            }
            prochaineCreation = maintenant + CREATION_MS;
            Creer(joueur, position, cap, ou, maintenant);
        }

        void DemanderModeles()
        {
            if (!modelesVerifies)
            {
                modelesVerifies = true;
                foreach (string nom in modelesVoulus)
                {
                    var m = new Model(nom);
                    if (m.IsInCdImage && m.IsVehicle) { nomModele = nom; break; }
                    journal.Erreur("Survie : modèle « " + nom + " » absent du jeu ou pas un véhicule — ignoré");
                }
                if (nomModele == null) { journal.Erreur("Survie : aucun modèle de police valide dans [POLICE] MODELES"); return; }
                els = gereParEls(nomModele);
            }
            if (nomModele == null) return;
            modeleVoiture = new Model(nomModele);
            modeleAgent = new Model(GTAMax.PED_POLICE);
            modeleVoiture.Request();
            modeleAgent.Request();
            modelesDemandes = true;
            chargementDepuis = Game.GameTime;
        }

        // Modèles gardés en mémoire TOUTE la poursuite (Driver 2 : modèles
        // « résidents ») ; rendus au jeu à la fin.
        void LibererModeles()
        {
            if (!modelesDemandes) return;
            try { modeleVoiture.MarkAsNoLongerNeeded(); modeleAgent.MarkAsNoLongerNeeded(); }
            catch (Exception) { }
            modelesDemandes = false;
        }

        void Creer(Ped joueur, Vector3 position, float cap, string ou, int maintenant)
        {
            Vehicle v = Function.Call<Vehicle>(Hash.CREATE_VEHICLE, modeleVoiture.Hash, position.X, position.Y, position.Z, cap, false, false, false);
            if (v == null || v.Handle == 0 || !v.Exists()) { journal.Erreur("Survie : le jeu a refusé de créer « " + nomModele + " »"); return; }
            Function.Call<bool>(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, v, 5f);
            Function.Call(Hash.FREEZE_ENTITY_POSITION, v, false);
            Function.Call(Hash.SET_ENTITY_LOAD_COLLISION_FLAG, v, true);
            Function.Call(Hash.SET_VEHICLE_HANDBRAKE, v, false);
            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, v, true, true, false);
            if (indestructibles)
            {
                // Comme dans Driver 2 : les chocs cabossent, rien ne l'arrête.
                Function.Call(Hash.SET_ENTITY_INVINCIBLE, v, true, false);
                Function.Call(Hash.SET_VEHICLE_TYRES_CAN_BURST, v, false);
                Function.Call(Hash.SET_VEHICLE_WHEELS_CAN_BREAK, v, false);
            }
            else Function.Call(Hash.SET_VEHICLE_STRONG, v, true);

            Ped c = v.CreatePedOnSeat(VehicleSeat.Driver, modeleAgent);
            GTAMax.PreparerAgent(c);
            if (c != null && c.Exists())
            {
                Function.Call(Hash.REMOVE_ALL_PED_WEAPONS, c, true);                 // il percute, il ne tire pas
                Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, c, false);
                Function.Call(Hash.SET_PED_CAN_BE_KNOCKED_OFF_VEHICLE, c, 1);        // jamais éjecté
                if (indestructibles) Function.Call(Hash.SET_ENTITY_INVINCIBLE, c, true, false);
            }
            v.IsSirenActive = true;
            if (!els) v.IsSirenSilent = false;

            var f = new FlicSurvie
            {
                Numero = ++numero, Voiture = v, Conducteur = c, ModeleHash = v.Model.Hash,
                Donateur = dernierDonateur, NeeA = maintenant, DernierePoseA = maintenant,
            };
            flics.Add(f);
            AjusterPuissance(f, joueur);
            Suivre(f, position, joueur, ou, maintenant);
            Lancer(f, joueur, position);
            Ordonner(f, joueur, v.Position.DistanceTo(joueur.Position), !joueur.IsInVehicle());
            journal.Info("Survie : flic " + f.Numero + " apparu (" + flics.Count + "/" + poursuite.FlicsVoulus + ", cadeau de " + f.Donateur + ") — " + ou
                + ", moteur +" + Math.Round(f.Bonus) + " %");
        }

        // ─── OÙ FAIRE APPARAÎTRE ─────────────────────────────────────────
        // Joueur rapide : LA COURSE — sur la voie d'à côté, juste derrière lui (hors
        // de sa vue), dans son sens, lancé un peu plus vite que lui : à sa hauteur en
        // 1 à 2 s. À défaut, derrière lui sur sa route. Lent ou arrêté (ou rien de
        // libre) : une route tout autour (derrière d'abord). Toujours hors du champ de
        // la caméra, ou caché par le décor. Plus jamais posé devant, tourné vers lui :
        // c'étaient des face-à-face (test n°2).
        bool TrouverPoint(Ped joueur, bool nouveau, out Vector3 position, out float cap, out string ou)
        {
            position = Vector3.Zero; cap = 0f; ou = "";
            Vector3 pj = joueur.Position;
            Vehicle vj = joueur.IsInVehicle() ? joueur.CurrentVehicle : null;
            float vitesse = vj != null ? vj.Speed : 0f;
            Vector3 dir = vj != null && vitesse > 3f ? vj.Velocity : (vj != null ? vj.ForwardVector : joueur.ForwardVector);
            Vector3 cam = Function.Call<Vector3>(Hash.GET_FINAL_RENDERED_CAM_COORD);
            Vector3 rot = Function.Call<Vector3>(Hash.GET_FINAL_RENDERED_CAM_ROT, 2);

            if (vitesse >= VITESSE_RAPIDE_MS)
            {
                if (ACoteDeLui(pj, dir, cam, rot, out position, out cap, out ou)) return true;
                if (DerriereSurSaRoute(pj, dir, cam, rot, out position, out cap, out ou)) return true;
            }

            double cap0 = Math.Atan2(dir.Y, dir.X);
            foreach (float angle in ReglesSurvie.AnglesCandidats(cookie++, nouveau))
            {
                double a = cap0 + angle * Math.PI / 180.0;
                float d = rayonM * (0.9f + (float)hasard.NextDouble() * 0.3f);
                var cible = new Vector3(pj.X + (float)Math.Cos(a) * d, pj.Y + (float)Math.Sin(a) * d, pj.Z);

                var sortiePos = new OutputArgument();
                var sortieCap = new OutputArgument();
                if (!Function.Call<bool>(Hash.GET_NTH_CLOSEST_VEHICLE_NODE_FAVOUR_DIRECTION, cible.X, cible.Y, cible.Z, pj.X, pj.Y, pj.Z,
                        1, sortiePos, sortieCap, NOEUD_ROUTE_GOUDRONNEE, 3f, 0f)) continue;
                Vector3 n = sortiePos.GetResult<Vector3>();
                float h = sortieCap.GetResult<float>();
                if (n == Vector3.Zero) continue;

                float distance = new Vector3(n.X - pj.X, n.Y - pj.Y, 0f).Length();
                if (!ReglesSurvie.DistanceDApparitionOk(distance, n.Z - pj.Z, rayonM)) continue;
                if (PresDUnFlic(n, DISTANCE_ENTRE_FLICS_M)) continue;
                string vue;
                if (!Accepter(n, distance, cam, rot, out vue)) continue;
                position = n;
                cap = ReglesSurvie.CapDeRouteVers(h, n.X, n.Y, pj.X, pj.Y);   // le sens rendu peut lui tourner le dos
                ou = ReglesSurvie.Secteur(angle) + ", " + Math.Round(distance) + " m, " + vue;
                return true;
            }
            return false;
        }

        // La course : sur la voie d'à côté (4 ou 7,5 m sur le côté), 15 ou 25 m
        // derrière lui — sur la route, au même niveau, voie libre, hors de sa vue —,
        // tourné dans SON sens.
        bool ACoteDeLui(Vector3 pj, Vector3 dir, Vector3 cam, Vector3 rot, out Vector3 position, out float cap, out string ou)
        {
            position = Vector3.Zero; cap = 0f; ou = "";
            float l = (float)Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
            if (l < 0.01f) return false;
            float fx = dir.X / l, fy = dir.Y / l;
            float rx = fy, ry = -fx;   // sa droite : son « devant » tourné d'un quart de tour horaire
            float[] cotes = (cookie++ & 1) == 0 ? new[] { 4f, -4f, 7.5f, -7.5f } : new[] { -4f, 4f, -7.5f, 7.5f };
            foreach (float recul in new[] { A_COTE_RECUL_M, A_COTE_RECUL_M + 10f })
                foreach (float cote in cotes)
                {
                    float x = pj.X - fx * recul + rx * cote, y = pj.Y - fy * recul + ry * cote;
                    var sol = new OutputArgument();
                    if (!Function.Call<bool>(Hash.GET_GROUND_Z_FOR_3D_COORD, x, y, pj.Z + 5f, sol, false, false)) continue;
                    float z = sol.GetResult<float>();
                    if (Math.Abs(z - pj.Z) > 3f) continue;                                  // pas un autre étage (pont, talus)
                    if (!Function.Call<bool>(Hash.IS_POINT_ON_ROAD, x, y, z, 0)) continue;  // sur la route, pas dans le décor
                    var p = new Vector3(x, y, z);
                    if (PresDUnFlic(p, 8f) || World.GetNearbyVehicles(p, 3.5f).Length > 0) continue;   // voie libre
                    string vue;
                    if (!Accepter(p, recul, cam, rot, out vue)) continue;
                    position = p;
                    cap = ReglesSurvie.CapVers(0f, 0f, fx, fy);
                    ou = "à côté (" + (cote > 0 ? "à droite" : "à gauche") + ", " + recul + " m derrière), " + vue;
                    return true;
                }
            return false;
        }

        // À défaut : derrière lui sur sa route (le jeu prédit la route : FIND_SPAWN_POINT_IN_DIRECTION),
        // dans le sens de la route qui va vers lui, donc dans son sens à lui.
        bool DerriereSurSaRoute(Vector3 pj, Vector3 dir, Vector3 cam, Vector3 rot, out Vector3 position, out float cap, out string ou)
        {
            position = Vector3.Zero; cap = 0f; ou = "";
            float l = (float)Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
            if (l < 0.01f) return false;
            var sortie = new OutputArgument();
            if (!Function.Call<bool>(Hash.FIND_SPAWN_POINT_IN_DIRECTION, pj.X, pj.Y, pj.Z, -dir.X / l, -dir.Y / l, 0f, DISTANCE_DERRIERE_ROUTE_M, sortie)) return false;
            Vector3 p = sortie.GetResult<Vector3>();
            if (p == Vector3.Zero) return false;
            float distance = new Vector3(p.X - pj.X, p.Y - pj.Y, 0f).Length();
            if (distance < DISTANCE_DERRIERE_ROUTE_M * 0.5f || distance > DISTANCE_DERRIERE_ROUTE_M * 1.6f || Math.Abs(p.Z - pj.Z) > 12f) return false;
            if (PresDUnFlic(p, DISTANCE_ENTRE_FLICS_M)) return false;
            string vue;
            if (!Accepter(p, distance, cam, rot, out vue)) return false;

            float capRoute = ReglesSurvie.CapVers(p.X, p.Y, pj.X, pj.Y);
            var noeud = new OutputArgument();
            var capNoeud = new OutputArgument();
            if (Function.Call<bool>(Hash.GET_CLOSEST_VEHICLE_NODE_WITH_HEADING, p.X, p.Y, p.Z, noeud, capNoeud, 1, 3f, 0f)
                && noeud.GetResult<Vector3>().DistanceTo(p) < 15f)
                capRoute = capNoeud.GetResult<float>();
            position = p;
            cap = ReglesSurvie.CapDeRouteVers(capRoute, p.X, p.Y, pj.X, pj.Y);
            ou = "derrière, sur sa route, " + Math.Round(distance) + " m, " + vue;
            return true;
        }

        // Hors du champ de la caméra : oui. Dans le champ : seulement caché par le décor, à 60 m ou plus.
        bool Accepter(Vector3 p, float distance, Vector3 cam, Vector3 rot, out string vue)
        {
            bool dansLeChamp = ReglesSurvie.DansLeChamp(cam.X, cam.Y, cam.Z, rot.X, rot.Z, p.X, p.Y, p.Z + 1f, DEMI_CHAMP_DEG);
            bool cache = dansLeChamp && distance >= DISTANCE_MIN_CACHE_M
                && Function.Call<bool>(Hash.WOULD_ENTITY_BE_OCCLUDED, modeleVoiture.Hash, p.X, p.Y, p.Z + 1f, true);
            vue = !dansLeChamp ? "hors champ" : "caché par le décor";
            return ReglesSurvie.ApparitionAcceptee(dansLeChamp, cache, distance, DISTANCE_MIN_CACHE_M, float.MaxValue);
        }

        // Vitesse de pointe du flic : de quoi suivre la voiture du joueur (une supercar
        // distançait la voiture de police), jamais moins que BONUS_MOTEUR.
        void AjusterPuissance(FlicSurvie f, Ped joueur)
        {
            Vehicle vj = joueur.IsInVehicle() ? joueur.CurrentVehicle : null;
            float vmaxJoueur = vj != null ? Function.Call<float>(Hash.GET_VEHICLE_ESTIMATED_MAX_SPEED, vj) : 0f;
            float vmaxFlic = Function.Call<float>(Hash.GET_VEHICLE_MODEL_ESTIMATED_MAX_SPEED, f.ModeleHash);
            float bonus = ReglesSurvie.BonusPourSuivre(vmaxJoueur, vmaxFlic, bonusMoteur, BONUS_MAX);
            if (Math.Abs(bonus - f.Bonus) < 1f && f.Bonus > 0f) return;
            f.Bonus = bonus;
            if (bonus > 0f) Function.Call(Hash.MODIFY_VEHICLE_TOP_SPEED, f.Voiture, bonus);
        }

        // Contrôle 2 s après la pose : le journal dira s'il s'est rapproché (et donc s'il était bien tourné).
        static void Suivre(FlicSurvie f, Vector3 position, Ped joueur, string ou, int maintenant)
        {
            f.SuiviA = maintenant + SUIVI_MS;
            f.SuiviDistance = position.DistanceTo(joueur.Position);
            int virgule = ou.IndexOf(',');
            f.SuiviOu = virgule > 0 ? ou.Substring(0, virgule) : ou;
        }

        bool PresDUnFlic(Vector3 p, float distanceMin)
        {
            foreach (FlicSurvie f in flics)
                if (f.Voiture.Exists() && f.Voiture.Position.DistanceTo(p) < distanceMin) return true;
            return false;
        }

        // Juste posé : derrière le joueur, il part un peu plus vite que lui (à sa
        // hauteur en 1 à 2 s) ; ailleurs (joueur lent), il démarre.
        void Lancer(FlicSurvie f, Ped joueur, Vector3 position)
        {
            Vehicle vj = joueur.IsInVehicle() ? joueur.CurrentVehicle : null;
            if (vj == null) return;
            Vector3 rel = position - joueur.Position;
            bool derriere = Regles.EstDerriere(rel.X, rel.Y, vj.ForwardVector.X, vj.ForwardVector.Y);
            float vitesse = derriere ? Math.Min(vj.Speed + LANCER_PLUS_MS, VITESSE_MAX_MS) : VITESSE_LANCER_AUTRE_MS;
            if (vitesse > 3f) Function.Call(Hash.SET_VEHICLE_FORWARD_SPEED, f.Voiture, vitesse);
        }

        // ─── PILOTER UN FLIC (toutes les 250 ms) ──────────────────────────
        void Piloter(FlicSurvie f, Ped joueur, int maintenant)
        {
            if (f.MorteA != 0) return;
            Vehicle v = f.Voiture;
            Ped c = f.Conducteur;
            Vector3 pj = joueur.Position;
            float distance = v.Position.DistanceTo(pj);
            bool visible = Function.Call<bool>(Hash.IS_ENTITY_ON_SCREEN, v);   // dans le cadre, même derrière un mur
            bool aPied = !joueur.IsInVehicle();

            if (indestructibles && maintenant >= f.ProchaineReparation)
            {
                f.ProchaineReparation = maintenant + REPARATION_MS;
                if (Function.Call<float>(Hash.GET_VEHICLE_ENGINE_HEALTH, v) < 900f) Function.Call(Hash.SET_VEHICLE_ENGINE_HEALTH, v, 1000f);
                Function.Call(Hash.SET_VEHICLE_PETROL_TANK_HEALTH, v, 1000f);
            }

            if (!f.DiagnostiqueFait && maintenant - f.NeeA >= DIAGNOSTIC_MS)
            {
                f.DiagnostiqueFait = true;
                journal.Info("Survie : flic " + f.Numero + " à 5 s : " + Math.Round(v.Speed * 3.6f) + " km/h, " + Math.Round(distance) + " m du joueur, "
                    + (visible ? "visible" : "hors champ"));
            }
            if (f.SuiviA != 0 && maintenant >= f.SuiviA)
            {
                f.SuiviA = 0;
                journal.Info("Survie : flic " + f.Numero + ", 2 s après sa pose (" + f.SuiviOu + ") : " + Math.Round(f.SuiviDistance) + " → "
                    + Math.Round(distance) + " m, " + Math.Round(v.Speed * 3.6f) + " km/h" + (distance < f.SuiviDistance ? "" : " — S'ÉLOIGNE"));
            }
            bool contact = distance < DISTANCE_CONTACT_COMPTE_M;
            if (contact && !f.AuContact) { f.Contacts++; contacts++; }
            f.AuContact = contact;

            bool auVolant = c != null && c.Exists() && !c.IsDead && c.IsInVehicle(v);
            if (!auVolant)
            {
                // Éjecté malgré tout : remis au volant, hors de la vue du joueur.
                if (!visible && c != null && c.Exists() && !c.IsDead) Function.Call(Hash.SET_PED_INTO_VEHICLE, c, v, -1);
                return;
            }

            if (Regles.EstPlantee(v.Speed, distance, VITESSE_PLANTEE, DISTANCE_CONTACT_M))
            { if (f.PlanteeDepuis == 0) f.PlanteeDepuis = Math.Max(1, maintenant); }
            else
            {
                f.PlanteeDepuis = 0;
                if (v.Speed > 5f) f.Manoeuvres = 0;
            }
            int planteeMs = f.PlanteeDepuis == 0 ? 0 : maintenant - f.PlanteeDepuis;
            bool horsDeCourse = Function.Call<bool>(Hash.IS_ENTITY_UPSIDEDOWN, v) || Function.Call<bool>(Hash.IS_ENTITY_IN_WATER, v);

            Vector3 rel = v.Position - pj;
            Vector3 sens = aPied || joueur.CurrentVehicle.Speed < 3f ? joueur.ForwardVector : joueur.CurrentVehicle.Velocity;
            bool derriere = Regles.EstDerriere(rel.X, rel.Y, sens.X, sens.Y);
            string raison = ReglesSurvie.RaisonDeRecycler(visible, distance, derriere, planteeMs, horsDeCourse, SEME_DERRIERE_M, SEME_M, PLANTEE_MAX_MS);
            if (raison != null && maintenant >= prochainPing && maintenant - f.DernierePoseA >= RECYCLAGE_MIN_MS
                && Recycler(f, joueur, raison, maintenant)) return;

            if (maintenant < f.ManoeuvreJusquA) return;   // il recule : on le laisse finir
            if (visible && planteeMs >= PLANTEE_AVANT_MANOEUVRE_MS) { Manoeuvrer(f, maintenant); return; }

            if (!aPied) Rattraper(f, joueur, visible, distance, maintenant);
            if (maintenant >= f.ProchainOrdre)
            {
                Ordonner(f, joueur, distance, aPied);
                f.ProchainOrdre = maintenant + (distance <= COURSE_DISTANCE_M ? ORDRE_PRES_MS : ORDRE_LOIN_MS);
            }
            if (!aPied && distance < DISTANCE_COUP_M && maintenant >= f.ProchainCoup) Percuter(f, joueur, maintenant);
        }

        // De loin : il suit les routes vers l'endroit où sera le joueur. Au contact :
        // il vise son flanc, au-delà de lui, en ligne droite. Joueur à pied : il
        // s'approche sans l'écraser (Driver 2 : « puppy dog »).
        void Ordonner(FlicSurvie f, Ped joueur, float distance, bool aPied)
        {
            Vehicle v = f.Voiture;
            Vector3 pj = joueur.Position;
            if (aPied)
            {
                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, f.Conducteur, v, pj.X, pj.Y, pj.Z, VITESSE_A_PIED_MS, 1, f.ModeleHash,
                    CONDUITE_A_PIED, 6f, 1f);
                return;
            }
            Vehicle vj = joueur.CurrentVehicle;
            Vector3 vitJ = vj.Velocity, vitC = v.Velocity, pc = v.Position;

            // Joueur rapide, flic dans les parages : la course (ReglesSurvie.ConsigneCourse).
            // La vitesse de croisière suit celle du joueur : il remonte, se cale à sa
            // hauteur, serre, passe devant — sans jamais lui foncer dessus de face.
            if (vj.Speed >= VITESSE_RAPIDE_MS && distance <= COURSE_DISTANCE_M)
            {
                float cx, cy, vitesse;
                Vector3 droiteJ = vj.RightVector;
                f.Phase = ReglesSurvie.ConsigneCourse(pj.X, pj.Y, vitJ.X, vitJ.Y, pc.X, pc.Y, droiteJ.X, droiteJ.Y, out cx, out cy, out vitesse);
                int styleCourse = f.Phase == "à sa hauteur" ? CONDUITE_COUP_D_EPAULE : Regles.CONDUITE_TRAQUE;
                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, f.Conducteur, v, cx, cy, pj.Z, Math.Min(vitesse, VITESSE_MAX_MS), 1,
                    f.ModeleHash, styleCourse, 0.5f, 1f);
                return;
            }

            float x, y;
            int style;
            if (distance <= DISTANCE_PERCUTER_M)
            {
                float t = ReglesSurvie.TempsDInterception(pj.X - pc.X, pj.Y - pc.Y, vitJ.X - vitC.X, vitJ.Y - vitC.Y,
                    Regles.Anticipation(distance, v.Speed), INTERCEPTION_MAX_S);
                Vector3 droite = vj.RightVector;
                ReglesSurvie.PointVise(pj.X, pj.Y, vitJ.X, vitJ.Y, pc.X, pc.Y, t, droite.X, droite.Y, FLANC_M, DEPASSEMENT_M, out x, out y);
                style = Regles.StyleConduite(distance, DISTANCE_LIGNE_DROITE_M);
                f.Phase = "percute";
            }
            else
            {
                float t = Regles.Anticipation(distance, v.Speed);
                x = pj.X + vitJ.X * t; y = pj.Y + vitJ.Y * t;
                style = Regles.CONDUITE_TRAQUE;
                f.Phase = "traque";
            }
            Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, f.Conducteur, v, x, y, pj.Z, GTAMax.VITESSE_TRAQUE, 1, f.ModeleHash, style, 0.5f, 1f);
        }

        // Hors champ seulement : s'il roule vers le joueur mais moins vite que lui,
        // on lui redonne de l'élan. Le joueur ne voit jamais la triche.
        void Rattraper(FlicSurvie f, Ped joueur, bool visible, float distance, int maintenant)
        {
            if (visible || distance < DISTANCE_MIN_RATTRAPAGE_M || maintenant < f.ProchainCoup) return;
            Vehicle v = f.Voiture;
            if (!Function.Call<bool>(Hash.IS_VEHICLE_ON_ALL_WHEELS, v)) return;
            Vector3 vers = joueur.Position - v.Position;
            if (!Regles.EstFaceA(v.ForwardVector.X, v.ForwardVector.Y, vers.X, vers.Y, COS_RATTRAPAGE)) return;
            float cible = ReglesSurvie.VitesseRattrapage(joueur.CurrentVehicle.Speed, v.Speed, poursuite.FacteurRattrapage, VITESSE_MAX_MS);
            if (cible <= 0f) return;
            Function.Call(Hash.SET_VEHICLE_FORWARD_SPEED, v, cible);
            f.ProchainCoup = maintenant + COUP_MS;
        }

        // Face au joueur à moins de 15 m et pas plus vite que lui : coup d'accélérateur.
        void Percuter(FlicSurvie f, Ped joueur, int maintenant)
        {
            Vehicle v = f.Voiture;
            Vehicle vj = joueur.CurrentVehicle;
            Vector3 vers = joueur.Position - v.Position;
            float vitesseCible = vj.Speed;
            if (v.Speed >= vitesseCible + 3f) return;
            if (!Regles.EstFaceA(v.ForwardVector.X, v.ForwardVector.Y, vers.X, vers.Y, COS_FACE)) return;
            float nouvelle = Math.Min(vitesseCible + ELAN_COUP_MS + poursuite.Chaleur, VITESSE_MAX_COUP_MS);
            if (nouvelle <= v.Speed) return;   // jamais un « coup » qui le ralentirait
            Function.Call(Hash.SET_VEHICLE_FORWARD_SPEED, v, nouvelle);
            f.ProchainCoup = maintenant + COUP_MS;
        }

        // Planté sous les yeux du joueur : il recule en braquant, une fois à
        // gauche, une fois tout droit en force (Driver 2 alterne ses stratégies).
        void Manoeuvrer(FlicSurvie f, int maintenant)
        {
            int action = f.Manoeuvres % 2 == 0 ? MANOEUVRE_TOURNE_RECULE : MANOEUVRE_RECULE_FORT;
            Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, f.Conducteur, f.Voiture, action, MANOEUVRE_MS);
            f.Manoeuvres++;
            f.ManoeuvreJusquA = maintenant + MANOEUVRE_MS;
            f.ProchainOrdre = f.ManoeuvreJusquA;   // la manœuvre remplace sa conduite : on la redonne juste après
            f.PlanteeDepuis = 0;
            journal.Info("Survie : flic " + f.Numero + " planté sous les yeux du joueur : il recule (" + f.Manoeuvres + ")");
        }

        // Le « ping out / ping in » de Driver 2 : la même voiture, replacée
        // ailleurs, réparée (personne ne la voit), relancée vers le joueur.
        bool Recycler(FlicSurvie f, Ped joueur, string raison, int maintenant)
        {
            Vector3 position; float cap; string ou;
            if (!TrouverPoint(joueur, false, out position, out cap, out ou)) { prochainPing = maintenant + NOUVEL_ESSAI_MS / 2; return false; }
            Vehicle v = f.Voiture;
            Function.Call(Hash.SET_ENTITY_VELOCITY, v, 0f, 0f, 0f);
            Function.Call(Hash.SET_ENTITY_COORDS, v, position.X, position.Y, position.Z, false, false, false, false);
            Function.Call(Hash.SET_ENTITY_ROTATION, v, 0f, 0f, cap, 2, true);
            Function.Call<bool>(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, v, 5f);
            Function.Call(Hash.SET_VEHICLE_FIXED, v);
            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, v, true, true, false);
            v.IsSirenActive = true;
            if (f.Conducteur != null && f.Conducteur.Exists() && !f.Conducteur.IsDead && !f.Conducteur.IsInVehicle(v))
                Function.Call(Hash.SET_PED_INTO_VEHICLE, f.Conducteur, v, -1);
            AjusterPuissance(f, joueur);   // il a pu changer de voiture
            Lancer(f, joueur, position);
            f.PlanteeDepuis = 0; f.Manoeuvres = 0; f.ManoeuvreJusquA = 0; f.ProchainOrdre = 0;
            f.Recyclages++;
            f.DernierePoseA = maintenant;
            Suivre(f, position, joueur, ou, maintenant);
            prochainPing = maintenant + PING_MS;
            journal.Info("Survie : flic " + f.Numero + " " + raison + " → réapparaît " + ou + " (recyclage " + f.Recyclages + ")");
            return true;
        }

        void Resumer(Ped joueur, int maintenant)
        {
            var sb = new StringBuilder("Survie " + ReglesSurvie.Duree(poursuite.DureeMs(maintenant)) + " — chaleur " + poursuite.Chaleur
                + ", fin dans " + ReglesSurvie.Duree(poursuite.RestantMs(maintenant)) + ", " + flics.Count + "/" + poursuite.FlicsVoulus + " flic(s)");
            if (joueur.IsInVehicle()) sb.Append(", joueur à " + Math.Round(joueur.CurrentVehicle.Speed * 3.6f) + " km/h");
            else sb.Append(", joueur à pied");
            foreach (FlicSurvie f in flics)
            {
                if (!f.Voiture.Exists()) continue;
                AjusterPuissance(f, joueur);   // il a pu changer de voiture
                sb.Append(" | flic " + f.Numero + " : " + Math.Round(f.Voiture.Speed * 3.6f) + " km/h à " + Math.Round(f.Voiture.Position.DistanceTo(joueur.Position)) + " m, "
                    + f.Phase + ", " + f.Contacts + " contact(s), moteur +" + Math.Round(f.Bonus) + " %");
                if (f.MorteA != 0) sb.Append(" (épave)");
            }
            journal.Info(sb.ToString());
        }

        // Rendu au jeu : sirène coupée, balade tranquille, plus rien d'invincible ni de « à nous ».
        void Lacher(FlicSurvie f)
        {
            try
            {
                Vehicle v = f.Voiture;
                Ped c = f.Conducteur;
                if (v != null && v.Exists())
                {
                    if (c != null && c.Exists() && !c.IsDead && c.IsInVehicle(v))
                        Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, c, v, 15f, GTAMax.CONDUITE_NORMALE);
                    v.IsSirenActive = false;
                    Function.Call(Hash.SET_ENTITY_INVINCIBLE, v, false, false);
                    v.MarkAsNoLongerNeeded();
                }
                if (c != null && c.Exists()) Function.Call(Hash.SET_ENTITY_INVINCIBLE, c, false, false);
                GTAMax.LacherAgent(c);
            }
            catch (Exception ex) { journal.ErreurLimitee("survie-lacher", "Survie : libération d'un flic : " + ex.Message); }
        }

        void Supprimer(FlicSurvie f)
        {
            try
            {
                if (f.Conducteur != null && f.Conducteur.Exists()) f.Conducteur.Delete();
                if (f.Voiture != null && f.Voiture.Exists()) f.Voiture.Delete();
            }
            catch (Exception ex) { journal.ErreurLimitee("survie-supprimer", "Survie : suppression d'une épave : " + ex.Message); }
        }

        // Petit message à gauche de la mini-carte, une fois (pas à chaque image).
        static void Notifier(string texte)
        {
            try
            {
                Function.Call(Hash.BEGIN_TEXT_COMMAND_THEFEED_POST, "STRING");
                Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, texte);
                Function.Call(Hash.END_TEXT_COMMAND_THEFEED_POST_TICKER, false, true);
            }
            catch (Exception) { }
        }

        int LireRecord()
        {
            try
            {
                int ms;
                if (File.Exists(cheminRecord) && int.TryParse(File.ReadAllText(cheminRecord).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ms))
                    return Math.Max(0, ms);
            }
            catch (Exception) { }
            return 0;
        }

        void EcrireRecord(int ms)
        {
            try { File.WriteAllText(cheminRecord, ms.ToString(CultureInfo.InvariantCulture), new UTF8Encoding(false)); }
            catch (Exception ex) { journal.ErreurLimitee("survie-record", "Survie : record non enregistré : " + ex.Message); }
        }
    }
#endif
}
