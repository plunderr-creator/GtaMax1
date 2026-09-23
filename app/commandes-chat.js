/**
 * commandes-chat.js — commandes « ! » du chat : parseur + registre + routeur.
 *
 * LA couture du gameplay viewer. Un message qui commence par « ! » devient
 * { commande, args, pseudo } et part vers le registre COMMANDES. En v1 il ne
 * contient que !aide : le contenu du gameplay n'est pas encore décidé.
 * Ces messages ne sont JAMAIS lus par la voix (evenements.js les écarte avant
 * le TTS, comme Final Max).
 *
 * AJOUTER UNE COMMANDE = ajouter une entrée au registre :
 *   nom: {
 *     recharge_ms: 60000,               // optionnel, anti-spam GLOBAL de la commande
 *     executer(cmd, ctx) { … },         // cmd = { commande, args, pseudo }
 *   }
 * ctx fourni par le serveur :
 *   repondre(texte)                 → réponse à voix haute (file TTS)
 *   envoyerCommande(verbe, args)    → action en jeu (gta-bridge.js, le seul pont)
 *   viewers()                       → le roster { pseudo: { premiere_vue, derniere_vue, dons_total } }
 *   sauver()                        → persiste le roster (débouncé)
 */

// « !Aide moi » → { commande:'aide', args:['moi'] }. La ponctuation collée au
// nom est tolérée (« !aide? »), un « ! » isolé ou suivi d'une espace n'est pas
// une commande (mais reste hors de la voix : il commence par « ! »).
function parserCommande(message, pseudo) {
  const m = String(message == null ? '' : message).trim().match(/^!(\S+)(?:\s+([\s\S]*))?$/u);
  if (!m) return null;
  const commande = m[1].normalize('NFC').toLowerCase().replace(/[^\p{L}\p{N}_]/gu, '');
  if (!commande) return null;
  const args = m[2] ? m[2].trim().split(/\s+/).filter(Boolean) : [];
  return { commande, args, pseudo: String(pseudo == null ? '' : pseudo) };
}

const COMMANDES = {
  // !aide — en v1, la seule commande : il n'y a encore rien à jouer.
  aide: {
    recharge_ms: 30000,   // la réponse passe par la voix : un chat qui spamme !aide ne doit pas la monopoliser
    executer(cmd, ctx) {
      ctx.repondre('Aucune commande n\'est encore disponible, ça arrive bientôt !');
    },
  },
};

// Un routeur par serveur : il garde la dernière exécution de chaque commande
// (recharge). Rend true si la commande existe (exécutée ou en recharge).
function creerRouteur({ commandes = COMMANDES, maintenant = Date.now, log = console } = {}) {
  const derniere = new Map();
  return function routerCommande(cmd, ctx) {
    if (!cmd) return false;
    // Object.hasOwn : « !constructor » ou « !__proto__ » ne doivent jamais
    // tomber sur un membre hérité d'Object.
    const def = Object.hasOwn(commandes, cmd.commande) ? commandes[cmd.commande] : null;
    if (!def || typeof def.executer !== 'function') {
      log.log(`💬 Commande inconnue : !${cmd.commande} (${cmd.pseudo}) — ignorée`);
      return false;
    }
    const t = maintenant();
    const avant = derniere.has(cmd.commande) ? derniere.get(cmd.commande) : -Infinity;
    if (def.recharge_ms > 0 && t - avant < def.recharge_ms) {
      log.log(`💬 !${cmd.commande} en recharge (${cmd.pseudo}) — ignorée`);
      return true;
    }
    derniere.set(cmd.commande, t);
    log.log(`💬 CMD    ${cmd.pseudo} → !${cmd.commande}${cmd.args.length ? ' ' + cmd.args.join(' ') : ''}`);
    try { def.executer(cmd, ctx); }
    catch (e) { log.error(`⚠️  Commande !${cmd.commande} en erreur (live maintenu) : ${(e && e.stack) || e}`); }
    return true;
  };
}

module.exports = { parserCommande, creerRouteur, COMMANDES };
