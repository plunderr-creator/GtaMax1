/**
 * evenements.js — les cinq événements TikTok, un handler nommé par événement.
 *
 * Tous sont branchés, journalisés et tiennent le roster à jour ; en v1 seuls
 * `chat` (voix + commandes « ! ») et `gift` (alerte + police en jeu) agissent.
 * like / follow / share sont prêts pour le gameplay futur : leur handler est
 * l'endroit où il se greffera.
 *
 * Lecture des trames reprise de Final Max (tiktok-live-connector 2.4.0) :
 * pseudo = user.displayId (repli uniqueId), texte = content, cadeau =
 * gift.name / gift.diamondCount, série = repeatCount + repeatEnd, likes = count.
 */
const roster = require('./roster');
const { parserCommande } = require('./commandes-chat');

const SEEN_MSG_MAX   = 600;    // derniers msgId de chat retenus (anti-rejeu), FIFO
const CHAT_MAX_CHARS = 300;    // au-delà : pavé/spam, ni lu ni routé (règle Final Max)
const NOM_MAX        = 32;     // surnom affiché dans l'alerte (en points de code)

// Surnom TikTok → texte affichable. Les surnoms sont libres : on retire les
// caractères de contrôle et les inversions de sens d'écriture (U+202E retourne
// tout le texte qui suit), on calme le « zalgo » (empilements d'accents) et on
// borne la longueur. L'overlay l'affiche en textContent : jamais de HTML.
function nomAffichable(brut) {
  const propre = String(brut == null ? '' : brut)
    .replace(/[\p{Cc}‎‏‪-‮⁦-⁩]/gu, '')
    .replace(/(\p{M}{2})\p{M}+/gu, '$1')
    .trim();
  return Array.from(propre).slice(0, NOM_MAX).join('');
}

// { cle: pseudo en minuscules (roster), pseudo: tel que TikTok l'écrit, nom: surnom affichable }
function extraireUtilisateur(data) {
  const u = (data && data.user) || {};
  const pseudo = String(u.displayId || u.uniqueId || (data && data.uniqueId) || '').trim().slice(0, 60);
  const nom = nomAffichable(u.nickname || (data && data.nickname)) || pseudo;
  return { cle: pseudo.toLowerCase(), pseudo, nom };
}

/**
 * @param tts             { ajouter(pseudo, texte) }         file-tts.js
 * @param envoyerCommande (verbe, args)                      gta-bridge.js — le SEUL pont vers le jeu
 * @param routerCommande  (cmd, ctx) → bool                  commandes-chat.js
 * @param viewers         () → roster (objet muté en place)
 * @param sauver          () → persistance débouncée
 * @param broadcast       (message) → overlays
 */
function creerEvenements({ tts, envoyerCommande, routerCommande, viewers, sauver, broadcast,
                           maintenant = Date.now, log = console, likesMs = 5000 }) {
  // Contexte donné aux commandes chat : ce qu'une commande a le droit de faire.
  const ctxCommande = {
    repondre: (texte) => tts.ajouter('', texte),
    envoyerCommande,
    viewers,
    sauver,
    log,
  };

  function toucher(pseudo) {
    if (roster.vu(viewers(), pseudo, maintenant())) sauver();
  }

  // Anti-doublon : TikTok renvoie parfois la même trame de chat plusieurs fois
  // (doublons réseau, rejeu des derniers messages à la reconnexion). Sans ça, un
  // message est lu 2× par la voix. Sans msgId, on ne déduplique pas (mieux vaut
  // lire deux fois que rater un message).
  const vusIds = new Set(), vusOrdre = [];
  function dejaVu(msgId) {
    if (!msgId) return false;
    if (vusIds.has(msgId)) return true;
    vusIds.add(msgId); vusOrdre.push(msgId);
    if (vusOrdre.length > SEEN_MSG_MAX) vusIds.delete(vusOrdre.shift());
    return false;
  }

  // TESTER SANS LIVE : opts.test = trame fabriquée (setup, live simulé). Même
  // chemin que le vrai live — voix, commandes, alerte, SPAWN_POLICE — mais le
  // roster n'est JAMAIS touché : un faux viewer n'a rien à faire dans la table
  // sur laquelle le gameplay futur se posera. Journal préfixé 🧪.
  const estTest = (opts) => !!(opts && opts.test);

  // ─── CHAT ───────────────────────────────────────────────
  // Message normal → voix. Message « ! » → jamais lu : parseur + registre de
  // commandes (commandes-chat.js), la couture du gameplay viewer.
  function onChat(data, opts) {
    const test = estTest(opts), tag = test ? '🧪 ' : '';
    if (dejaVu(data && data.common && data.common.msgId)) return;
    const { pseudo } = extraireUtilisateur(data);
    const msg = String((data && (data.content || data.comment)) || '').trim();
    if (!pseudo || !msg || msg.length > CHAT_MAX_CHARS) return;
    if (!test) toucher(pseudo);
    if (!msg.startsWith('!')) {
      log.log(`${tag}💬 CHAT   ${pseudo} : ${msg}`);
      tts.ajouter(pseudo, msg);
      return;
    }
    const cmd = parserCommande(msg, pseudo);
    if (!cmd) { log.log(`${tag}💬 CHAT   ${pseudo} : ${msg} (commence par « ! » : non lu)`); return; }
    routerCommande(cmd, ctxCommande);
  }

  // ─── CADEAU ─────────────────────────────────────────────
  // Une voiture de police par cadeau. Une série (combo de roses) compte UNE
  // fois, à sa fin (repeatEnd), avec son total — comme Final Max.
  function onGift(data, opts) {
    const test = estTest(opts), tag = test ? '🧪 ' : '';
    const { pseudo, nom } = extraireUtilisateur(data);
    if (!pseudo) return;
    const g = (data && data.gift) || {}, gd = (data && data.giftDetails) || {};
    const cadeau   = String(g.name || gd.giftName || data.giftName || 'Cadeau').slice(0, 50);
    const diamants = Math.max(0, parseInt(g.diamondCount ?? gd.diamondCount ?? data.diamondCount ?? 0, 10) || 0);
    const nombre   = Math.max(1, parseInt(data.repeatCount, 10) || 1);
    if (!test) toucher(pseudo);                                       // série en cours = le viewer est là
    if (data.repeatEnd === 0 || data.repeatEnd === false) return;    // série en cours → on attend la fin
    const pieces = diamants * nombre;
    log.log(`${tag}🎁 DON    ${pseudo} → ${cadeau} ×${nombre} (${pieces} pièce${pieces > 1 ? 's' : ''})`);
    if (!test) {
      roster.ajouterDon(viewers(), pseudo, pieces, maintenant());
      sauver();
    }
    // Ce que VOIT et ENTEND le live : alerte, merci vocal nominatif (repris de
    // Final Max : le donateur veut être entendu), voiture de police en jeu.
    broadcast({ type: 'alerte_don', pseudo: nom, cadeau, nombre, pieces });
    tts.ajouter('', 'Merci ' + pseudo + ' pour ' + cadeau + (nombre > 1 ? ' fois ' + nombre : '') + ' !');
    try {
      envoyerCommande('SPAWN_POLICE', { donateur: pseudo, cadeau });
    } catch (e) {
      log.error(`❌ SPAWN_POLICE non envoyée (${pseudo}) : ${(e && e.message) || e}`);
    }
  }

  // ─── LIKES ──────────────────────────────────────────────
  // Rafales : on agrège par viewer et on journalise une ligne par viewer toutes
  // les likesMs, au lieu d'une ligne par trame.
  const likes = new Map();   // cle → { pseudo, n }
  function onLike(data) {
    const { cle, pseudo } = extraireUtilisateur(data);
    const n = Math.max(0, parseInt(data && (data.count ?? data.likeCount), 10) || 0);
    if (!pseudo || !n) return;
    toucher(pseudo);
    const e = likes.get(cle) || { pseudo, n: 0 };
    e.n += n;
    likes.set(cle, e);
  }
  function viderLikes() {
    for (const e of likes.values()) log.log(`❤️ LIKES  ${e.pseudo} → ${e.n}`);
    likes.clear();
  }
  const minuterieLikes = setInterval(viderLikes, likesMs);
  if (minuterieLikes.unref) minuterieLikes.unref();

  // ─── FOLLOW ─────────────────────────────────────────────
  // Dédoublonné pour la durée du process : TikTok renvoie l'événement
  // plusieurs fois par follower.
  const followVus = new Set();
  function onFollow(data) {
    const { cle, pseudo } = extraireUtilisateur(data);
    if (!pseudo || followVus.has(cle)) return;
    followVus.add(cle);
    toucher(pseudo);
    log.log(`💙 FOLLOW ${pseudo}`);
  }

  // ─── PARTAGE ────────────────────────────────────────────
  function onShare(data) {
    const { pseudo } = extraireUtilisateur(data);
    if (!pseudo) return;
    toucher(pseudo);
    log.log(`📣 SHARE  ${pseudo}`);
  }

  return { onChat, onGift, onLike, onFollow, onShare, viderLikes };
}

module.exports = { creerEvenements, extraireUtilisateur, nomAffichable };
