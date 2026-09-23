/**
 * simulation.js — tester sans être en live.
 *
 * TikTok n'envoie rien tant qu'on n'est pas en live. On fabrique donc des
 * trames de la MÊME forme que tiktok-live-connector et on les donne aux VRAIS
 * handlers (evenements.js) avec { test: true } : voix, commandes « ! »,
 * alerte, merci et SPAWN_POLICE suivent exactement le chemin d'un live, sans
 * jamais toucher au roster.
 *
 *   trameChat / trameDon  → un message / un cadeau à la demande (setup)
 *   creerSimulation       → le « live simulé » : de faux viewers qui écrivent
 *                           et offrent des cadeaux, 10 min au plus
 */

// Pièces des cadeaux proposés dans le setup (valeurs TikTok courantes). Un nom
// inconnu vaut 1 pièce : en test, seules l'alerte et la voiture comptent.
const PIECES_CADEAUX = {
  'Rose': 1,
  'Perfume': 20,
  'Hand Hearts': 100,
  'Galaxy': 1000,
  'TikTok Universe': 44999,
};

function trameChat(pseudo, message, nom) {
  return { user: { displayId: pseudo, uniqueId: pseudo, nickname: nom || pseudo }, content: message };
}

function trameDon(pseudo, cadeau, nombre, nom, pieces) {
  const diamants = pieces !== undefined ? pieces : (Object.hasOwn(PIECES_CADEAUX, cadeau) ? PIECES_CADEAUX[cadeau] : 1);
  return {
    user: { displayId: pseudo, uniqueId: pseudo, nickname: nom || pseudo },
    gift: { name: cadeau, diamondCount: diamants },
    repeatCount: nombre || 1,
    repeatEnd: true,   // série terminée : le cadeau compte tout de suite
  };
}

// ─── LIVE SIMULÉ ──────────────────────────────────────────
const FAUX_VIEWERS = [
  ['lucas_du_13', 'Lucas du 13'], ['ines.gaming', 'Inès 🎮'], ['kevin_42', 'Kévin'],
  ['sarah_ptdr', 'Sarah 🔥'], ['tony_m', 'Tony M.'], ['mehdi_rs3', 'Mehdi RS3'],
  ['xx_nico_xx', 'xX Nico Xx'], ['chloe.v', 'Chloé'], ['yanis_75', 'Yanis'],
  ['jade_lpb', 'Jade'], ['maxime.gta', 'Maxime 🚓'], ['lea_off', 'Léa'],
];
const MESSAGES = [
  'wesh la police arrive 😂', 'fonce fonce fonce !', 'tu vas te faire attraper', 'trop stylé le gyrophare',
  'GG', 'c\'est quelle voiture ?', 'la sirène française 🔥', 'envoie les flics !', 'tourne à gauche !!',
  'salut tout le monde', '!aide', 'mdr', 'c\'est GTA 5 ?', 'ils vont te coincer', 'accélère !!!', 'bonne chance 🍀',
];
const CADEAUX_SIMULES = ['Rose', 'Rose', 'Rose', 'Perfume', 'Hand Hearts', 'Galaxy'];   // la rose domine, comme en vrai

const CHAT_MIN_MS = 5000,  CHAT_MAX_MS = 10000;
const DON_MIN_MS  = 20000, DON_MAX_MS  = 40000;

/**
 * @param evenements { onChat(data, opts), onGift(data, opts) }  (evenements.js)
 * planifier/annuler/maintenant/hasard injectables : les tests avancent le temps à la main.
 */
function creerSimulation({ evenements, maintenant = Date.now, planifier = setTimeout, annuler = clearTimeout,
                           hasard = Math.random, log = console, dureeMaxMs = 10 * 60000 }) {
  let actif = false, finA = 0;
  const minuteries = new Set();
  const pioche = (liste) => liste[Math.min(liste.length - 1, Math.floor(hasard() * liste.length))];

  function plusTard(fn, minMs, maxMs) {
    const id = planifier(() => { minuteries.delete(id); if (actif) fn(); }, minMs + hasard() * (maxMs - minMs));
    minuteries.add(id);
  }

  function tempsEcoule() {
    if (maintenant() < finA) return false;
    arreter('durée maximale atteinte (' + Math.round(dureeMaxMs / 60000) + ' min)');
    return true;
  }

  function tourChat() {
    if (tempsEcoule()) return;
    const [pseudo, nom] = pioche(FAUX_VIEWERS);
    evenements.onChat(trameChat(pseudo, pioche(MESSAGES), nom), { test: true });
    plusTard(tourChat, CHAT_MIN_MS, CHAT_MAX_MS);
  }

  function tourDon() {
    if (tempsEcoule()) return;
    const [pseudo, nom] = pioche(FAUX_VIEWERS);
    evenements.onGift(trameDon(pseudo, pioche(CADEAUX_SIMULES), 1 + Math.floor(hasard() * 3), nom), { test: true });
    plusTard(tourDon, DON_MIN_MS, DON_MAX_MS);
  }

  function demarrer() {
    if (actif) return false;
    actif = true;
    finA = maintenant() + dureeMaxMs;
    log.log(`🧪 Live simulé démarré (${Math.round(dureeMaxMs / 60000)} min max) : faux messages toutes les 5-10 s, faux cadeaux toutes les 20-40 s.`);
    plusTard(tourChat, 1000, 2000);   // premier message tout de suite : on sait que ça marche
    plusTard(tourDon, 6000, 9000);    // premier cadeau vite aussi
    return true;
  }

  function arreter(raison) {
    if (!actif) return false;
    actif = false;
    for (const id of minuteries) annuler(id);
    minuteries.clear();
    log.log('🧪 Live simulé arrêté' + (raison ? ' : ' + raison : '') + '.');
    return true;
  }

  function etat() {
    return { actif, resteMs: actif ? Math.max(0, finA - maintenant()) : 0 };
  }

  return { demarrer, arreter, etat };
}

module.exports = { creerSimulation, trameChat, trameDon, PIECES_CADEAUX };
