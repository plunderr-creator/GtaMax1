/* Poursuites directes, fuite et records. Un conducteur par voiture. */
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
    /// cadeau et fixe le nombre de flics ; la poursuite
    /// s'arrête dureeMs après le DERNIER cadeau. Temps = Game.GameTime (ms).
    /// </summary>
    public sealed class ProfilViewer
    {
        public int Total, Depenses, Moteur, Freins, Transmission;
        public string Modele = "municipale";
        public int Boosts, Barrages;    // sorts achetés, pas encore lancés
    }

    // Un seul proprietaire (le jeu) pour les gains, achats et sauvegardes.
    public sealed class ScoresImpact
    {
        readonly Dictionary<string, ProfilViewer> valeurs = new Dictionary<string, ProfilViewer>(StringComparer.OrdinalIgnoreCase);
        public static readonly int[] Seuils = { 0, 5, 15, 30, 60, 100 };
        // Un modèle par niveau (choix d'Elliot 2026-09-28) : Municipale, vraie police GTA, puis de plus en plus rapide, scooter stunt en dernier.
        public static readonly string[] Modeles = { "municipale", "police", "golf", "rs3", "m5", "scooter" };
        public static readonly string[] Vehicules = { null, "police", "mk7", "cmstrs3", "bmci", "mbkstunt" };   // null = [POLICE] MODELES de l'ini
        public static string Vehicule(string modele) { int i = Array.IndexOf(Modeles, modele); return i < 0 ? null : Vehicules[i]; }
        public ProfilViewer Profil(string nom)
        {
            ProfilViewer p;
            return valeurs.TryGetValue(nom, out p) ? p : new ProfilViewer();
        }
        public int Total(string nom) { return Profil(nom).Total; }
        public int Solde(string nom) { var p = Profil(nom); return p.Total - p.Depenses; }
        public static int NiveauPour(int total)
        {
            int n = 1;
            while (n < Seuils.Length && total >= Seuils[n]) n++;
            return n;
        }
        public int Niveau(string nom) { return NiveauPour(Total(nom)); }
        // Cadeau TikTok : 1 point par pièce offerte.
        public bool Offrir(string nom, int points)
        {
            if (points <= 0 || string.IsNullOrWhiteSpace(nom)) return false;
            var p = Profil(nom);
            p.Total = (int)Math.Min(int.MaxValue, (long)p.Total + points);
            valeurs[nom] = p;
            return true;
        }
        public bool Toucher(string nom, bool contact, ref bool dejaMarque)
        {
            if (!contact || dejaMarque || string.IsNullOrWhiteSpace(nom)) return false;
            dejaMarque = true;
            var p = Profil(nom);
            if (p.Total == int.MaxValue) return false;
            p.Total++;
            valeurs[nom] = p;
            return true;
        }
        // !ok lvlup : un rang de plus pour moteur, transmission ET freins d'un coup.
        public static int Rang(ProfilViewer p) { return Math.Max(p.Moteur, Math.Max(p.Freins, p.Transmission)); }
        public string Lvlup(string nom)
        {
            var p = Profil(nom);
            int rang = Rang(p);
            if (rang >= 3) return "Ta voiture est deja au maximum (rang 3).";
            if (Niveau(nom) < rang + 2) return "Niveau " + (rang + 2) + " requis.";
            int prix = new[] { 3, 8, 15 }[rang];
            if (Solde(nom) < prix) return "Il te faut " + prix + " points disponibles.";
            p.Depenses += prix;
            p.Moteur = p.Freins = p.Transmission = rang + 1;
            valeurs[nom] = p;
            return null;
        }
        // Sorts du chat : ils coutent du solde, jamais du total ni du niveau.
        public const int PRIX_BARRAGE = 5, NIVEAU_BARRAGE = 2;
        public const int PRIX_BOOST = 3, NIVEAU_BOOST = 1;
        public const int STOCK_MAX = 9;
        // !ok acheter boost|barrage : le sort part en stock, lancé plus tard sans rien payer.
        public string Acheter(string nom, string sort)
        {
            bool boost = sort == "boost";
            if (!boost && sort != "barrage") return "A acheter : boost (" + PRIX_BOOST + " pts) ou barrage (" + PRIX_BARRAGE + " pts, niv. " + NIVEAU_BARRAGE + ").";
            var p = Profil(nom);
            if ((boost ? p.Boosts : p.Barrages) >= STOCK_MAX) return "Tu en as deja " + STOCK_MAX + ", lance-en d'abord.";
            string erreur = Depenser(nom, boost ? PRIX_BOOST : PRIX_BARRAGE, boost ? NIVEAU_BOOST : NIVEAU_BARRAGE);
            if (erreur != null) return erreur;
            p = Profil(nom);
            if (boost) p.Boosts++; else p.Barrages++;
            valeurs[nom] = p;
            return null;
        }
        public bool Utiliser(string nom, string sort)
        {
            var p = Profil(nom);
            if (sort == "boost" ? p.Boosts <= 0 : p.Barrages <= 0) return false;
            if (sort == "boost") p.Boosts--; else p.Barrages--;
            valeurs[nom] = p;
            return true;
        }
        public string Depenser(string nom, int prix, int niveauMin)
        {
            if (Niveau(nom) < niveauMin) return "Niveau " + niveauMin + " requis.";
            if (Solde(nom) < prix) return "Il te faut " + prix + " points disponibles.";
            var p = Profil(nom); p.Depenses += prix; valeurs[nom] = p;
            return null;
        }
        public string Choisir(string nom, string modele)
        {
            int indice = Array.IndexOf(Modeles, modele);
            if (indice < 0) return "Modele inconnu : !ok voitures pour la liste.";
            if (Niveau(nom) <= indice) return "Ce modele se debloque au niveau " + (indice + 1) + ".";
            var p = Profil(nom); p.Modele = modele; valeurs[nom] = p;
            return null;
        }
        public string Exporter()
        {
            var texte = new StringBuilder();
            foreach (var entree in valeurs)
            {
                var p = entree.Value;
                texte.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(entree.Key))).Append(':')
                    .Append(p.Total).Append(':').Append(p.Depenses).Append(':').Append(p.Moteur).Append(':')
                    .Append(p.Freins).Append(':').Append(p.Transmission).Append(':').Append(p.Modele).Append(':').Append(p.Boosts).Append(':').Append(p.Barrages).Append('\n');
            }
            return texte.ToString();
        }
        public void Importer(string texte)
        {
            var lues = new Dictionary<string, ProfilViewer>(StringComparer.OrdinalIgnoreCase);
            foreach (string ligne in texte.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(ligne)) continue;
                string[] c = ligne.Trim().Split(':'); int total;
                if ((c.Length != 2 && c.Length != 7 && c.Length != 9) || !int.TryParse(c[1], out total) || total < 0)
                    throw new FormatException("Ligne de progression invalide.");
                string nom = Encoding.UTF8.GetString(Convert.FromBase64String(c[0]));
                if (string.IsNullOrWhiteSpace(nom)) throw new FormatException("Viewer absent.");
                var p = new ProfilViewer { Total = total };
                if (c.Length >= 7)
                {
                    if (!int.TryParse(c[2], out p.Depenses) || p.Depenses < 0 || p.Depenses > total
                        || !int.TryParse(c[3], out p.Moteur) || p.Moteur < 0 || p.Moteur > 3
                        || !int.TryParse(c[4], out p.Freins) || p.Freins < 0 || p.Freins > 3
                        || !int.TryParse(c[5], out p.Transmission) || p.Transmission < 0 || p.Transmission > 3)
                        throw new FormatException("Achat sauvegarde invalide.");
                    // Ancien catalogue (origine, police2, fbi2...) ou modèle retiré : retour à la Municipale.
                    int indice = Array.IndexOf(Modeles, c[6]);
                    p.Modele = indice >= 0 && indice < NiveauPour(total) ? c[6] : Modeles[0];
                }
                if (c.Length == 9 && (!int.TryParse(c[7], out p.Boosts) || p.Boosts < 0 || p.Boosts > STOCK_MAX
                    || !int.TryParse(c[8], out p.Barrages) || p.Barrages < 0 || p.Barrages > STOCK_MAX))
                    throw new FormatException("Sorts sauvegardes invalides.");
                lues[nom] = p;
            }
            valeurs.Clear();
            foreach (var entree in lues) valeurs.Add(entree.Key, entree.Value);
        }
    }


    public sealed class Poursuite
    {
        readonly int maxFlics, dureeMs, chaleurMax, depart, renfort;
        bool active;
        int chaleur, debutA, finA;

        public Poursuite(int maxFlics, int dureeMs, int chaleurMax, int depart = 1, int renfort = 1)
        {
            this.depart = Math.Max(1, depart);
            this.renfort = Math.Max(1, renfort);
            this.maxFlics = Math.Max(1, maxFlics);
            this.dureeMs = Math.Max(1000, dureeMs);
            this.chaleurMax = Math.Max(1, chaleurMax);
        }

        public bool Active { get { return active; } }
        public int Chaleur { get { return chaleur; } }
        public int DebutA { get { return debutA; } }
        public int FinA { get { return finA; } }
        public int FlicsVoulus { get { return active ? Math.Min(maxFlics, chaleur) : 0; } }

        // true si ce cadeau COMMENCE une poursuite.
        public bool Cadeau(int maintenant)
        {
            bool debut = !active;
            if (debut) { active = true; debutA = maintenant; chaleur = 0; }
            chaleur = Math.Min(chaleurMax, chaleur + (debut ? depart : renfort));
            finA = maintenant + dureeMs;
            return debut;
        }

        // Un flic semé abandonne (test n°5 : « il faut que je puisse les semer ») : un flic de
        // moins à ses trousses, sans remplaçant. Rend la chaleur restante ; 0 = tous semés.
        public int Semer()
        {
            chaleur = Math.Max(0, Math.Min(chaleur, maxFlics) - 1);
            return chaleur;
        }

        // Sort « barrage » : n flics de plus d'un coup, lance la poursuite si besoin.
        public bool Renforcer(int maintenant, int n)
        {
            bool debut = !active;
            if (debut) { active = true; debutA = maintenant; chaleur = 0; }
            chaleur = Math.Min(chaleurMax, chaleur + Math.Max(1, n));
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
        // Taille lisible près du joueur et discrète quand la voiture s'éloigne.
        public static float EchelleNom(float distanceM)
        {
            return Math.Max(0.30f, Math.Min(1f, 18f / Math.Max(18f, distanceM)));
        }

        // Semé (test n°5 : « il faut que je puisse les semer ») : hors de sa vue, et loin
        // derrière lui, ou très loin partout. Loin DEVANT, il arrive sur lui : pas semé.
        // Survie le fait abandonner s'il le reste 12 s.
        public static bool EstSeme(bool visible, float distanceM, bool derriereLeJoueur, float semeDerriereM, float semeM)
        {
            return !visible && (distanceM > semeM || (derriereLeJoueur && distanceM > semeDerriereM));
        }

        public static string SortDuFlicLoin(bool visible, float distanceM, bool derriereLeJoueur, bool venuAuContact, float semeDerriereM, float semeM)
        {
            if (!EstSeme(visible, distanceM, derriereLeJoueur, semeDerriereM, semeM)) return null;
            return venuAuContact ? "semé" : "perdu de vue";
        }

        // Vitesse de rattrapage, toujours supérieure à celle du joueur.
        public static float VitesseDePoursuite(float vitesseJoueurMs)
        { return Math.Min(85f, Math.Max(30f, vitesseJoueurMs + 18f)); }

        // Un bonus limité aide la voiture de police à suivre un véhicule plus rapide.
        public static float BonusVitesse(float vitesseJoueurMax, float vitessePoliceMax)
        {
            // 2026-09-28 « trop facile » : plancher 75 → 100 %, marge 1,35 → 1,5, plafond 150 → 180 %.
            if (vitesseJoueurMax <= 1f || vitessePoliceMax <= 1f) return 100f;
            return Math.Min(180f, Math.Max(100f, (vitesseJoueurMax * 1.5f / vitessePoliceMax - 1f) * 100f));
        }

        // Une patrouille devant reste rattrapable, puis devient poursuivante au dépassement.
        public static float VitessePatrouille(float joueurMs)
        { return Math.Min(40f, Math.Max(4f, joueurMs * 0.65f)); }

        public static bool PatrouilleDepassee(float relX, float relY, float avantX, float avantY)
        {
            // Un cap qui tourne ne constitue pas un dépassement à 110 ou 181 m.
            float distance2 = relX * relX + relY * relY;
            float norme = (float)Math.Sqrt(avantX * avantX + avantY * avantY);
            return distance2 <= 35f * 35f && norme > 0.1f
                && (relX * avantX + relY * avantY) / norme <= 6f;
        }

        public static bool ReprendreChasse(bool active, int ancienneCible, int cible, int etatTache)
        { return !active || ancienneCible != cible || etatTache == 3 || etatTache == 7; }

        // Trois places seulement : gauche, droite, devant. Les autres poursuivent.
        public static bool PlacementLateralOk(int role, float longitudinal, float lateral, float hauteur, float alignement, float distance)
        {
            return (role == 1 || role == 2) && (role == 1 ? lateral <= -12f : lateral >= 12f)
                && Math.Abs(lateral) <= 80f && longitudinal >= -20f && longitudinal <= 110f
                && Math.Abs(hauteur) <= 6f && alignement >= 0.85f && distance >= 45f;
        }

        // 0 = attente bornée, 1 = tentative, 2 = abandon définitif.
        public static int DecisionEmbuscade(bool utilisee, bool enVoiture, int attenteMs, bool libre, bool delaiEcoule)
        {
            if (utilisee || !enVoiture || attenteMs >= 20000) return 2;
            return libre && delaiEcoule ? 1 : 0;
        }

        public static int ChoisirRole(int actuel, float distance, bool gauche, bool droite, bool devant)
        {
            if (distance > 220f) return 0;
            if (actuel > 0) return actuel;
            if (distance > 180f) return 0;
            return !gauche ? 1 : !droite ? 2 : !devant ? 3 : 0;
        }

        public static bool OccasionContact(int role, float longitudinal, float lateral, float hauteur, float vitesseJoueur)
        {
            if (role == 0 || Math.Abs(hauteur) > 4f || vitesseJoueur < 5f) return false;
            return role == 3 ? longitudinal >= 6f && longitudinal <= 22f && Math.Abs(lateral) < 6f
                : Math.Abs(longitudinal) < 12f && Math.Abs(lateral) < 7f;
        }

        public static int ModePlacement(int role) { return role == 3 ? 0 : role; }

        public static float CoupleRattrapage(bool patrouille, float vitesse, float vitesseJoueur,
                                            float distance, float alignementTrajet, bool derriere, bool stable)
        {
            // Le moteur reçoit son surcroît de couple chaque image, sans imposer de vitesse ni déplacer la voiture.
            if (patrouille || !stable || !derriere || vitesse < 3f || vitesseJoueur < 10f
                || distance < 25f || distance > 420f || alignementTrajet < 0.65f
                || vitesse >= vitesseJoueur + 10f) return 1f;
            return distance >= 120f ? 1.8f : 1.6f;
        }

        public static bool EstPoliceNative(int typePed)
        { return typePed == 6 || typePed == 27 || typePed == 29; }

        public static int NiveauPoliceNative(int chaleur)
        {
            if (chaleur <= 0) return 0;
            if (chaleur <= 6) return 2;
            if (chaleur <= 10) return 3;
            if (chaleur <= 15) return 4;
            return 5;
        }

        public static bool DoitIntercepter(bool visible, bool enVehicule, bool derriere, float distance, int retardMs)
        { return !visible && enVehicule && derriere && distance >= 145f && retardMs >= 3000; }

        public static bool ContinuiteRoute(float avance, float lateral, float hauteur, float alignement)
        { return avance >= 8f && avance <= 32f && Math.Abs(lateral) <= 8f && Math.Abs(hauteur) <= 4f && alignement >= 0.85f; }

        // Quand la cible est de côté ou derrière, ralentir assez pour négocier le virage.
        public static float VitesseEnVirage(float demande, float avantX, float avantY, float cibleX, float cibleY)
        {
            double direction = Math.Sqrt(avantX * avantX + avantY * avantY);
            double cible = Math.Sqrt(cibleX * cibleX + cibleY * cibleY);
            if (direction < 0.1 || cible < 1.0) return demande;
            double cosinus = (avantX * cibleX + avantY * cibleY) / (direction * cible);
            return cosinus < 0.0 ? Math.Min(demande, 16f) : cosinus < 0.7 ? Math.Min(demande, 24f) : demande;
        }

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
        // joueur ; seulement s'il est retourné ou à l'eau, ou planté trop longtemps. Un
        // flic semé n'est plus recyclé, il abandonne (test n°5, EstSeme).
        // null = on le laisse chasser.
        public static string RaisonDeRecycler(bool visible, int planteeMs, bool horsDeCourse, int planteeMaxMs)
        {
            if (visible) return null;
            if (horsDeCourse) return "retourné ou à l'eau";
            if (planteeMs >= planteeMaxMs) return "planté";
            return null;
        }

        // Viser juste après la position future du joueur pour chercher le contact.
        public static void CiblePoursuite(float jx, float jy, float vjx, float vjy, float cx, float cy, float anticipationS, out float x, out float y)
        {
            x = jx + vjx * anticipationS;
            y = jy + vjy * anticipationS;
            float dx = x - cx, dy = y - cy;
            float distance = (float)Math.Sqrt(dx * dx + dy * dy);
            if (distance > 0.1f) { x += dx / distance * 6f; y += dy / distance * 6f; }
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
        public bool PointMarque;
        public int MorteA;              // épave (flics destructibles seulement) ; 0 = roule
        public int PlanteeDepuis;       // 0 = elle roule (ou elle est au contact)
        public int ManoeuvreJusquA;     // marche arrière de dégagement en cours jusqu'à…
        public int Manoeuvres;          // dégagements depuis la dernière fois qu'elle a roulé
        public int ProchainOrdre, ProchaineReparation;
        public float BonusVitesse;
        public int BoostJusqua;         // sort « boost » du viewer : vitesse et couple en plus jusqu'à…
        public bool PatrouilleDevant, OrdrePatrouille, ChasseActive;
        public int CibleChasse;
        public int Role, OrdreTactique = -1, VehiculeCible, ContactJusqua, ProchainContact;
        public bool PhaseContact;
        public bool EmbuscadeUtilisee;
        public int AttenteEmbuscadeA;
        public int Assistances, RefusAlignement, RefusGlisse;
        public float Couple = 1f;
        public Vector3 SensPatrouille;
        public int PatrouilleDepuis;
        public int Recyclages;
        public int DernierePoseA;       // apparition ou dernier recyclage : pas de nouveau recyclage avant 10 s
        public int SemeDepuis;          // loin et hors de vue depuis… (0 = non) : 12 s → il abandonne
        public int RetardDepuis;        // loin derrière et invisible : replacé devant après 3 s
        public bool VenuAuContact;      // à 25 m du joueur ou moins depuis sa pose : lui seul peut être semé
    }

    /// <summary>Point de route où faire apparaître un flic.</summary>
    sealed class PoseFlic
    {
        public Vector3 Position;
        public float Cap;
        public string Ou = "";
        public bool Devant;
        public int Role;
    }

    /// <summary>Le mode survie en jeu. Appelé par GTAMax : Cadeau(), Tick(), Liberer().</summary>
    sealed class Survie
    {
        // ── Réglages fixes (les réglables sont dans GTAMax.ini, [SURVIE]) ──
        const int CHALEUR_MAX = 5;
        const float DEMI_CHAMP_DEG = 60f;           // cône de la caméra, large marge comprise
        const float DISTANCE_MIN_CACHE_M = 60f;      // dans le cône : seulement caché par le décor, et loin
        const float DISTANCE_ENTRE_FLICS_M = 20f;
        // Après un passage à moins de 25 m : abandon à 260 m derrière ou 380 m ailleurs, hors écran pendant 12 s.
        const float SEME_DERRIERE_M = 260f, SEME_M = 380f;
        const int SEME_MS = 12000;
        const int RECYCLAGE_MIN_MS = 10000;           // un même flic n'est pas replacé plus d'une fois toutes les 10 s
        const int PLANTEE_MAX_MS = 2500;             // plantée hors champ : recyclée
        const int PLANTEE_AVANT_MANOEUVRE_MS = 1200; // plantée sous les yeux du joueur : elle recule en braquant
        const float VITESSE_PLANTEE = 1.5f;          // m/s
        const float DISTANCE_CONTACT_M = 12f;        // au contact, être arrêté est normal : elle vient de percuter
        const int MANOEUVRE_MS = 1200;
        const int MANOEUVRE_TOURNE_RECULE = 13, MANOEUVRE_RECULE_FORT = 28;   // TASK_VEHICLE_TEMP_ACTION ; 16 à 18 font planter le jeu : jamais
        const int ORDRE_MS = 500;
        const int PING_MS = 500;                     // un recyclage toutes les 0,5 s au plus, tous flics confondus
        const int CREATION_MS = 1500;                // une création toutes les 1,5 s au plus (12 flics depuis le test n°5)
        const int NOUVEL_ESSAI_MS = 500;             // pas de route cachée trouvée : on réessaie dans 0,5 s
        const int CHARGEMENT_MAX_MS = 5000;
        const int ENTRETIEN_MS = 250;
        const int REPARATION_MS = 2000;
        const int ETOILES_MS = 5000;
        const int DESARMEMENT_MS = 500;
        const float RAYON_DESARMEMENT_M = 350f;
        const int ARME_A_FEU_OU_EXPLOSIF = 6;
        // Hélicos police/SWAT, SWAT, barrages avec herses, bateaux et armée.
        static readonly int[] DISPATCH_INTERDITS = { 2, 4, 8, 12, 13, 14 };
        const int EPAVE_MS = 8000;
        const float VITESSE_A_PIED_MS = 14f, DISTANCE_ATTRAPE_M = 8f;
        const int ATTRAPE_MS = 2000;
        // À pied, le flic s'approche sans l'écraser : il contourne le joueur (bit 64 absent) et les piétons (16).
        const int CONDUITE_PATROUILLE = 4 | 16 | 32 | 524288 | 1073741824;
        const int CONDUITE_A_PIED = 4 | 16 | 32 | 512 | 262144 | 524288;
        const int NOEUD_ROUTE_GOUDRONNEE = 0;        // GET_NTH_CLOSEST_VEHICLE_NODE_FAVOUR_DIRECTION

        readonly Journal journal;
        readonly Func<string, bool> gereParEls;
        readonly string cheminRecord;
        readonly Poursuite poursuite;
        readonly List<FlicSurvie> flics = new List<FlicSurvie>();
        readonly Queue<string> donateursEnAttente = new Queue<string>();
        readonly Queue<string> voituresEnAttente = new Queue<string>();   // cadeau de test : voiture imposée ("" = celle du viewer)
        readonly List<string> modelesVoulus;
        readonly float rayonM;
        readonly bool indestructibles;

        string nomModele;               // premier modèle valide de [POLICE] MODELES
        readonly List<string> nomsVoitures = new List<string>();   // tous les modèles valides, pris tour à tour (BAC en voitures FR, 2026-09-24)
        int prochaineVoiture;
        string nomPed;                  // [POLICE] PED (s_m_y_cop_01 par défaut)
        bool modelesVerifies, els;
        Model modeleVoiture, modeleAgent;
        bool modelesDemandes;
        int chargementDepuis;
        int prochaineCreation, prochainPing, prochainEntretien;
        int attrapeDepuis, prochainDiagnostic, prochaineEmbuscade, prochainEssaiEmbuscade;
        int policeLanceeA, prochainDesarmement, policiersDesarmes;
        int prochaineInterception;
        int cookie, numero;
        int maxEtoilesAvant = -1;       // niveau de recherche max d'avant la poursuite, rendu à la fin
        int recordMs;
        string dernierDonateur = "";
        readonly ScoresImpact scores = new ScoresImpact();
        readonly string cheminScores;
        bool scoresModifies;
        int sauvegardeScoresA, pointJusqua;
        string pointNom = "";

        readonly Queue<string> retoursGarage = new Queue<string>();
        bool progressionLisible = true;
        int numeroRetour;

        // Sorts actifs pour l'overlay : « id:pseudo base64:sort:durée ms », 20 dernières lignes.
        readonly Queue<string> sortsLances = new Queue<string>();
        void AnnoncerSort(string nom, string sort, int dureeMs)
        {
            try
            {
                string id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) + "-" + (++numeroRetour);
                sortsLances.Enqueue(id + ":" + Convert.ToBase64String(Encoding.UTF8.GetBytes(nom)) + ":" + sort + ":" + dureeMs.ToString(CultureInfo.InvariantCulture));
                while (sortsLances.Count > 20) sortsLances.Dequeue();
                string chemin = cheminScores + ".sorts";
                File.WriteAllText(chemin + ".tmp", string.Join("\n", sortsLances.ToArray()), new UTF8Encoding(false));
                if (File.Exists(chemin)) File.Replace(chemin + ".tmp", chemin, null);
                else File.Move(chemin + ".tmp", chemin);
            }
            catch (Exception ex) { journal.ErreurLimitee("sort-overlay", ex.Message); }
        }

        void RetourGarage(string nom, string texte)
        {
            string message = nom + " : " + texte;
            Notifier(ReglesSurvie.TexteJeu(message, 250));
            try
            {
                string id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) + "-" + (++numeroRetour);
                retoursGarage.Enqueue(id + ":" + Convert.ToBase64String(Encoding.UTF8.GetBytes(message)));
                while (retoursGarage.Count > 20) retoursGarage.Dequeue();
                string chemin = cheminScores + ".messages";
                File.WriteAllText(chemin + ".tmp", string.Join("\n", retoursGarage.ToArray()), new UTF8Encoding(false));
                if (File.Exists(chemin)) File.Replace(chemin + ".tmp", chemin, null);
                else File.Move(chemin + ".tmp", chemin);
            }
            catch (Exception ex) { journal.ErreurLimitee("garage-retour", ex.Message); }
        }

        public void Garage(string nom, string action, string valeur)
        {
            if (string.IsNullOrWhiteSpace(nom) || nom.Length > 60) return;
            if (!progressionLisible) { RetourGarage(nom, "Sauvegarde illisible, garage suspendu."); return; }
            var p = scores.Profil(nom);
            if (action == "garage" || action == "points" || action == "niveau")
            {
                RetourGarage(nom, "Niv. " + scores.Niveau(nom) + " | " + scores.Solde(nom) + " disponibles / " + p.Total
                    + " cumules | " + p.Modele + " | voiture rang " + ScoresImpact.Rang(p) + "/3");
                return;
            }
            if (action == "voitures")
            {
                RetourGarage(nom, "municipale (niv.1), police (niv.2), golf (niv.3), rs3 (niv.4), m5 (niv.5), scooter (niv.6). !ok voiture modele");
                return;
            }
            if (action == "aide")
            {
                RetourGarage(nom, "!ok garage | !ok voitures | !ok voiture modele | !ok lvlup (moteur+transmission+freins) : rangs 1/2/3 = 3/8/15 pts, niveaux 2/3/4. | !ok acheter barrage (5 pts, niv. 2) puis !ok barrage | !ok acheter boost (3 pts) puis !ok boost : ta voiture +vitesse 15 s.");
                return;
            }
            if (action == "sort") { LancerSort(nom, valeur); return; }
            if (action != "lvlup" && action != "voiture" && action != "acheter") return;
            if (action == "voiture" && ScoresImpact.Vehicule(valeur) != null)
            {
                var modele = new Model(ScoresImpact.Vehicule(valeur));
                if (!modele.IsInCdImage || !modele.IsVehicle) { RetourGarage(nom, "Ce modele n'est pas installe."); return; }
            }
            string avant = scores.Exporter();
            string erreur = action == "lvlup" ? scores.Lvlup(nom) : action == "acheter" ? scores.Acheter(nom, valeur) : scores.Choisir(nom, valeur);
            if (erreur != null) { RetourGarage(nom, erreur); return; }
            scoresModifies = true;
            sauvegardeScoresA = 0;
            SauverScores();
            if (scoresModifies)
            {
                scores.Importer(avant);
                RetourGarage(nom, "Sauvegarde impossible : achat ou changement annule, points conserves.");
                return;
            }
            foreach (var f in flics)
                if (string.Equals(f.Donateur, nom, StringComparison.OrdinalIgnoreCase) && f.Voiture != null && f.Voiture.Exists())
                    AppliquerFreins(f);
            if (action == "acheter")
            {
                var achat = scores.Profil(nom);
                RetourGarage(nom, (valeur == "boost" ? "BOOST achete ! !ok boost pour le lancer (" + achat.Boosts : "BARRAGE achete ! !ok barrage pour le lancer (" + achat.Barrages)
                    + " en stock). " + scores.Solde(nom) + " points disponibles.");
                return;
            }
            RetourGarage(nom, action == "lvlup"
                ? "LVL UP ! Voiture rang " + ScoresImpact.Rang(scores.Profil(nom)) + " : moteur, transmission et freins. " + scores.Solde(nom) + " points disponibles."
                : valeur + " selectionne pour ta prochaine voiture.");
        }

        // ─── SORTS DU CHAT ────────────────────────────────────────────────
        const int VOITURES_BARRAGE = 3;
        const int BOOST_MS = 15000;
        const float BOOST_VITESSE = 30f, BOOST_COUPLE = 0.6f;
        int barrageRestant;             // voitures du sort à créer d'un coup, sans attendre CREATION_MS

        void LancerSort(string nom, string sort)
        {
            if (sort == "boost") { LancerBoost(nom); return; }
            if (sort != "barrage") { RetourGarage(nom, "Sorts : !ok acheter barrage (" + ScoresImpact.PRIX_BARRAGE + " pts, niv. " + ScoresImpact.NIVEAU_BARRAGE + ") = 3 voitures devant le joueur. !ok acheter boost (" + ScoresImpact.PRIX_BOOST + " pts) = ta voiture plus rapide 15 s."); return; }
            if (!GTAMax.JoueurDisponible() || Function.Call<bool>(Hash.GET_MISSION_FLAG)) { RetourGarage(nom, "Sort impossible maintenant, rien n'est utilise."); return; }
            string avant = scores.Exporter();
            if (!scores.Utiliser(nom, "barrage")) { RetourGarage(nom, "Pas de barrage en stock : !ok acheter barrage (" + ScoresImpact.PRIX_BARRAGE + " pts, niv. " + ScoresImpact.NIVEAU_BARRAGE + ")."); return; }
            scoresModifies = true; sauvegardeScoresA = 0; SauverScores();
            if (scoresModifies) { scores.Importer(avant); RetourGarage(nom, "Sauvegarde impossible : sort annule, barrage conserve."); return; }

            int maintenant = Game.GameTime;
            dernierDonateur = nom;
            bool debut = poursuite.Renforcer(maintenant, VOITURES_BARRAGE);
            if (debut) Commencer();
            for (int i = 0; i < VOITURES_BARRAGE; i++) { donateursEnAttente.Enqueue(nom); voituresEnAttente.Enqueue(""); cadeauxEnAttente.Enqueue("SORT BARRAGE"); }
            barrageRestant += VOITURES_BARRAGE;
            prochaineCreation = alerteJusqua = 0;
            MettreLesEtoiles();
            journal.Info("SORT : " + nom + " lance BARRAGE (" + VOITURES_BARRAGE + " voitures devant), solde=" + scores.Solde(nom));
            Notifier("~p~SORT !~s~ " + nom + " invoque un barrage de police");
            AnnoncerSort(nom, "barrage", 20000);
            RetourGarage(nom, "BARRAGE ! 3 voitures devant le joueur. " + scores.Profil(nom).Barrages + " barrage(s) en stock.");
        }

        // Sort « boost » : les voitures du viewer en poursuite vont plus vite pendant 15 s.
        void LancerBoost(string nom)
        {
            int maintenant = Game.GameTime;
            var siennes = new List<FlicSurvie>();
            foreach (var f in flics)
                if (string.Equals(f.Donateur, nom, StringComparison.OrdinalIgnoreCase) && f.MorteA == 0
                    && f.Voiture != null && f.Voiture.Exists() && f.Voiture.Model.Hash == f.ModeleHash) siennes.Add(f);
            if (!poursuite.Active || siennes.Count == 0) { RetourGarage(nom, "Boost : tu n'as pas de voiture en poursuite, rien n'est utilise."); return; }
            foreach (var f in siennes)
                if (f.BoostJusqua > maintenant) { RetourGarage(nom, "Boost deja actif, rien n'est utilise."); return; }
            string avant = scores.Exporter();
            if (!scores.Utiliser(nom, "boost")) { RetourGarage(nom, "Pas de boost en stock : !ok acheter boost (" + ScoresImpact.PRIX_BOOST + " pts)."); return; }
            scoresModifies = true; sauvegardeScoresA = 0; SauverScores();
            if (scoresModifies) { scores.Importer(avant); RetourGarage(nom, "Sauvegarde impossible : sort annule, boost conserve."); return; }

            Ped joueur = Game.Player.Character;
            foreach (var f in siennes) { f.BoostJusqua = maintenant + BOOST_MS; AdapterVitesse(f, joueur); }
            journal.Info("SORT : " + nom + " lance BOOST (" + siennes.Count + " voiture(s), " + BOOST_MS / 1000 + " s), solde=" + scores.Solde(nom));
            Notifier("~p~SORT !~s~ " + nom + " passe en ~r~BOOST");
            AnnoncerSort(nom, "boost", BOOST_MS);
            RetourGarage(nom, "BOOST ! Ta voiture fonce pendant " + BOOST_MS / 1000 + " s. " + scores.Profil(nom).Boosts + " boost(s) en stock.");
        }

        void AppliquerFreins(FlicSurvie f)
        {
            int rang = scores.Profil(f.Donateur).Freins;
            if (rang <= 0) return;
            Function.Call(Hash.SET_VEHICLE_MOD_KIT, f.Voiture, 0);
            int nombre = Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, f.Voiture, 12);
            if (nombre > 0) Function.Call(Hash.SET_VEHICLE_MOD, f.Voiture, 12, Math.Min(rang, nombre) - 1, false);
        }

        void ChoisirModeleViewer()
        {
            if (nomsVoitures.Count == 0) return;
            string nom = donateursEnAttente.Count > 0 ? donateursEnAttente.Peek() : dernierDonateur;
            string imposee = voituresEnAttente.Count > 0 && donateursEnAttente.Count > 0 ? voituresEnAttente.Peek() : "";
            string selection = ScoresImpact.Vehicule(imposee != "" ? imposee : scores.Profil(nom).Modele);
            if (selection == null) selection = nomsVoitures[prochaineVoiture % nomsVoitures.Count];
            var m = new Model(selection);
            if (!m.IsInCdImage || !m.IsVehicle) { selection = nomsVoitures[0]; m = new Model(selection); }
            if (modeleVoiture.Hash != m.Hash)
            {
                modeleVoiture.MarkAsNoLongerNeeded();
                chargementDepuis = Game.GameTime;
            }
            nomModele = selection;
            els = gereParEls(selection);
            modeleVoiture = m;
            modeleVoiture.Request();
        }


        void DetecterPoints()
        {
            if (!progressionLisible || !poursuite.Active || !GTAMax.JoueurDisponible()) return;
            Ped joueur = Game.Player.Character;
            Entity cible = joueur.IsInVehicle() ? (Entity)joueur.CurrentVehicle : joueur;
            foreach (FlicSurvie f in flics)
            {
                Vehicle v = f.Voiture;
                if (f.PointMarque || f.MorteA != 0 || v == null || !v.Exists()
                    || v.Model.Hash != f.ModeleHash || v.Handle == cible.Handle
                    || f.Conducteur == null || !f.Conducteur.Exists() || f.Conducteur.IsDead
                    || !f.Conducteur.IsInVehicle(v)) continue;
                bool contact = Function.Call<bool>(Hash.IS_ENTITY_TOUCHING_ENTITY, v, cible);
                if (!scores.Toucher(f.Donateur, contact, ref f.PointMarque)) continue;
                scoresModifies = true;
                pointNom = f.Donateur;
                pointJusqua = Game.GameTime + 3000;
                journal.Info("POINT : " + f.Donateur + " +1, total=" + scores.Total(f.Donateur));
            }
        }

        void SauverScores()
        {
            if (!progressionLisible || !scoresModifies || Game.GameTime < sauvegardeScoresA) return;
            sauvegardeScoresA = Game.GameTime + 500;
            try
            {
                File.WriteAllText(cheminScores + ".tmp", scores.Exporter(), new UTF8Encoding(false));
                if (File.Exists(cheminScores)) File.Replace(cheminScores + ".tmp", cheminScores, null);
                else File.Move(cheminScores + ".tmp", cheminScores);
                scoresModifies = false;
            }
            catch (Exception ex) { journal.ErreurLimitee("scores", "Scores non sauvegardés : " + ex.Message); }
        }
        readonly Queue<string> cadeauxEnAttente = new Queue<string>();
        string alerteNom = "", alerteCadeau = "";
        int alerteDepuis, alerteJusqua;

        void TexteCentral(string texte, float y, float taille, int r, int g, int b)
        {
            Function.Call(Hash.SET_TEXT_FONT, 4);
            Function.Call(Hash.SET_TEXT_SCALE, 0f, taille);
            Function.Call(Hash.SET_TEXT_COLOUR, r, g, b, 255);
            Function.Call(Hash.SET_TEXT_CENTRE, true);
            Function.Call(Hash.SET_TEXT_WRAP, 0.06f, 0.94f);
            Function.Call(Hash.SET_TEXT_OUTLINE);
            Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
            Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, texte);
            Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, 0.5f, y, 0);
        }

        void DessinerCadeau()
        {
            int age = Game.GameTime - alerteDepuis;
            if (Game.GameTime >= alerteJusqua) return;
            float pulse = 1f + 0.06f * (float)Math.Sin(age / 160.0);
            Function.Call(Hash.DRAW_RECT, 0.5f, 0.15f, 0.88f, 0.24f, 8, 8, 18, 175, false);
            TexteCentral("WEE WOO ! +1 POLICE", 0.04f, 0.90f * pulse, 255, 70, 100);
            TexteCentral(ReglesSurvie.TexteJeu(alerteNom, 24), 0.115f, 0.72f, 255, 230, 40);
            TexteCentral(alerteCadeau + "  /  T'ES CUIT !", 0.185f, 0.55f, 255, 255, 255);
        }

        PoseFlic PointCadeau(Ped joueur)
        {
            Vector3 origine = joueur.Position;
            Vector3 sens = joueur.IsInVehicle() ? joueur.CurrentVehicle.ForwardVector : joueur.ForwardVector;
            float vitesse = joueur.IsInVehicle() ? joueur.CurrentVehicle.Speed : 0f;
            for (int i = 0; i < 6; i++)
            {
                Vector3 vise = origine + sens * (Math.Max(22f, Math.Min(75f, vitesse * 1.6f)) + i * 9f);
                var sortie = new OutputArgument();
                var cap = new OutputArgument();
                if (!Function.Call<bool>(Hash.GET_CLOSEST_VEHICLE_NODE_WITH_HEADING,
                    vise.X, vise.Y, vise.Z, sortie, cap, 1, 3f, 0f)) continue;
                Vector3 p = sortie.GetResult<Vector3>(), ecart = p - origine;
                float devant = ecart.X * sens.X + ecart.Y * sens.Y;
                float cote = Math.Abs(ecart.X * sens.Y - ecart.Y * sens.X);
                if (devant < 18f || cote > 14f || Math.Abs(ecart.Z) > 5f) continue;
                if (World.GetNearbyVehicles(p, 8f).Length > 0 || World.GetNearbyPeds(p, 5f).Length > 0) continue;
                return new PoseFlic { Position = p, Cap = ReglesSurvie.CapDeRouteVers(cap.GetResult<float>(),
                    p.X, p.Y, p.X + sens.X * 20f, p.Y + sens.Y * 20f), Ou = "cadeau devant le joueur" };
            }
            return null;
        }

        public Survie(Journal journal, string dossierScripts, Func<string, bool> gereParEls, List<string> modeles, string modelePed,
                      int maxFlics, int dureeS, float rayonM, bool indestructibles)
        {
            Courante = this;
            this.journal = journal;
            this.gereParEls = gereParEls;
            cheminRecord = Path.Combine(dossierScripts, "GTAMax.record");
            modelesVoulus = modeles;
            nomPed = modelePed;
            this.rayonM = rayonM;
            this.indestructibles = indestructibles;
            poursuite = new Poursuite(maxFlics, dureeS * 1000, Math.Max(CHALEUR_MAX, maxFlics), 6, 2);
            recordMs = LireRecord();
            cheminScores = Path.Combine(dossierScripts, "GTAMax.points");
            try { if (File.Exists(cheminScores)) scores.Importer(File.ReadAllText(cheminScores)); }
            catch (Exception ex) { progressionLisible = false; journal.ErreurLimitee("scores-lecture", "Progression suspendue : " + ex.Message); }
        }

        // ─── NOM DU DONATEUR AU-DESSUS DE SA VOITURE (chaque image, via NomsDonateurs) ───
        public static Survie Courante;
        const float NOMS_DISTANCE_M = 120f;

        public void DessinerNoms()
        {
            DetecterPoints();
            if (Game.GameTime >= pointJusqua) DessinerCadeau();
            if (Game.GameTime < pointJusqua)
            {
                TexteCentral("BONK ! +1 POINT", 0.04f, 0.90f, 80, 255, 130);
                TexteCentral(ReglesSurvie.TexteJeu(pointNom, 24) + " : " + scores.Total(pointNom) + " POINTS", 0.12f, 0.65f, 255, 230, 40);
            }
            if (!poursuite.Active || flics.Count == 0) return;
            // Crashs en poursuite (2026-09-24) : trafic add-on dense + flics = streaming saturé.
            // Moins de trafic pendant la poursuite (natives « this frame » : à chaque image).
            Function.Call(Hash.SET_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, 0.4f);
            Function.Call(Hash.SET_RANDOM_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, 0.4f);
            Function.Call(Hash.SET_PARKED_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, 0.2f);
            Vector3 cam = GameplayCamera.Position;
            foreach (FlicSurvie f in flics)
            {
                Vehicle v = f.Voiture;
                if (v == null || !v.Exists() || v.Model.Hash != f.ModeleHash || f.MorteA != 0) continue;
                bool booste = f.BoostJusqua > Game.GameTime;
                float coupleViewer = f.Couple + scores.Profil(f.Donateur).Transmission * 0.08f + (booste ? BOOST_COUPLE : 0f);
                if (coupleViewer > 1f && f.Conducteur != null && f.Conducteur.Exists() && !f.Conducteur.IsDead
                    && f.Conducteur.IsInVehicle(v) && !v.IsInAir && !v.IsUpsideDown)
                    Function.Call(Hash.SET_VEHICLE_CHEAT_POWER_INCREASE, v, coupleViewer);
                if (string.IsNullOrEmpty(f.Donateur)) continue;
                Vector3 p = v.Position + new Vector3(0f, 0f, 2.2f);
                float distance = p.DistanceTo(cam);
                if (distance > NOMS_DISTANCE_M || !Function.Call<bool>(Hash.IS_ENTITY_ON_SCREEN, v)) continue;
                float taille = Math.Max(0.65f, ReglesSurvie.EchelleNom(distance));
                Function.Call(Hash.SET_DRAW_ORIGIN, p.X, p.Y, p.Z, 0);
                try
                {
                Function.Call(Hash.SET_TEXT_FONT, 4);
                Function.Call(Hash.SET_TEXT_SCALE, 0.0f, 0.65f * taille);
                Function.Call(Hash.SET_TEXT_COLOUR, 255, 230, 40, 255);
                Function.Call(Hash.SET_TEXT_WRAP, 0f, 1f);
                Function.Call(Hash.SET_TEXT_OUTLINE);
                Function.Call(Hash.SET_TEXT_CENTRE, true);
                Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
                Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, ReglesSurvie.TexteJeu(f.Donateur, 24) + " | Niv." + scores.Niveau(f.Donateur) + " | " + scores.Total(f.Donateur) + " pts" + (booste ? " | BOOST" : ""));
                Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, 0f, 0f, 0);
                }
                finally { Function.Call(Hash.CLEAR_DRAW_ORIGIN); }
            }
        }

        // ─── UN CADEAU ────────────────────────────────────────────────────
        public void Cadeau(string donateur, string cadeau, int points) { Cadeau(donateur, cadeau, points, null); }
        // voiture : cadeau de test du setup, modèle du catalogue imposé sans toucher au profil du viewer.
        public void Cadeau(string donateur, string cadeau, int points, string voiture)
        {
            string nom = ReglesSurvie.TexteJeu(donateur, 60);
            // Les points du cadeau comptent toujours, même si la poursuite ne peut pas démarrer.
            if (progressionLisible && scores.Offrir(nom, points))
            {
                scoresModifies = true; sauvegardeScoresA = 0; SauverScores();
                journal.Info("POINTS : " + nom + " +" + points + " (cadeau), total=" + scores.Total(nom));
            }
            if (!GTAMax.JoueurDisponible()) { journal.Info("Survie : cadeau de " + nom + " ignoré (joueur mort, en chargement ou en cinématique)"); return; }
            if (Function.Call<bool>(Hash.GET_MISSION_FLAG)) { journal.Info("Survie : cadeau de " + nom + " ignoré (mission en cours, on ne la casse pas)"); return; }

            int maintenant = Game.GameTime;
            int avant = ReglesSurvie.NiveauPoliceNative(poursuite.Chaleur);
            dernierDonateur = nom;
            bool debut = poursuite.Cadeau(maintenant);
            if (debut) Commencer();
            donateursEnAttente.Enqueue(nom);
            voituresEnAttente.Enqueue(Array.IndexOf(ScoresImpact.Modeles, voiture) >= 0 ? voiture : "");
            cadeauxEnAttente.Enqueue(ReglesSurvie.TexteJeu(cadeau, 28));
            int niveau = ReglesSurvie.NiveauPoliceNative(poursuite.Chaleur);
            MettreLesEtoiles();

            journal.Info("Survie : " + (debut ? "DÉBUT — " : "") + "cadeau de " + nom + (string.IsNullOrEmpty(cadeau) ? "" : " (" + cadeau + ")")
                + " — police native " + niveau + " étoile(s), fin dans " + ReglesSurvie.Duree(poursuite.RestantMs(maintenant)));
            if (debut) Notifier("~r~SURVIE !~s~ " + nom + " déclenche " + niveau + " étoiles");
            else if (niveau > avant) Notifier(nom + " renforce la police : ~r~" + niveau + " étoiles");
            else Notifier(nom + " prolonge la poursuite native !");
        }

        void Commencer()
        {
            flics.Clear();
            donateursEnAttente.Clear();
            voituresEnAttente.Clear();
            cadeauxEnAttente.Clear();
            numero = 0;
            attrapeDepuis = 0;
            policeLanceeA = Game.GameTime;
            prochaineCreation = prochainPing = prochainEntretien = 0;
            prochainDiagnostic = 0;
            prochaineEmbuscade = prochainEssaiEmbuscade = 0;
            journal.Info("Survie : profil NATIF-v2, police terrestre sans tirs ni herses");
            maxEtoilesAvant = Function.Call<int>(Hash.GET_MAX_WANTED_LEVEL);
            Function.Call(Hash.CLEAR_PLAYER_WANTED_LEVEL, Game.Player.Handle);
            Function.Call(Hash.SET_MAX_WANTED_LEVEL, 5);
            Function.Call(Hash.SET_POLICE_IGNORE_PLAYER, Game.Player.Handle, false);
            Function.Call(Hash.SET_EVERYONE_IGNORE_PLAYER, Game.Player.Handle, false);
            ConfigurerDispatch(false);
        }

        void ConfigurerDispatch(bool autoriser)
        {
            foreach (int service in DISPATCH_INTERDITS)
                Function.Call(Hash.ENABLE_DISPATCH_SERVICE, service, autoriser);
        }

        void DesarmerPolice(Ped joueur, int maintenant)
        {
            if (maintenant < prochainDesarmement) return;
            prochainDesarmement = maintenant + DESARMEMENT_MS;
            foreach (Ped p in World.GetNearbyPeds(joueur, RAYON_DESARMEMENT_M))
            {
                if (p == null || !p.Exists() || p.IsDead) continue;
                if (!ReglesSurvie.EstPoliceNative(Function.Call<int>(Hash.GET_PED_TYPE, p))) continue;
                if (!Function.Call<bool>(Hash.IS_PED_ARMED, p, ARME_A_FEU_OU_EXPLOSIF)) continue;
                Function.Call(Hash.REMOVE_ALL_PED_WEAPONS, p, true);
                policiersDesarmes++;
            }
        }

        // Vraies étoiles : patrouilles, barrages et hélicoptères du jeu épaulent les voitures GTAMax.
        void MettreLesEtoiles()
        {
            int niveau = ReglesSurvie.NiveauPoliceNative(poursuite.Chaleur);
            Function.Call(Hash.SET_FAKE_WANTED_LEVEL, 0);
            if (Function.Call<int>(Hash.GET_MAX_WANTED_LEVEL) != 5) Function.Call(Hash.SET_MAX_WANTED_LEVEL, 5);
            Function.Call(Hash.SET_POLICE_IGNORE_PLAYER, Game.Player.Handle, false);
            Function.Call(Hash.SET_PLAYER_WANTED_LEVEL, Game.Player.Handle, niveau, false);
            Function.Call(Hash.SET_PLAYER_WANTED_LEVEL_NOW, Game.Player.Handle, false);
        }

        // ─── TICK (appelé toutes les 100 ms par GTAMax) ───────────────────
        public void Tick()
        {
            SauverScores();
            if (!poursuite.Active) return;
            int maintenant = Game.GameTime;
            Ped joueur = Game.Player.Character;

            string fin = RaisonDeFin(joueur, maintenant);
            if (fin != null) { Arreter(fin); return; }

            if (maintenant < prochainEntretien) return;
            prochainEntretien = maintenant + ENTRETIEN_MS;
            if (!GTAMax.JoueurDisponible()) return;   // chargement, cinématique : on attend

            // GTA gère la conduite et la recherche. GTAMax retire les armes des policiers proches.
            DesarmerPolice(joueur, maintenant);
            Ranger(joueur, maintenant);
            Recruter(joueur, maintenant);
            foreach (FlicSurvie f in flics) PoursuivreImpact(f, joueur, maintenant);
            if (maintenant >= prochainDiagnostic)
            {
                prochainDiagnostic = maintenant + 5000;
                Diagnostiquer(joueur);
            }
        }

        // Poursuite native : percussions agressives, sans la consigne de rester derrière.
        void PoursuivreImpact(FlicSurvie f, Ped joueur, int maintenant)
        {
            Vehicle v = f.Voiture;
            Ped c = f.Conducteur;
            if (v == null || !v.Exists() || v.Model.Hash != f.ModeleHash || f.MorteA != 0
                || c == null || !c.Exists() || c.IsDead || !c.IsInVehicle(v)) return;
            if (maintenant < f.ProchainOrdre) return;
            f.ProchainOrdre = maintenant + 1000;
            // Rattrapage (test 2026-09-28 « trop facile ») : distancée, hors de vue, 3 s → replacée devant sur sa route.
            if (joueur.IsInVehicle() && !f.EmbuscadeUtilisee)
            {
                Vector3 rel = v.Position - joueur.Position, sens = joueur.CurrentVehicle.Speed < 3f ? joueur.ForwardVector : joueur.CurrentVehicle.Velocity;
                bool visible = Function.Call<bool>(Hash.IS_ENTITY_ON_SCREEN, v);
                bool derriere = Regles.EstDerriere(rel.X, rel.Y, sens.X, sens.Y);
                float distance = v.Position.DistanceTo(joueur.Position);
                if (visible || !derriere || distance < 145f) f.RetardDepuis = 0;
                else if (f.RetardDepuis == 0) f.RetardDepuis = Math.Max(1, maintenant);
                else if (maintenant >= prochaineInterception && maintenant - f.DernierePoseA >= RECYCLAGE_MIN_MS
                    && ReglesSurvie.DoitIntercepter(visible, true, derriere, distance, maintenant - f.RetardDepuis)
                    && IntercepterDevant(f, joueur, maintenant))
                {
                    f.ChasseActive = false;   // la pose donne une consigne de patrouille : la poursuite reprend à la seconde suivante
                    return;
                }
            }
            // Test 2026-09-28 : la police du jeu suivait mieux que les voitures viewers (chase brute, blocage
            // interdit, style et vitesse réimposés). Le chauffeur devient un vrai flic en combat : même IA de poursuite.
            if (!f.ChasseActive || f.CibleChasse != joueur.Handle || !Function.Call<bool>(Hash.IS_PED_IN_COMBAT, c, joueur))
            {
                if (!f.ChasseActive)
                {
                    Function.Call(Hash.SET_PED_AS_COP, c, true);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, c, 3, false);    // reste dans sa voiture
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, c, 46, true);    // ne fuit jamais
                    Function.Call(Hash.SET_PED_COMBAT_ABILITY, c, 2);              // professionnel
                    Function.Call(Hash.SET_DRIVER_ABILITY, c, 1f);
                    Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, c, 1f);
                }
                Function.Call(Hash.TASK_COMBAT_PED, c, joueur, 0, 16);
                f.ChasseActive = true;
                f.CibleChasse = joueur.Handle;
            }
            // Plus agressives (même test) : percussion continue, PIT et coupe-route permis, collée au joueur.
            Function.Call(Hash.SET_TASK_VEHICLE_CHASE_BEHAVIOR_FLAG, c, 8, false);     // CANT_RAM
            Function.Call(Hash.SET_TASK_VEHICLE_CHASE_BEHAVIOR_FLAG, c, 16, false);    // CANT_SPIN_OUT (PIT)
            Function.Call(Hash.SET_TASK_VEHICLE_CHASE_BEHAVIOR_FLAG, c, 32, false);    // CANT_MAKE_AGGRESSIVE_MOVE
            Function.Call(Hash.SET_TASK_VEHICLE_CHASE_BEHAVIOR_FLAG, c, 128, true);    // USE_CONTINUOUS_RAM
            Function.Call(Hash.SET_TASK_VEHICLE_CHASE_IDEAL_PURSUIT_DISTANCE, c, 0f);
            AdapterVitesse(f, joueur);
        }

        void Diagnostiquer(Ped joueur)
        {
            var lignes = new List<string>();
            int proches = 0;
            foreach (FlicSurvie f in flics)
            {
                Vehicle v = f.Voiture;
                if (v == null || !v.Exists() || v.Model.Hash != f.ModeleHash) continue;
                float distance = v.Position.DistanceTo(joueur.Position);
                if (distance <= 40f) proches++;
                lignes.Add(f.Numero + ":" + Math.Round(v.Speed * 3.6f) + "km/h@" + Math.Round(distance)
                    + "m/boost=" + f.Assistances + "/couple=" + f.Couple.ToString("0.0")
                    + "/refus=" + f.RefusAlignement + ":" + f.RefusGlisse + "/" + (f.PatrouilleDevant ? "patrouille" : f.Role > 0 ? NomRole(f.Role) : f.ChasseActive ? "chasse" : "manoeuvre"));
            }
            float vitesse = joueur.IsInVehicle() ? joueur.CurrentVehicle.Speed : joueur.Speed;
            string modele = joueur.IsInVehicle() ? joueur.CurrentVehicle.Model.Hash.ToString("X8") : "a-pied";
            int etoiles = Function.Call<int>(Hash.GET_PLAYER_WANTED_LEVEL, Game.Player.Handle);
            journal.Info("Survie DIAG : joueur=" + Math.Round(vitesse * 3.6f) + "km/h modele=" + modele
                + " étoiles=" + etoiles + " police=GTA terrestre sans tirs herses hélicos"
                + " désarmements=" + policiersDesarmes
                + " viewers=" + proches + "/" + lignes.Count + "≤40m " + string.Join(" ", lignes.ToArray()));
        }

        string RaisonDeFin(Ped joueur, int maintenant)
        {
            if (joueur == null || !joueur.Exists() || joueur.IsDead) return "mort";
            if (Function.Call<bool>(Hash.IS_PLAYER_BEING_ARRESTED, Game.Player.Handle, true)) return "attrapé";   // par la police du jeu (test n°6)
            if (Function.Call<bool>(Hash.GET_MISSION_FLAG)) return "mission";
            if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS)) return "changement de personnage";
            if (poursuite.Expiree(maintenant)) return "survécu";   // plus de cadeau depuis DUREE_S : il a tenu
            if (poursuite.Chaleur == 0) return "semés";
            if (maintenant - policeLanceeA >= 10000
                && Function.Call<int>(Hash.GET_PLAYER_WANTED_LEVEL, Game.Player.Handle) == 0) return "semés";

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
            donateursEnAttente.Clear(); voituresEnAttente.Clear(); cadeauxEnAttente.Clear(); alerteJusqua = 0;
            foreach (FlicSurvie f in flics) Lacher(f);
            flics.Clear();
            LibererModeles();
            RendreLaPolice();

            bool compte = raison == "survécu" || raison == "semés" || raison == "attrapé" || raison == "mort";
            bool record = compte && duree > recordMs;
            if (record) { recordMs = duree; EcrireRecord(duree); }
            string temps = ReglesSurvie.Duree(duree);
            string suffixe = !compte ? "" : record ? " — ~y~nouveau record !~s~" : " (record : " + ReglesSurvie.Duree(recordMs) + ")";
            journal.Info("Survie : FIN (" + raison + ") — " + temps + (record ? ", nouveau record" : ""));
            if (raison == "semés") Notifier("~g~SEMÉS !~s~ Tu as échappé à la police après " + temps + suffixe);
            else if (raison == "survécu") Notifier("~g~SURVÉCU !~s~ " + temps + " de poursuite" + suffixe);
            else if (raison == "attrapé") Notifier("~r~ATTRAPÉ !~s~ Survie : " + temps + suffixe);
            else if (raison == "mort") Notifier("~r~Survie terminée~s~ : " + temps + suffixe);
            else Notifier("Poursuite arrêtée (" + raison + ")");
        }

        // Script déchargé (Reload(), fermeture du jeu) : rien ne reste « à nous ».
        public void Liberer()
        {
            sauvegardeScoresA = 0; SauverScores();
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
            Function.Call(Hash.CLEAR_PLAYER_WANTED_LEVEL, Game.Player.Handle);
            ConfigurerDispatch(true);
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
                    LacherPolicier(f.Conducteur);   // voiture partie (ou handle réutilisé) : ses occupants ne restent pas « à nous »
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
            if (donateursEnAttente.Count == 0) { barrageRestant = 0; return; }
            bool barrage = barrageRestant > 0;
            if (!barrage && (maintenant < prochaineCreation || maintenant < alerteJusqua)) return;
            // Borne le nombre de véhicules persistants. Un nouveau cadeau attend
            // si toutes les voitures sont encore proches ou visibles. Un sort barrage
            // peut monter jusqu'à 9 pour arriver tout de suite.
            if (flics.Count >= Math.Min(barrage ? 11 : 8, poursuite.FlicsVoulus))   // 6 → 8 (2026-09-28, « trop facile »)
            {
                FlicSurvie retire = null;
                foreach (FlicSurvie ancien in flics)
                    if (ancien.Voiture != null && ancien.Voiture.Exists()
                        && !joueur.IsInVehicle(ancien.Voiture)
                        && ancien.Voiture.Position.DistanceTo(joueur.Position) > 120f
                        && !Function.Call<bool>(Hash.IS_ENTITY_ON_SCREEN, ancien.Voiture))
                    { retire = ancien; break; }
                if (retire == null) return;
                Supprimer(retire);
                flics.Remove(retire);
            }
            if (!modelesDemandes) DemanderModeles();
            ChoisirModeleViewer();
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

            PoseFlic pose = PointCadeau(joueur);
            if (pose == null)
            {
                journal.ErreurLimitee("survie-point", "Survie : pas de place libre devant le joueur pour le cadeau, nouvel essai");
                prochaineCreation = maintenant + NOUVEL_ESSAI_MS;
                return;
            }
            prochaineCreation = maintenant + CREATION_MS;
            Creer(joueur, pose, maintenant);
            if (barrageRestant > 0 && --barrageRestant > 0) Recruter(joueur, maintenant);   // les 3 d'un coup
        }

        void DemanderModeles()
        {
            if (!modelesVerifies)
            {
                modelesVerifies = true;
                foreach (string nom in modelesVoulus)
                {
                    var m = new Model(nom);
                    if (m.IsInCdImage && m.IsVehicle) { nomsVoitures.Add(nom); continue; }
                    journal.Erreur("Survie : modèle « " + nom + " » absent du jeu ou pas un véhicule — ignoré");
                }
                if (nomsVoitures.Count == 0) { journal.Erreur("Survie : aucun modèle de police valide dans [POLICE] MODELES"); return; }
                nomModele = nomsVoitures[0];
                els = gereParEls(nomModele);
                var p = new Model(nomPed);
                if (!(p.IsInCdImage && p.IsPed))
                {
                    journal.Erreur("Survie : policier « " + nomPed + " » ([POLICE] PED) absent du jeu ou pas un personnage — " + GTAMax.PED_POLICE + " à la place");
                    nomPed = GTAMax.PED_POLICE;
                }
                journal.Info("Survie : voiture(s) « " + string.Join(", ", nomsVoitures.ToArray()) + " » tour à tour, policier « " + nomPed + " »");
            }
            if (nomModele == null) return;
            foreach (string nom in nomsVoitures) new Model(nom).Request();   // toutes gardées en mémoire pendant la poursuite
            modeleVoiture = new Model(nomsVoitures[prochaineVoiture % nomsVoitures.Count]);
            modeleAgent = new Model(nomPed);
            modeleAgent.Request();
            modelesDemandes = true;
            chargementDepuis = Game.GameTime;
        }

        // Modèles gardés en mémoire TOUTE la poursuite (Driver 2 : modèles
        // « résidents ») ; rendus au jeu à la fin.
        void LibererModeles()
        {
            if (!modelesDemandes) return;
            try { foreach (string nom in nomsVoitures) new Model(nom).MarkAsNoLongerNeeded(); modeleVoiture.MarkAsNoLongerNeeded(); modeleAgent.MarkAsNoLongerNeeded(); }
            catch (Exception) { }
            modelesDemandes = false;
        }

        void Creer(Ped joueur, PoseFlic pose, int maintenant)
        {
            Vector3 position = pose.Position;
            Vehicle v = Function.Call<Vehicle>(Hash.CREATE_VEHICLE, modeleVoiture.Hash, position.X, position.Y, position.Z, pose.Cap, false, false, false);
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
            if (c == null || !c.Exists() || c.IsDead || !c.IsInVehicle(v))
            {
                journal.Erreur("Survie : conducteur non créé, voiture annulée");
                if (c != null && c.Exists()) c.Delete();
                v.Delete();
                return;
            }
            GTAMax.PreparerAgent(c);
            if (c != null && c.Exists())
            {
                Function.Call(Hash.REMOVE_ALL_PED_WEAPONS, c, true);                 // il percute, il ne tire pas
                Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, c, false);
                Function.Call(Hash.SET_PED_CAN_BE_KNOCKED_OFF_VEHICLE, c, 1);        // jamais éjecté
                if (indestructibles) Function.Call(Hash.SET_ENTITY_INVINCIBLE, c, true, false);
                Function.Call(Hash.SET_PED_RANDOM_COMPONENT_VARIATION, c, 0);       // en civil : chacun sa tête, ses habits
            }
            v.IsSirenActive = true;
            if (!els) v.IsSirenSilent = false;

            var f = new FlicSurvie
            {
                Numero = ++numero, Voiture = v, Conducteur = c, ModeleHash = v.Model.Hash,
                Donateur = donateursEnAttente.Count > 0 ? donateursEnAttente.Dequeue() : dernierDonateur,
                DernierePoseA = maintenant,
            };
            if (voituresEnAttente.Count > 0) voituresEnAttente.Dequeue();
            AppliquerFreins(f);
            flics.Add(f);
            alerteNom = f.Donateur;
            alerteCadeau = cadeauxEnAttente.Count > 0 ? cadeauxEnAttente.Dequeue() : "Cadeau";
            alerteDepuis = maintenant;
            alerteJusqua = maintenant + 3500;
            f.SemeDepuis = 0;
            f.VenuAuContact = false;
            AdapterVitesse(f, joueur);
            PoursuivreImpact(f, joueur, maintenant);
            journal.Info("Survie : flic " + f.Numero + " apparu en « " + nomModele + " » ("
                + flics.Count + "/" + poursuite.FlicsVoulus + ", cadeau de " + f.Donateur + ") — " + pose.Ou);
            prochaineVoiture++;   // le suivant prend la voiture suivante de la liste
        }

        // Une route derrière le joueur ; si elle manque, quelques routes alentour.
        PoseFlic TrouverPoint(Ped joueur, bool nouveau)
        {
            Vector3 pj = joueur.Position;
            Vehicle vj = joueur.IsInVehicle() ? joueur.CurrentVehicle : null;
            Vector3 dir = vj != null && vj.Speed > 3f ? vj.Velocity : (vj != null ? vj.ForwardVector : joueur.ForwardVector);
            Vector3 cam = Function.Call<Vector3>(Hash.GET_FINAL_RENDERED_CAM_COORD);
            Vector3 rot = Function.Call<Vector3>(Hash.GET_FINAL_RENDERED_CAM_ROT, 2);
            if (vj != null && vj.Speed >= 8f)
            {
                int[] repartition = { 3, 1, 2 };
                int voulu = repartition[cookie++ % 3];
                PoseFlic tactique = voulu == 3 ? DevantSurSaRoute(joueur, cam, rot)
                    : SurLeCote(joueur, voulu, cam, rot);
                // Une rue latérale absente : chercher devant avant de revenir derrière.
                if (tactique == null && voulu != 3) tactique = DevantSurSaRoute(joueur, cam, rot);
                if (tactique != null)
                {
                    tactique.Role = RoleLibre(tactique.Devant ? 3 : voulu) ? (tactique.Devant ? 3 : voulu) : 0;
                    return tactique;
                }
            }
            PoseFlic derriere = DerriereSurSaRoute(pj, dir, cam, rot);
            if (derriere != null) return derriere;
            double cap0 = Math.Atan2(dir.Y, dir.X);
            foreach (float angle in ReglesSurvie.AnglesCandidats(cookie++, nouveau))
            {
                double a = cap0 + angle * Math.PI / 180.0;
                float d = rayonM;
                var cible = new Vector3(pj.X + (float)Math.Cos(a) * d, pj.Y + (float)Math.Sin(a) * d, pj.Z);
                var sortiePos = new OutputArgument();
                var sortieCap = new OutputArgument();
                if (!Function.Call<bool>(Hash.GET_NTH_CLOSEST_VEHICLE_NODE_FAVOUR_DIRECTION, cible.X, cible.Y, cible.Z, pj.X, pj.Y, pj.Z,
                        1, sortiePos, sortieCap, NOEUD_ROUTE_GOUDRONNEE, 3f, 0f)) continue;
                Vector3 n = sortiePos.GetResult<Vector3>();
                float h = sortieCap.GetResult<float>();
                if (n == Vector3.Zero) continue;
                float distance = new Vector3(n.X - pj.X, n.Y - pj.Y, 0f).Length();
                if (!ReglesSurvie.DistanceDApparitionOk(distance, n.Z - pj.Z, rayonM) || PresDUnFlic(n, DISTANCE_ENTRE_FLICS_M)) continue;
                string vue;
                if (!Accepter(n, distance, cam, rot, out vue)) continue;
                return new PoseFlic { Position = n, Cap = ReglesSurvie.CapDeRouteVers(h, n.X, n.Y, pj.X, pj.Y),
                    Ou = ReglesSurvie.Secteur(angle) + ", " + Math.Round(distance) + " m, " + vue };
            }
            return null;
        }

        bool RoleLibre(int role)
        {
            foreach (FlicSurvie f in flics)
                if (f.MorteA == 0 && f.Role == role && f.Voiture != null && f.Voiture.Exists()) return false;
            return true;
        }

        PoseFlic SurLeCote(Ped joueur, int role, Vector3 cam, Vector3 rot)
        {
            Vehicle cible = joueur.CurrentVehicle;
            Vector3 depart = cible.Position, sens = cible.Velocity;
            float norme = (float)Math.Sqrt(sens.X * sens.X + sens.Y * sens.Y);
            if (norme < 8f) return null;
            sens = new Vector3(sens.X / norme, sens.Y / norme, 0f);
            Vector3 droite = new Vector3(sens.Y, -sens.X, 0f);
            float signe = role == 1 ? -1f : 1f;
            foreach (float avance in new float[] { 50f, 80f, 10f })
            foreach (float ecart in new float[] { 20f, 40f, 60f })
            {
                Vector3 vise = depart + sens * avance + droite * (ecart * signe);
                var sortie = new OutputArgument(); var cap = new OutputArgument();
                if (!Function.Call<bool>(Hash.GET_CLOSEST_VEHICLE_NODE_WITH_HEADING,
                    vise.X, vise.Y, vise.Z, sortie, cap, 1, 3f, 0f)) continue;
                Vector3 n = sortie.GetResult<Vector3>(), rel = n - depart;
                float h = ReglesSurvie.CapDeRouteVers(cap.GetResult<float>(), n.X, n.Y, n.X + sens.X * 50f, n.Y + sens.Y * 50f);
                double rad = h * Math.PI / 180.0;
                float alignement = -(float)Math.Sin(rad) * sens.X + (float)Math.Cos(rad) * sens.Y;
                float distance = rel.Length();
                if (!ReglesSurvie.PlacementLateralOk(role, rel.X * sens.X + rel.Y * sens.Y,
                    rel.X * droite.X + rel.Y * droite.Y, rel.Z, alignement, distance)) continue;
                if (PresDUnFlic(n, 25f) || World.GetNearbyVehicles(n, 15f).Length != 0) continue;
                string vue;
                if (!Accepter(n, distance, cam, rot, out vue)) continue;
                return new PoseFlic { Position = n, Cap = h, Role = role,
                    Ou = (role == 1 ? "à gauche" : "à droite") + ", sur une route, même sens, " + Math.Round(distance) + " m, " + vue };
            }
            return null;
        }

        // Suivre les nœuds de route par pas courts plutôt que viser une rue éloignée.
        // Rejeter les ruptures, carrefours trop serrés et changements d'étage.
        PoseFlic DevantSurSaRoute(Ped joueur, Vector3 cam, Vector3 rot, bool cacheSeulement = false)
        {
            Vehicle cible = joueur.CurrentVehicle;
            Vector3 depart = cible.Position, point = depart;
            Vector3 sens = cible.Velocity;
            float longueur = (float)Math.Sqrt(sens.X * sens.X + sens.Y * sens.Y);
            if (longueur < 8f) return null;
            sens = new Vector3(sens.X / longueur, sens.Y / longueur, 0f);
            float minimum = Math.Min(190f, Math.Max(140f, cible.Speed * 3f));
            for (int i = 0; i < 12; i++)
            {
                Vector3 vise = point + sens * 20f;
                var sortie = new OutputArgument();
                var cap = new OutputArgument();
                if (!Function.Call<bool>(Hash.GET_CLOSEST_VEHICLE_NODE_WITH_HEADING,
                    vise.X, vise.Y, vise.Z, sortie, cap, 1, 3f, 0f)) return null;
                Vector3 suivant = sortie.GetResult<Vector3>();
                float h = ReglesSurvie.CapDeRouteVers(cap.GetResult<float>(), suivant.X, suivant.Y,
                    suivant.X + sens.X * 50f, suivant.Y + sens.Y * 50f);
                double rad = h * Math.PI / 180.0;
                Vector3 direction = new Vector3(-(float)Math.Sin(rad), (float)Math.Cos(rad), 0f);
                Vector3 pas = suivant - point;
                if (!ReglesSurvie.ContinuiteRoute(pas.X * sens.X + pas.Y * sens.Y,
                    pas.X * sens.Y - pas.Y * sens.X, pas.Z, direction.X * sens.X + direction.Y * sens.Y)) return null;
                point = suivant;
                sens = direction;
                float distance = point.DistanceTo(depart);
                if (distance < minimum) continue;
                if (PresDUnFlic(point, 30f) || World.GetNearbyVehicles(point, 15f).Length != 0) continue;
                // Au loin seulement : permet une patrouille devant même sur route ouverte.
                bool champ = ReglesSurvie.DansLeChamp(cam.X, cam.Y, cam.Z, rot.X, rot.Z,
                    point.X, point.Y, point.Z + 1f, DEMI_CHAMP_DEG);
                if (cacheSeulement && champ && !Function.Call<bool>(Hash.WOULD_ENTITY_BE_OCCLUDED,
                    modeleVoiture.Hash, point.X, point.Y, point.Z + 1f, true)) continue;
                return new PoseFlic { Position = point, Cap = h, Devant = true,
                    Ou = "devant sur la route, même sens, " + Math.Round(distance) + " m, patrouille lancée à "
                        + Math.Round(ReglesSurvie.VitessePatrouille(cible.Speed) * 3.6f) + " km/h" + (champ ? ", au loin" : ", hors champ") };
            }
            return null;
        }

        void Lancer(FlicSurvie f, PoseFlic pose, Ped joueur, int maintenant)
        {
            f.PatrouilleDevant = pose.Devant;
            f.PatrouilleDepuis = maintenant;
            f.OrdrePatrouille = false;
            f.ChasseActive = false;
            f.CibleChasse = 0;
            f.Role = pose.Role; f.OrdreTactique = -1; f.PhaseContact = false; f.ContactJusqua = 0;
            f.Couple = 1f;
            double cap = pose.Cap * Math.PI / 180.0;
            f.SensPatrouille = new Vector3(-(float)Math.Sin(cap), (float)Math.Cos(cap), 0f);
            float vitesse = 0f;
            if (joueur.IsInVehicle())
            {
                Vehicle cible = joueur.CurrentVehicle;
                Vector3 avant = f.Voiture.ForwardVector, sens = cible.ForwardVector;
                float alignement = avant.X * sens.X + avant.Y * sens.Y;
                vitesse = pose.Devant ? ReglesSurvie.VitessePatrouille(cible.Speed)
                    : Math.Min(alignement > 0.7f ? 65f : 18f, Math.Max(8f, cible.Speed * 0.9f));
            }
            Function.Call(Hash.SET_VEHICLE_FORWARD_SPEED, f.Voiture, vitesse);
        }

        // Derrière lui sur sa route, dans le sens qui mène vers lui.
        PoseFlic DerriereSurSaRoute(Vector3 pj, Vector3 dir, Vector3 cam, Vector3 rot)
        {
            float l = (float)Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
            if (l < 0.01f) return null;
            var sortie = new OutputArgument();
            if (!Function.Call<bool>(Hash.FIND_SPAWN_POINT_IN_DIRECTION, pj.X, pj.Y, pj.Z, -dir.X / l, -dir.Y / l, 0f, rayonM, sortie)) return null;
            Vector3 p = sortie.GetResult<Vector3>();
            if (p == Vector3.Zero) return null;
            float distance = new Vector3(p.X - pj.X, p.Y - pj.Y, 0f).Length();
            if (distance < rayonM * 0.5f || distance > rayonM * 1.6f || Math.Abs(p.Z - pj.Z) > 12f) return null;
            if (PresDUnFlic(p, DISTANCE_ENTRE_FLICS_M)) return null;
            string vue;
            if (!Accepter(p, distance, cam, rot, out vue)) return null;

            float capRoute = ReglesSurvie.CapVers(p.X, p.Y, pj.X, pj.Y);
            var noeud = new OutputArgument();
            var capNoeud = new OutputArgument();
            if (Function.Call<bool>(Hash.GET_CLOSEST_VEHICLE_NODE_WITH_HEADING, p.X, p.Y, p.Z, noeud, capNoeud, 1, 3f, 0f)
                && noeud.GetResult<Vector3>().DistanceTo(p) < 15f)
                capRoute = capNoeud.GetResult<float>();
            return new PoseFlic
            {
                Position = p,
                Cap = ReglesSurvie.CapDeRouteVers(capRoute, p.X, p.Y, pj.X, pj.Y),
                Ou = "derrière, sur sa route, " + Math.Round(distance) + " m, " + vue,
            };
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

        bool PresDUnFlic(Vector3 p, float distanceMin)
        {
            foreach (FlicSurvie f in flics)
                if (f.Voiture.Exists() && f.Voiture.Position.DistanceTo(p) < distanceMin) return true;
            return false;
        }

        void AdapterVitesse(FlicSurvie f, Ped joueur)
        {
            Vehicle cible = joueur.IsInVehicle() ? joueur.CurrentVehicle : null;
            float vitesseJoueur = cible == null ? 0f : Function.Call<float>(Hash.GET_VEHICLE_ESTIMATED_MAX_SPEED, cible);
            float vitessePolice = Function.Call<float>(Hash.GET_VEHICLE_MODEL_ESTIMATED_MAX_SPEED, f.ModeleHash);
            float bonus = ReglesSurvie.BonusVitesse(vitesseJoueur, vitessePolice) + scores.Profil(f.Donateur).Moteur * 8f
                + (f.BoostJusqua > Game.GameTime ? BOOST_VITESSE : 0f);
            if (Math.Abs(bonus - f.BonusVitesse) < 1f) return;
            Function.Call(Hash.MODIFY_VEHICLE_TOP_SPEED, f.Voiture, bonus);
            f.BonusVitesse = bonus;
        }

        // ─── PILOTER UN FLIC (toutes les 250 ms) ──────────────────────────
        void Piloter(FlicSurvie f, Ped joueur, int maintenant)
        {
            f.Couple = 1f;
            if (f.MorteA != 0 || f.Voiture == null || !f.Voiture.Exists() || f.Voiture.Model.Hash != f.ModeleHash) return;
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

            bool auVolant = c != null && c.Exists() && !c.IsDead && c.IsInVehicle(v);
            if (!auVolant)
            {
                // Éjecté malgré tout : remis au volant, hors de la vue du joueur.
                if (!visible && c != null && c.Exists() && !c.IsDead) Function.Call(Hash.SET_PED_INTO_VEHICLE, c, v, -1);
                f.ChasseActive = false; f.OrdrePatrouille = false; f.OrdreTactique = -1; f.Role = 0;
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
            // Une voiture restée loin derrière est replacée devant sur la route, uniquement hors caméra.
            bool retard = !f.PatrouilleDevant && !visible && derriere && distance >= 145f && joueur.IsInVehicle();
            if (!retard) f.RetardDepuis = 0;
            else if (f.RetardDepuis == 0) f.RetardDepuis = Math.Max(1, maintenant);
            else if (!f.EmbuscadeUtilisee && maintenant >= prochaineInterception && maintenant >= prochainPing
                && maintenant - f.DernierePoseA >= RECYCLAGE_MIN_MS
                && ReglesSurvie.DoitIntercepter(visible, true, derriere, distance, maintenant - f.RetardDepuis)
                && IntercepterDevant(f, joueur, maintenant)) return;

            // Semé 12 s de suite (venu au contact, puis loin et hors de vue) : il abandonne (test n°5).
            // Jamais venu au contact : il le cherche encore, reposé ailleurs.
            if (distance <= 25f) f.VenuAuContact = true;
            string loin = f.PatrouilleDevant ? null : ReglesSurvie.SortDuFlicLoin(visible, distance, derriere, f.VenuAuContact || f.EmbuscadeUtilisee, SEME_DERRIERE_M, SEME_M);
            if (loin != "semé") { f.SemeDepuis = 0; f.AttenteEmbuscadeA = 0; }
            else if (f.SemeDepuis == 0) f.SemeDepuis = Math.Max(1, maintenant);
            else if (maintenant - f.SemeDepuis >= SEME_MS) { DerniereChance(f, joueur, distance, derriere, maintenant); return; }
            string raison = ReglesSurvie.RaisonDeRecycler(visible, planteeMs, horsDeCourse, PLANTEE_MAX_MS)
                ?? (loin == "perdu de vue" ? loin : null);
            if (!f.EmbuscadeUtilisee && raison != null && maintenant >= prochainPing && maintenant - f.DernierePoseA >= RECYCLAGE_MIN_MS
                && Recycler(f, joueur, raison, maintenant)) return;

            if (!aPied && maintenant >= f.ManoeuvreJusquA)
            {
                Vector3 avant = v.ForwardVector, trajet = joueur.CurrentVehicle.Velocity;
                float norme = (float)Math.Sqrt(trajet.X * trajet.X + trajet.Y * trajet.Y);
                float alignement = norme < 1f ? 0f : (avant.X * trajet.X + avant.Y * trajet.Y) / norme;
                float arriere = norme < 1f ? 0f : (rel.X * trajet.X + rel.Y * trajet.Y) / norme;
                Vector3 mouvement = v.Velocity;
                float glisse = v.Speed < 1f ? 1f : (mouvement.X * avant.X + mouvement.Y * avant.Y) / v.Speed;
                bool stable = v.IsOnAllWheels && !v.IsInAir && !horsDeCourse && glisse > 0.72f && Math.Abs(rel.Z) < 10f;
                f.Couple = ReglesSurvie.CoupleRattrapage(f.PatrouilleDevant, v.Speed, joueur.CurrentVehicle.Speed,
                    distance, alignement, arriere < -12f, stable);
                if (f.Couple > 1f) f.Assistances++;
                else if (alignement < 0.65f) f.RefusAlignement++;
                else if (!stable) f.RefusGlisse++;
            }
            if (maintenant < f.ManoeuvreJusquA) return;   // il recule : on le laisse finir
            if (visible && planteeMs >= PLANTEE_AVANT_MANOEUVRE_MS) { Manoeuvrer(f, maintenant); return; }

            if (maintenant >= f.ProchainOrdre)
            {
                Ordonner(f, joueur, distance, aPied);
                f.ProchainOrdre = maintenant + ORDRE_MS;
            }
        }

        /* Ancienne aide retirée : elle lançait un test de collision natif asynchrone par voiture. */
        // Semé (test n°5 : « il faut que je puisse les semer ») : il abandonne — rendu au jeu,
        // un flic de moins à ses trousses, une étoile de moins. Le dernier : « SEMÉS ! » (RaisonDeFin).
        void DerniereChance(FlicSurvie f, Ped joueur, float distance, bool derriere, int maintenant)
        {
            if (f.AttenteEmbuscadeA == 0) f.AttenteEmbuscadeA = Math.Max(1, maintenant);
            bool libre = true;
            foreach (FlicSurvie autre in flics)
                if (autre != f && autre.EmbuscadeUtilisee && autre.PatrouilleDevant && autre.MorteA == 0) { libre = false; break; }
            int decision = ReglesSurvie.DecisionEmbuscade(f.EmbuscadeUtilisee, joueur.IsInVehicle(),
                maintenant - f.AttenteEmbuscadeA, libre, maintenant >= prochaineEmbuscade && maintenant >= prochainEssaiEmbuscade);
            if (decision == 2)
            {
                journal.Info("Survie : flic " + f.Numero + (f.EmbuscadeUtilisee ? " dernière interception échappée" : " dernière interception impossible (pas de placement sûr disponible)"));
                Abandonner(f, distance, derriere);
                return;
            }
            if (decision == 0) return;
            prochainEssaiEmbuscade = maintenant + 1000;
            Vector3 cam = Function.Call<Vector3>(Hash.GET_FINAL_RENDERED_CAM_COORD);
            Vector3 rot = Function.Call<Vector3>(Hash.GET_FINAL_RENDERED_CAM_ROT, 2);
            PoseFlic pose = DevantSurSaRoute(joueur, cam, rot, true);
            if (pose == null) return;
            // Une même voiture : pas de renfort supplémentaire ni de compteur remis à zéro.
            f.EmbuscadeUtilisee = true;
            prochaineEmbuscade = maintenant + 8000;
            Replacer(f, pose, joueur, "DERNIÈRE EMBUSCADE", maintenant);
            f.AttenteEmbuscadeA = 0;
        }

        void Abandonner(FlicSurvie f, float distance, bool derriere)
        {
            Lacher(f);
            flics.Remove(f);
            int reste = poursuite.Semer();
            MettreLesEtoiles();
            journal.Info("Survie : flic " + f.Numero + " SEMÉ (" + Math.Round(distance) + " m" + (derriere ? " derrière" : "")
                + ", hors de vue depuis 12 s) — il abandonne, " + reste + " à ses trousses");
            if (reste > 0) Notifier("~g~Flic semé !~s~ Encore " + reste + " à tes trousses");
        }

        // Une seule consigne : suivre la position du joueur et chercher le choc.
        void Ordonner(FlicSurvie f, Ped joueur, float distance, bool aPied)
        {
            Vehicle v = f.Voiture;
            Ped conducteur = f.Conducteur;
            if (v == null || !v.Exists() || v.Model.Hash != f.ModeleHash || conducteur == null
                || !conducteur.Exists() || conducteur.IsDead || !conducteur.IsInVehicle(v)) return;
            Vector3 pj = joueur.Position;
            AdapterVitesse(f, joueur);
            if (f.PatrouilleDevant && !aPied)
            {
                Vector3 ecart = v.Position - pj;
                Vector3 avant = f.SensPatrouille;
                bool depasse = Math.Abs(ecart.Z) <= 6f && ReglesSurvie.PatrouilleDepassee(ecart.X, ecart.Y, avant.X, avant.Y);
                float lateral = Math.Abs(ecart.X * avant.Y - ecart.Y * avant.X);
                bool routeQuittee = Game.GameTime - f.PatrouilleDepuis > 20000 && (lateral > 70f || distance > 380f);
                if (!depasse && !routeQuittee)
                {
                    // Continuer dans son sens : viser le joueur ici provoquerait un demi-tour.
                    if (!f.OrdrePatrouille)
                    {
                        Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, f.Conducteur, v,
                            ReglesSurvie.VitessePatrouille(joueur.CurrentVehicle.Speed), CONDUITE_PATROUILLE);
                        f.OrdrePatrouille = true;
                    }
                    else Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, f.Conducteur,
                        ReglesSurvie.VitessePatrouille(joueur.CurrentVehicle.Speed));
                    return;
                }
                journal.Info("Survie : flic " + f.Numero + " patrouille -> poursuite ("
                    + (depasse ? "joueur arrivé à hauteur" : "routes séparées") + ", " + Math.Round(distance) + " m)");
            }
            f.PatrouilleDevant = false;
            if (aPied)
            {
                f.ChasseActive = false; f.OrdreTactique = -1; f.Role = 0; f.PhaseContact = false;
                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, f.Conducteur, v, pj.X, pj.Y, pj.Z, VITESSE_A_PIED_MS, 1, f.ModeleHash,
                    CONDUITE_A_PIED, 6f, 1f);
                return;
            }
            if (OrdonnerTactique(f, joueur, distance)) return;
            // La tâche suit elle-même le joueur. Ne pas la détruire toutes les 500 ms.
            int etat = f.ChasseActive
                ? Function.Call<int>(Hash.GET_SCRIPT_TASK_STATUS, conducteur, 0x55966344u) : 7; // SCRIPT_TASK_ANY
            if (ReglesSurvie.ReprendreChasse(f.ChasseActive, f.CibleChasse, joueur.Handle, etat))
            {
                Function.Call(Hash.TASK_VEHICLE_CHASE, conducteur, joueur);
                Function.Call(Hash.SET_TASK_VEHICLE_CHASE_BEHAVIOR_FLAG, conducteur, 1, true);
                Function.Call(Hash.SET_TASK_VEHICLE_CHASE_BEHAVIOR_FLAG, conducteur, 2, true);
                Function.Call(Hash.SET_TASK_VEHICLE_CHASE_BEHAVIOR_FLAG, conducteur, 32, false);
                Function.Call(Hash.SET_TASK_VEHICLE_CHASE_IDEAL_PURSUIT_DISTANCE, conducteur, 5f);
                Function.Call(Hash.SET_DRIVE_TASK_DRIVING_STYLE, conducteur, Regles.CONDUITE_TRAQUE);
                f.ChasseActive = true;
                f.CibleChasse = joueur.Handle;
                journal.Info("Survie : flic " + f.Numero + " chasse native engagée à " + Math.Round(distance) + " m (état précédent " + etat + ")");
            }
            Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, conducteur, ReglesSurvie.VitesseDePoursuite(joueur.CurrentVehicle.Speed));
        }

        bool OrdonnerTactique(FlicSurvie f, Ped joueur, float distance)
        {
            bool gauche = false, droite = false, devant = false;
            foreach (FlicSurvie autre in flics)
            {
                if (autre == f || autre.MorteA != 0 || autre.Voiture == null
                    || !autre.Voiture.Exists()) continue;
                if (autre.Role == 1) gauche = true;
                if (autre.Role == 2) droite = true;
                if (autre.Role == 3) devant = true;
            }
            int role = ReglesSurvie.ChoisirRole(f.Role, distance, gauche, droite, devant);
            if (f.Role != role)
            {
                f.Role = role; f.OrdreTactique = -1; f.ChasseActive = false; f.PhaseContact = false;
                journal.Info("Survie : flic " + f.Numero + " rôle " + NomRole(role));
            }
            if (role == 0) return false;
            Vehicle cible = joueur.CurrentVehicle;
            Vector3 ecart = f.Voiture.Position - cible.Position, sens = cible.ForwardVector;
            float longitudinal = ecart.X * sens.X + ecart.Y * sens.Y;
            float lateral = ecart.X * sens.Y - ecart.Y * sens.X;
            int maintenant = Game.GameTime;
            if (f.PhaseContact && maintenant >= f.ContactJusqua)
            {
                f.PhaseContact = false;
                f.ProchainContact = maintenant + 5000;
            }
            if (!f.PhaseContact && maintenant >= f.ProchainContact
                && ReglesSurvie.OccasionContact(role, longitudinal, lateral, ecart.Z, cible.Speed))
            {
                f.PhaseContact = true;
                f.ContactJusqua = maintenant + 3000;
            }
            int ordre = role + (f.PhaseContact ? 10 : 0);
            int etat = f.OrdreTactique == ordre
                ? Function.Call<int>(Hash.GET_SCRIPT_TASK_STATUS, f.Conducteur, 0x55966344u) : 7;
            float vitesse = ReglesSurvie.VitesseDePoursuite(cible.Speed);
            if (f.OrdreTactique != ordre || f.VehiculeCible != cible.Handle || etat == 3 || etat == 7)
            {
                // Ne pas éviter tous les véhicules : cela annule la distance d'escorte.
                const int style = 8 | 32 | 64 | 512 | 262144 | 524288;
                if (f.PhaseContact)
                    Function.Call(Hash.TASK_VEHICLE_MISSION, f.Conducteur, f.Voiture, cible,
                        role == 3 ? 3 : 2, vitesse, style, 3f, 8f, true);
                else
                    Function.Call(Hash.TASK_VEHICLE_ESCORT, f.Conducteur, f.Voiture, cible,
                        ReglesSurvie.ModePlacement(role), vitesse, style, role == 3 ? 12f : 4.5f, 0, 8f);
                f.OrdreTactique = ordre;
                f.VehiculeCible = cible.Handle;
                journal.Info("Survie : flic " + f.Numero + " " + NomRole(role)
                    + (f.PhaseContact ? " tente le contact" : " se place") + " à " + Math.Round(distance) + " m");
            }
            else Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, f.Conducteur, vitesse);
            f.ChasseActive = false;
            return true;
        }

        static string NomRole(int role)
        { return role == 1 ? "gauche" : role == 2 ? "droite" : role == 3 ? "devant" : "poursuivant"; }

        // Planté sous les yeux du joueur : il recule en braquant, une fois à
        // gauche, une fois tout droit en force (Driver 2 alterne ses stratégies).
        void Manoeuvrer(FlicSurvie f, int maintenant)
        {
            int action = f.Manoeuvres % 2 == 0 ? MANOEUVRE_TOURNE_RECULE : MANOEUVRE_RECULE_FORT;
            Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, f.Conducteur, f.Voiture, action, MANOEUVRE_MS);
            f.Manoeuvres++;
            f.OrdrePatrouille = false; f.ChasseActive = false; f.Couple = 1f;
            f.OrdreTactique = -1; f.PhaseContact = false; f.ProchainContact = maintenant + 5000;
            f.ManoeuvreJusquA = maintenant + MANOEUVRE_MS;
            f.ProchainOrdre = f.ManoeuvreJusquA;   // la manœuvre remplace sa conduite : on la redonne juste après
            f.PlanteeDepuis = 0;
            journal.Info("Survie : flic " + f.Numero + " planté sous les yeux du joueur : il recule (" + f.Manoeuvres + ")");
        }

        bool IntercepterDevant(FlicSurvie f, Ped joueur, int maintenant)
        {
            Vector3 cam = Function.Call<Vector3>(Hash.GET_FINAL_RENDERED_CAM_COORD);
            Vector3 rot = Function.Call<Vector3>(Hash.GET_FINAL_RENDERED_CAM_ROT, 2);
            PoseFlic pose = DevantSurSaRoute(joueur, cam, rot, true);
            if (pose == null) { prochaineInterception = maintenant + NOUVEL_ESSAI_MS; return false; }
            Replacer(f, pose, joueur, "INTERCEPTION TÉLÉPORTÉE", maintenant);
            prochaineInterception = maintenant + 3500;
            return true;
        }

        // Le « ping out / ping in » de Driver 2 : la même voiture, replacée
        // ailleurs, réparée (personne ne la voit), relancée vers le joueur.
        bool Recycler(FlicSurvie f, Ped joueur, string raison, int maintenant)
        {
            if (f.EmbuscadeUtilisee) return false;
            PoseFlic pose = TrouverPoint(joueur, false);
            if (pose == null) { prochainPing = maintenant + NOUVEL_ESSAI_MS / 2; return false; }
            Replacer(f, pose, joueur, raison, maintenant);
            return true;
        }

        void Replacer(FlicSurvie f, PoseFlic pose, Ped joueur, string raison, int maintenant)
        {
            Vector3 position = pose.Position;
            Vehicle v = f.Voiture;
            Function.Call(Hash.SET_ENTITY_VELOCITY, v, 0f, 0f, 0f);
            Function.Call(Hash.SET_ENTITY_COORDS, v, position.X, position.Y, position.Z, false, false, false, false);
            Function.Call(Hash.SET_ENTITY_ROTATION, v, 0f, 0f, pose.Cap, 2, true);
            Function.Call<bool>(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, v, 5f);
            // Éviter de reconstruire la carrosserie à chaque repositionnement.
            // Les épaves passent par Ranger ; ici seuls moteur et réservoir sont rétablis.
            Function.Call(Hash.SET_VEHICLE_ENGINE_HEALTH, v, 1000f);
            Function.Call(Hash.SET_VEHICLE_PETROL_TANK_HEALTH, v, 1000f);
            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, v, true, true, false);
            v.IsSirenActive = true;
            if (f.Conducteur != null && f.Conducteur.Exists() && !f.Conducteur.IsDead && !f.Conducteur.IsInVehicle(v))
                Function.Call(Hash.SET_PED_INTO_VEHICLE, f.Conducteur, v, -1);
            f.SemeDepuis = 0; f.RetardDepuis = 0; f.VenuAuContact = false;
            AdapterVitesse(f, joueur);
            Lancer(f, pose, joueur, maintenant);
            f.PlanteeDepuis = 0; f.Manoeuvres = 0; f.ManoeuvreJusquA = 0; f.ProchainOrdre = maintenant + ORDRE_MS;
            Ordonner(f, joueur, v.Position.DistanceTo(joueur.Position), !joueur.IsInVehicle());
            f.Recyclages++;
            f.DernierePoseA = maintenant;
            prochainPing = maintenant + PING_MS;
            journal.Info("Survie : flic " + f.Numero + " " + raison + " → réapparaît " + pose.Ou + " (recyclage " + f.Recyclages + ")");
        }

        // Rendu au jeu : sirène coupée, balade tranquille, plus rien d'invincible ni de « à nous ».
        void Lacher(FlicSurvie f)
        {
            try
            {
                f.Couple = 1f;
                Vehicle v = f.Voiture;
                Ped c = f.Conducteur;
                if (v != null && v.Exists())
                {
                    if (c != null && c.Exists() && !c.IsDead && c.IsInVehicle(v))
                        Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, c, v, 15f, GTAMax.CONDUITE_NORMALE);
                    v.IsSirenActive = false;
                    if (f.BonusVitesse > 0f) Function.Call(Hash.MODIFY_VEHICLE_TOP_SPEED, v, 0f);
                    Function.Call(Hash.SET_ENTITY_INVINCIBLE, v, false, false);
                    v.MarkAsNoLongerNeeded();
                }
                LacherPolicier(c);
            }
            catch (Exception ex) { journal.ErreurLimitee("survie-lacher", "Survie : libération d'un flic : " + ex.Message); }
        }

        static void LacherPolicier(Ped p)
        {
            if (p == null || !p.Exists()) return;
            Function.Call(Hash.SET_ENTITY_INVINCIBLE, p, false, false);
            GTAMax.LacherAgent(p);
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

        // Petit message de notification, une fois (pas à chaque image).
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

    // Le plugin tourne toutes les 100 ms : un texte dessiné si rarement clignote.
    // Ce petit script dessine les noms à chaque image.
    public class NomsDonateurs : Script
    {
        public NomsDonateurs()
        {
            Interval = 0;
            Tick += delegate
            {
                // Le son moteur du Zip ne doit pas rester haut pendant un cabrage sans gaz.
                try
                {
                    if (GTAMax.JoueurDisponible())
                    {
                        Ped joueur = Game.Player.Character;
                        Vehicle zip = joueur.CurrentVehicle;
                        if (zip != null && zip.Exists() && zip.Model.Hash == Game.GenerateHash("zip2000")
                            && zip.GetPedOnSeat(VehicleSeat.Driver) == joueur
                            && zip.Rotation.X > 10f
                            && Function.Call<float>(Hash.GET_CONTROL_NORMAL, 0, 71) < 0.05f
                            && Function.Call<float>(Hash.GET_DISABLED_CONTROL_NORMAL, 0, 71) < 0.05f)
                            zip.CurrentRPM = Math.Min(zip.CurrentRPM, 0.20f);
                    }
                }
                catch (Exception) { }
                Survie s = Survie.Courante;
                if (s == null) return;
                try { s.DessinerNoms(); } catch (Exception) { }
            };
        }
    }
#endif
}
