/**
 * gta-bridge.js — LE pont vers GTA V. Seul module qui écrit dans la file du jeu.
 *
 * Point d'entrée unique : envoyerCommande(verbe, args). Tout ce qui veut agir sur
 * le jeu — le don en v1, le gameplay viewer plus tard — passe par là. Rien d'autre
 * n'écrit dans <dossier GTA V>\scripts\gtamax_queue.txt.
 *
 * FORMAT (une commande par ligne) : VERBE t=<ms epoch> clé=valeur clé=valeur
 *   - VERBE : [A-Z][A-Z0-9_]* ; clés : [a-z][a-z0-9_]* ; `t` est réservé ;
 *   - valeurs encodées façon URL (encodeURIComponent) : un nom de cadeau avec
 *     espaces, un pseudo avec « = » ou un retour à la ligne ne cassent jamais le
 *     découpage. Le plugin décode avec Uri.UnescapeDataString.
 *
 * PROTOCOLE (un fichier, deux propriétaires — aucune course, aucun doublon) :
 *   - seul Node CRÉE le fichier, et seulement quand il n'existe pas : il écrit
 *     gtamax_queue.txt.tmp EN ENTIER puis le renomme (le plugin ne voit jamais
 *     un fichier à moitié écrit) ;
 *   - seul le plugin le CONSOMME : il le renomme en gtamax_queue.en-cours,
 *     le lit, le supprime ;
 *   - tant que le fichier précédent n'est pas consommé, les commandes attendent
 *     ici et on réessaie toutes les retryMs : une rafale de dons part en un seul
 *     fichier. On ne relit/réécrit JAMAIS un fichier existant : entre la lecture
 *     et le rename, le plugin aurait pu le consommer, et on rejouerait ses
 *     commandes une deuxième fois.
 *
 * PAS DE RATTRAPAGE (même règle que le TTS) : une commande de plus de ttlMs est
 * jetée. Jeu fermé pendant une heure de dons = aucune vague de voitures au
 * lancement suivant. Le plugin applique la même péremption de son côté (fichier
 * resté d'une session précédente).
 */
const fs = require('fs');

const NOM_FICHIER = 'gtamax_queue.txt';
const VERBE_RE    = /^[A-Z][A-Z0-9_]*$/;
const CLE_RE      = /^[a-z][a-z0-9_]*$/;
const VALEUR_MAX  = 200;   // une valeur est un pseudo ou un nom de cadeau, pas un roman

function formaterLigne(verbe, args, t) {
  if (typeof verbe !== 'string' || !VERBE_RE.test(verbe)) throw new TypeError('Verbe de commande invalide : ' + JSON.stringify(verbe));
  let ligne = verbe + ' t=' + Math.floor(Number(t) || 0);
  for (const [cle, valeur] of Object.entries(args || {})) {
    if (!CLE_RE.test(cle) || cle === 't') throw new TypeError('Clé de commande invalide : ' + JSON.stringify(cle));
    if (valeur === undefined || valeur === null) continue;
    // toWellFormed : un pseudo avec un demi-émoji (surrogate orphelin) ferait
    // lever encodeURIComponent — en plein live, pour un simple cadeau.
    ligne += ' ' + cle + '=' + encodeURIComponent(String(valeur).toWellFormed().slice(0, VALEUR_MAX));
  }
  return ligne;
}

/**
 * @param fichier  chemin complet de la file, ou fonction qui le rend (le dossier
 *                 du jeu se change à chaud dans le setup)
 */
function creerPont({ fichier, ttlMs = 60000, retryMs = 150, alerteMs = 10000, maintenant = Date.now, log = console }) {
  const chemin = typeof fichier === 'function' ? fichier : () => fichier;
  const attente = [];   // { ligne, t } — dans l'ordre d'arrivée
  let minuterie = null;
  let bloqueDepuis = null, alerteDonnee = false, derniereErreur = -Infinity;   // null et -Infinity : une horloge à 0 (tests) reste une vraie date

  function planifier() {
    if (minuterie) return;
    minuterie = setTimeout(() => { minuterie = null; vider(); }, retryMs);
    if (minuterie.unref) minuterie.unref();   // ne retient jamais le process (tests, arrêt)
  }

  function vider() {
    const now = maintenant();
    let perimees = 0;
    while (attente.length && now - attente[0].t > ttlMs) { attente.shift(); perimees++; }
    if (perimees) log.warn(`🚓 ${perimees} commande(s) de plus de ${Math.round(ttlMs / 1000)} s jetée(s) : le jeu ne consommait pas la file.`);
    if (!attente.length) { bloqueDepuis = null; alerteDonnee = false; return; }

    const f = chemin();
    let present = false;
    try { present = fs.existsSync(f); } catch {}
    if (present) {
      // Le plugin n'a pas encore pris le fichier précédent : on attend notre tour.
      if (bloqueDepuis === null) bloqueDepuis = now;
      if (!alerteDonnee && now - bloqueDepuis >= alerteMs) {
        alerteDonnee = true;
        log.warn(`⚠️  La file du jeu n'est pas consommée depuis ${Math.round(alerteMs / 1000)} s — GTA V est-il lancé avec le plugin GTAMax ? (${f})`);
      }
      planifier();
      return;
    }

    const tmp = f + '.tmp';
    try {
      fs.writeFileSync(tmp, attente.map(a => a.ligne).join('\n') + '\n', 'utf8');   // 1) le fichier temporaire, en entier
      fs.renameSync(tmp, f);                                                          // 2) il apparaît d'un coup sous son vrai nom
      attente.length = 0;
      bloqueDepuis = null; alerteDonnee = false;
    } catch (e) {
      try { fs.unlinkSync(tmp); } catch {}
      if (now - derniereErreur > 10000) {   // dossier absent, disque plein… : une ligne par 10 s, pas une par essai
        derniereErreur = now;
        log.error(`❌ File du jeu impossible à écrire (${f}) : ${e.message} — dossier GTA V à vérifier dans le setup.`);
      }
      planifier();
    }
  }

  // LE point d'entrée. Lève TypeError sur un verbe ou une clé invalides : c'est
  // une erreur de programmation, elle doit sauter aux yeux dès le premier test.
  function envoyerCommande(verbe, args) {
    const t = maintenant();
    const ligne = formaterLigne(verbe, args, t);
    attente.push({ ligne, t });
    log.log('🎮 → jeu : ' + ligne);
    vider();
  }

  // Lecture seule, pour /api/gta/status : n'agit jamais sur le jeu.
  function diagnostic() {
    const f = chemin();
    let present = false;
    try { present = fs.existsSync(f); } catch {}
    return { fichier: f, enAttente: attente.length, fichierPresent: present };
  }

  return { envoyerCommande, diagnostic };
}

module.exports = { creerPont, formaterLigne, NOM_FICHIER };
