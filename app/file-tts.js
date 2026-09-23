/**
 * file-tts.js — lecture vocale du chat : la FILE côté serveur.
 * (Final Max v0.3.0, server.js « TTS — LECTURE VOCALE DU CHAT », sorti du
 *  serveur pour être testable ; la logique et les réglages sont inchangés.)
 *
 * Les messages qui ne commencent pas par « ! » sont lus à voix haute par une voix
 * neurale Edge (cf. tts.js). Le mp3 est généré ICI (le serveur a internet et Node),
 * gardé en mémoire, et l'overlay le joue via GET /api/tts/<id> — le son sort donc
 * directement dans la source navigateur OBS, sans routage audio du bureau.
 * ⚠️ Pas de SAPI ni de synthèse navigateur : ils sortiraient sur la carte son du
 * bureau, pas dans la source OBS.
 * ⚠️ Impossible de pousser l'audio par WebSocket : un mp3 de phrase dépasse déjà
 * ce qu'on veut faire transiter par trame. D'où le détour HTTP.
 */

const TTS_MAX_QUEUE  = 4;     // le chat va plus vite que la voix → au-delà, on saute (jamais de retard qui s'accumule)
const TTS_MAX_AGE_MS = 7000;  // un message en attente depuis trop longtemps se ferait entendre en décalage flagrant → on saute plutôt que de rattraper le retard
const TTS_CACHE_MAX  = 20;    // mp3 gardés en mémoire (~6 Ko/s de parole → ~2 Mo max), FIFO
const TTS_MAX_CHARS  = 220;   // borne la durée de lecture d'un pavé

// Nettoyage du texte AVANT lecture vocale. Les viewers spamment émojis et caractères
// répétés pour rendre la voix pénible (« 😂😂😂😂… », « aaaaaaah !!!! ») :
//  1. on retire émojis et pictogrammes (la voix les vocalise mal ou fait du bruit) ;
//  2. on réduit toute répétition d'un même caractère à 2 (« aaaaaah » → « aah ») ;
//  3. on écrase les espaces. Un message vidé de tout (que des émojis) n'est pas lu.
// (‍ = liant ZWJ, ️ = sélecteur de variante emoji, ⃣ = touche « keycap ».)
const TTS_EMOJI_RE = /[\p{Extended_Pictographic}\p{Emoji_Modifier}\u{1F1E6}-\u{1F1FF}‍️⃣]/gu;
function nettoyer(texte) {
  return String(texte == null ? '' : texte)
    .replace(TTS_EMOJI_RE, ' ')
    .replace(/(.)\1{2,}/gu, '$1$1')  // « boooom » → « boom », « !!!! » → « !! »
    .replace(/\s+/g, ' ')
    .trim();
}

/**
 * @param synthesize (texte, voix) → Promise<Buffer mp3>   (tts.js en prod)
 * @param broadcast  (message) → void                      (vers tous les overlays)
 * @param reglages   () → { enabled, voice, volume, read_username }  (relu à chaque message : un réglage change à chaud)
 */
function creerFileTts({ synthesize, broadcast, reglages, maintenant = Date.now, log = console }) {
  const file  = [];
  const cache = new Map();   // id → Buffer mp3, servi par GET /api/tts/<id>
  let occupe = false, seq = 0;

  // force = bouton « Tester la voix » du setup (préécoute voix coupée).
  function ajouter(pseudo, texte, force) {
    const r = reglages();
    if (!r.enabled && !force) return;
    if (file.length >= TTS_MAX_QUEUE) return;
    const propre = nettoyer(texte);
    if (!propre) return;   // message tout en émojis/symboles → rien à lire, pas de place gaspillée dans la file
    const qui = r.read_username ? nettoyer(pseudo) : '';   // pseudos TikTok souvent bourrés d'émojis
    const dit = (qui ? qui + '. ' : '') + propre;
    file.push({ viewer: String(pseudo || ''), text: String(texte), spoken: dit.slice(0, TTS_MAX_CHARS), ts: maintenant() });
    pomper();
  }

  async function pomper() {
    if (occupe) return;
    let job = file.shift();
    while (job && maintenant() - job.ts > TTS_MAX_AGE_MS) job = file.shift();   // vide les messages trop vieux (retard déjà audible)
    if (!job) return;
    occupe = true;
    try {
      const r = reglages();
      const audio = await synthesize(job.spoken, r.voice);
      const id = maintenant().toString(36) + '-' + (++seq);
      cache.set(id, audio);
      while (cache.size > TTS_CACHE_MAX) cache.delete(cache.keys().next().value);
      broadcast({ type: 'tts', id, viewer: job.viewer, text: job.text.slice(0, 120), volume: r.volume, ts: job.ts });
    } catch (e) {
      // Service Edge injoignable / protocole changé → on saute le message, le live continue.
      log.warn('🔊 TTS échec (message sauté) : ' + ((e && e.message) || e));
    }
    occupe = false;
    if (file.length) pomper();
  }

  return {
    ajouter,
    audio: (id) => cache.get(String(id)),
    vider: () => { file.length = 0; },     // voix coupée : on coupe aussi ce qui attendait d'être lu
    taille: () => file.length,
  };
}

module.exports = { creerFileTts, nettoyer, TTS_MAX_QUEUE, TTS_MAX_AGE_MS, TTS_CACHE_MAX };
