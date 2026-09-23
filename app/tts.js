/**
 * GTA MAX — Synthèse vocale (voix neurales Microsoft Edge)
 * (Repris tel quel de Final Max v0.3.0, qui le tenait de Plunderr : testé en live.)
 *
 * Client minimal du service « Read Aloud » d'Edge (le même que la lecture à voix
 * haute du navigateur) : gratuit, sans clé API, voix neurales naturelles.
 * Implémenté directement sur le paquet `ws` (déjà en dépendance) — npm n'est pas
 * disponible chez les clients, donc AUCUNE dépendance nouvelle.
 *
 * ⚠️ Service non officiel : nécessite internet et peut casser si Microsoft change
 * le protocole (même risque que les paquets edge-tts/msedge-tts, qui se mettent
 * à jour dans ce cas — s'inspirer d'eux pour réparer).
 *
 * Voix françaises disponibles (config.json → tts.voice) :
 *   fr-FR-DeniseNeural               (femme — défaut)
 *   fr-FR-HenriNeural                (homme)
 *   fr-FR-EloiseNeural               (enfant)
 *   fr-FR-RemyMultilingualNeural     (homme, moderne — ⚠️ multilingue : change d'accent
 *                                      selon la langue détectée du texte, ex. anglais
 *                                      sur les pseudos/messages anglophones)
 *   fr-FR-VivienneMultilingualNeural (femme, moderne — même limite que Remy)
 */

const crypto    = require('crypto');
const WebSocket = require('ws');

const TRUSTED_CLIENT_TOKEN = '6A5AA1D4EAFF4E9FB37E23D68491D6F4';
const EDGE_VERSION         = '132.0.2957.140'; // version Edge plausible pour Sec-MS-GEC-Version / User-Agent
const WSS_URL              = 'wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1';
const OUTPUT_FORMAT        = 'audio-24khz-48kbitrate-mono-mp3'; // ~6 Ko/s : léger pour le cache mémoire
const SYNTH_TIMEOUT_MS     = 15000;

// Jeton anti-abus « Sec-MS-GEC » (DRM) exigé depuis fin 2024 : SHA-256 hex majuscule
// de (horloge Windows en ticks de 100 ns, arrondie à 5 min près) + token client.
// BigInt obligatoire : les ticks (~1.3e17) dépassent Number.MAX_SAFE_INTEGER.
function secMsGec() {
  const sec   = BigInt(Math.floor(Date.now() / 1000) + 11644473600); // epoch Unix → epoch Windows (1601)
  const ticks = (sec - (sec % 300n)) * 10000000n;                    // arrondi 300 s, en unités de 100 ns
  return crypto.createHash('sha256').update(ticks.toString() + TRUSTED_CLIENT_TOKEN).digest('hex').toUpperCase();
}

function escapeXml(s) {
  return String(s).replace(/[&<>'"]/g, c => ({ '&':'&amp;', '<':'&lt;', '>':'&gt;', "'":'&apos;', '"':'&quot;' }[c]));
}

/**
 * Synthétise `text` avec la voix `voice` → Promise<Buffer> (mp3).
 * Rejette si le service est injoignable / répond mal (l'appelant saute le message).
 */
function synthesize(text, voice) {
  return new Promise((resolve, reject) => {
    const connectId = crypto.randomUUID().replace(/-/g, '');
    const url = `${WSS_URL}?TrustedClientToken=${TRUSTED_CLIENT_TOKEN}&Sec-MS-GEC=${secMsGec()}&Sec-MS-GEC-Version=1-${EDGE_VERSION}&ConnectionId=${connectId}`;

    const ws = new WebSocket(url, {
      headers: {
        'User-Agent': `Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/132.0.0.0 Safari/537.36 Edg/${EDGE_VERSION}`,
        'Origin': 'chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold',
        'Pragma': 'no-cache',
        'Cache-Control': 'no-cache',
      },
    });

    const chunks = [];
    let settled = false;
    const finish = (err, buf) => {
      if (settled) return; settled = true;
      clearTimeout(timer);
      try { ws.close(); } catch {}
      err ? reject(err) : resolve(buf);
    };
    const timer = setTimeout(() => finish(new Error('TTS timeout (' + SYNTH_TIMEOUT_MS + ' ms)')), SYNTH_TIMEOUT_MS);

    ws.on('open', () => {
      const ts = new Date().toISOString();
      ws.send(
        `X-Timestamp:${ts}\r\nContent-Type:application/json; charset=utf-8\r\nPath:speech.config\r\n\r\n` +
        JSON.stringify({ context: { synthesis: { audio: {
          metadataoptions: { sentenceBoundaryEnabled: 'false', wordBoundaryEnabled: 'false' },
          outputFormat: OUTPUT_FORMAT,
        } } } })
      );
      ws.send(
        `X-RequestId:${connectId}\r\nContent-Type:application/ssml+xml\r\nX-Timestamp:${ts}Z\r\nPath:ssml\r\n\r\n` +
        `<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='fr-FR'>` +
        `<voice name='${escapeXml(voice)}'>${escapeXml(text)}</voice></speak>`
      );
    });

    ws.on('message', (data, isBinary) => {
      if (isBinary) {
        // Trame binaire : [taille en-tête (uint16 BE)][en-tête texte][payload audio]
        const headerLen = data.readUInt16BE(0);
        const header    = data.slice(2, 2 + headerLen).toString('utf8');
        if (header.includes('Path:audio')) chunks.push(data.slice(2 + headerLen));
      } else if (String(data).includes('Path:turn.end')) {
        chunks.length ? finish(null, Buffer.concat(chunks))
                      : finish(new Error('TTS : aucune donnée audio reçue (voix inconnue ?)'));
      }
    });

    ws.on('error', (e) => finish(new Error('TTS : ' + (e && e.message || e))));
    ws.on('close', (code) => finish(new Error('TTS : connexion fermée avant la fin (code ' + code + ')')));
  });
}

module.exports = { synthesize };
