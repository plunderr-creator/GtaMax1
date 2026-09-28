// Chat → voix ; cadeaux et paliers de likes → alerte, voix et police.
const SEEN_MSG_MAX   = 600;    // derniers msgId de chat retenus (anti-rejeu), FIFO
const CHAT_MAX_CHARS = 300;    // au-delà : pavé/spam ignoré
const NOM_MAX        = 32;     // surnom affiché dans l'alerte (en points de code)
const { MODELES } = require('./progression');   // voitures du catalogue, imposables en test

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

// Identifiant TikTok et surnom affichable.
function extraireUtilisateur(data) {
  const u = (data && data.user) || {};
  const pseudo = String(u.displayId || u.uniqueId || (data && data.uniqueId) || '').trim().slice(0, 60);
  const nom = nomAffichable(u.nickname || (data && data.nickname)) || pseudo;
  return { pseudo, nom };
}

function creerEvenements({ tts, envoyerCommande, broadcast, log = console, likesParPolice = 250, fichierCredits = null }) {
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

  // Les événements simulés suivent le même chemin et sont identifiés dans le journal.
  const estTest = (opts) => !!(opts && opts.test);

  // Voix payante : chaque pièce offerte = 1 message lu. Sans crédit, le chat n'est pas lu (anti-spam).
  // Gardés dans fichierCredits (JSON { pseudo: crédits }) pour survivre à un redémarrage du serveur.
  const creditsVoix = new Map();
  const cheminCredits = () => typeof fichierCredits === 'function' ? fichierCredits() : fichierCredits;
  if (fichierCredits) {
    try { for (const [k, v] of Object.entries(JSON.parse(require('fs').readFileSync(cheminCredits(), 'utf8')))) if (v > 0) creditsVoix.set(k, v); }
    catch (e) { if (e.code !== 'ENOENT') log.error('Crédits voix illisibles : ' + e.message); }
  }
  // Crédits des cadeaux de test (« Tester sans live ») : en mémoire seulement, jamais dans le fichier.
  const creditsTest = new Map();
  function changerCredits(cle, n, test) {
    const m = test ? creditsTest : creditsVoix;
    if (n > 0) m.set(cle, n); else m.delete(cle);
    if (!fichierCredits || test) return;
    try {
      const fs = require('fs');
      const f = cheminCredits();
      fs.writeFileSync(f + '.tmp', JSON.stringify(Object.fromEntries(creditsVoix)));
      fs.renameSync(f + '.tmp', f);
    } catch (e) { log.error('Crédits voix non sauvés : ' + e.message); }
  }
  const commandesGarage = new Map();
  function onChat(data, opts) {
    const test = estTest(opts), tag = test ? '🧪 ' : '';
    if (dejaVu(data && data.common && data.common.msgId)) return;
    const { pseudo } = extraireUtilisateur(data);
    const msg = String((data && (data.content || data.comment)) || '').trim();
    if (!pseudo || !msg || msg.length > CHAT_MAX_CHARS) return;
    if (msg.startsWith('!')) {
      // Toutes les commandes viewer commencent par « !ok » : !ok garage, !ok barrage…
      const m = /^!ok(?:\s+(garage|points|niveau|voitures|voiture|lvlup|barrage|boost|acheter|aide)(?:\s+([a-z0-9_]+))?)?$/i.exec(msg);
      if (!m) return;
      let action = (m[1] || 'aide').toLowerCase(), valeur = (m[2] || '').toLowerCase();
      if ((action === 'barrage' || action === 'boost' || action === 'lvlup') && valeur) return;
      if (action === 'acheter' && valeur !== 'boost' && valeur !== 'barrage') valeur = '';
      if (action === 'barrage' || action === 'boost') { valeur = action; action = 'sort'; }
      const cle = pseudo.toLowerCase(), now = Date.now();
      if (commandesGarage.has(cle) && now - commandesGarage.get(cle) < 3000) return;
      commandesGarage.delete(cle); commandesGarage.set(cle, now);
      if (commandesGarage.size > 2000) commandesGarage.delete(commandesGarage.keys().next().value);
      try { envoyerCommande('GARAGE', { donateur: pseudo, action, valeur }); }
      catch (e) { log.error('Garage non envoye : ' + e.message); }
      return;
    }
    const credits = (test ? creditsTest : creditsVoix).get(pseudo.toLowerCase()) || 0;
    if (!credits) { log.log(`${tag}🔇 CHAT   ${pseudo} : ${msg} (sans cadeau, pas lu)`); return; }
    changerCredits(pseudo.toLowerCase(), credits - 1, test);
    log.log(`${tag}💬 CHAT   ${pseudo} : ${msg} (${credits - 1} message${credits > 2 ? 's' : ''} restant${credits > 2 ? 's' : ''})`);
    tts.ajouter(pseudo, msg);
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
    if (data.repeatEnd === 0 || data.repeatEnd === false) return;    // série en cours → on attend la fin
    const pieces = diamants * nombre;
    if (pieces > 0) changerCredits(pseudo.toLowerCase(), ((test ? creditsTest : creditsVoix).get(pseudo.toLowerCase()) || 0) + pieces, test);
    log.log(`${tag}🎁 DON    ${pseudo} → ${cadeau} ×${nombre} (${pieces} pièce${pieces > 1 ? 's' : ''})`);
    // Ce que VOIT et ENTEND le live : alerte, merci vocal nominatif (repris de
    // Final Max : le donateur veut être entendu), voiture de police en jeu.
    broadcast({ type: 'alerte_don', pseudo: nom, identifiant: pseudo, cadeau, nombre, pieces });
    tts.ajouter('', 'Merci ' + pseudo + ' pour ' + cadeau + (nombre > 1 ? ' fois ' + nombre : '') + ' !');
    try {
      // 1 point au classement par pièce (pas pour les cadeaux de test : classement réel).
      // En test, le setup peut imposer la voiture du catalogue (le profil du viewer ne change pas).
      const voiture = test && MODELES.includes(opts.voiture) ? { voiture: opts.voiture } : {};
      envoyerCommande('SPAWN_POLICE', test ? { donateur: pseudo, cadeau, ...voiture } : { donateur: pseudo, cadeau, points: pieces });
    } catch (e) {
      log.error(`❌ SPAWN_POLICE non envoyée (${pseudo}) : ${(e && e.message) || e}`);
    }
  }

  // Une voiture tous les 250 likes cumulés du live.
  let likesTotal = 0;
  function onLike(data) {
    const { pseudo } = extraireUtilisateur(data);
    const n = Math.max(0, parseInt(data && (data.count ?? data.likeCount), 10) || 0);
    if (!pseudo || !n) return;
    const avant = Math.floor(likesTotal / likesParPolice);
    likesTotal += n;
    for (let i = avant; i < Math.floor(likesTotal / likesParPolice); i++) {
      log.log(`❤️ ${(i + 1) * likesParPolice} LIKES → police (${pseudo})`);
      broadcast({ type: 'alerte_like', pseudo, identifiant: pseudo, cadeau: likesParPolice + ' likes', nombre: 1 });
      tts.ajouter('', likesParPolice + ' likes, la police arrive !');
      try {
        envoyerCommande('SPAWN_POLICE', { donateur: pseudo, cadeau: likesParPolice + ' likes' });
      } catch (err) {
        log.error(`❌ SPAWN_POLICE non envoyée (likes) : ${(err && err.message) || err}`);
      }
    }
  }
  return { onChat, onGift, onLike };
}

module.exports = { creerEvenements, extraireUtilisateur, nomAffichable };
