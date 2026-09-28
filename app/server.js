/* GTA Max : connexion TikTok, voix du chat, alertes et commandes de poursuite. */

// path AVANT dotenv : ancrer le .env sur LE DOSSIER DE L'APP (__dirname), pas le
// cwd — lancé hors du .bat (node app/server.js depuis ailleurs, service, IDE),
// dotenv ne trouvait pas app/.env → SESSION_ID « manquant » alors qu'il existe.
require('dotenv').config({ path: require('path').join(__dirname, '.env'), quiet: true }); // quiet:true coupe le tip promo dotenv (bruit/pub dans stdout)

// ── Forcer l'IPv4 sur toutes les connexions sortantes ───────────────────────
// Beaucoup de réseaux grand public annoncent l'IPv6 sans la router : Node tente alors
// l'IPv6 en premier et bloque en ETIMEDOUT sur les IPs Cloudflare en 2606:4700:: —
// typiquement « Failed to connect to sign server » (EulerStream est derrière Cloudflare).
// 'ipv4first' ne fait que RÉORDONNER ce que renvoie le resolver : insuffisant si une
// AAAA passe quand même. On force donc family:4 sur dns.lookup (le point commun à axios,
// got et ws) → résolution A uniquement, aucune IPv6 ne peut atteindre connect().
// Un host IPv6-only casserait, mais toute la stack (EulerStream/TikTok/Cloudflare) est
// dual-stack ; les littéraux IP et les family:6 explicites sont laissés intacts.
try {
  const dns = require('dns');
  dns.setDefaultResultOrder('ipv4first');
  const forceV4 = (opts) => {
    if (opts == null) opts = {};
    else if (typeof opts === 'number') opts = { family: opts };
    return opts.family === 6 ? opts : { ...opts, family: 4 };
  };
  const _lookup = dns.lookup;
  dns.lookup = function (hostname, options, callback) {
    if (typeof options === 'function') { callback = options; options = {}; }
    return _lookup.call(this, hostname, forceV4(options), callback);
  };
  if (dns.promises && dns.promises.lookup) {
    const _plookup = dns.promises.lookup;
    dns.promises.lookup = function (hostname, options) {
      return _plookup.call(this, hostname, forceV4(options));
    };
  }
} catch {}

const { TikTokLiveConnection } = require('tiktok-live-connector');
const { WebSocketServer }       = require('ws');
const http                      = require('http');
const fs                        = require('fs');
const path                      = require('path');
const { synthesize: ttsSynthesize } = require('./tts');        // voix neurales Edge (lecture du chat)
const { creerFileTts }              = require('./file-tts');   // file 4 / 7 s / cache 20
const { creerPont, NOM_FICHIER }    = require('./gta-bridge'); // LE pont vers le jeu
const { creerEvenements }           = require('./evenements');
const { creerSuivi } = require('./progression');
const { creerSimulation, trameChat, trameDon } = require('./simulation');   // tester sans live
const { MODELES } = require('./progression');
const { etatPlugin }                = require('./outils/plugin');

// ─── CHEMINS ─────────────────────────────────────────────
const CONFIG_PATH         = path.join(__dirname, 'config.json');
const CONFIG_DEFAULT_PATH = path.join(__dirname, 'config.default.json');
const ENV_PATH            = path.join(__dirname, '.env');
const GTA_DEFAUT          = 'C:\\Program Files (x86)\\Steam\\steamapps\\common\\Grand Theft Auto V';

// ─── LOGS FICHIER ────────────────────────────────────────
// La « fenêtre noire » se ferme et toute trace disparaît. On duplique
// console.{log,warn,error} vers app/logs/gtamax.log avec rotation simple
// (2 Mo, 1 backup .1) pour relire les erreurs APRÈS coup, téléchargeables
// en un clic via GET /api/logs.
const LOG_DIR  = path.join(__dirname, 'logs');
const LOG_PATH = path.join(LOG_DIR, 'gtamax.log');
const LOG_MAX  = 2 * 1024 * 1024; // 2 Mo avant rotation
let _logStream = null, _logBytes = 0;
function _openLog() {
  try {
    if (!fs.existsSync(LOG_DIR)) fs.mkdirSync(LOG_DIR, { recursive: true });
    if (fs.existsSync(LOG_PATH) && fs.statSync(LOG_PATH).size > LOG_MAX) {
      try { fs.renameSync(LOG_PATH, LOG_PATH + '.1'); } catch {} // garde 1 seul backup
    }
    _logBytes  = fs.existsSync(LOG_PATH) ? fs.statSync(LOG_PATH).size : 0;
    _logStream = fs.createWriteStream(LOG_PATH, { flags: 'a' });
  } catch { _logStream = null; }
}
function _writeLog(level, args) {
  if (!_logStream) return;
  try {
    const line = args.map(a => (a && a.stack) ? a.stack
      : (typeof a === 'object' ? JSON.stringify(a) : String(a))).join(' ');
    const buf = `${new Date().toISOString()} [${level}] ${line}\n`;
    _logStream.write(buf);
    _logBytes += Buffer.byteLength(buf);
    if (_logBytes > LOG_MAX) { try { _logStream.end(); } catch {} _openLog(); } // rotation à chaud (live de plusieurs heures)
  } catch {}
}
_openLog();
['log','warn','error'].forEach(m => {
  const orig = console[m].bind(console);
  console[m] = (...a) => { orig(...a); _writeLog(m === 'log' ? 'INFO' : m.toUpperCase(), a); };
});

// ─── VERSION ─────────────────────────────────────────────
const APP_VERSION = (() => { try { return require('./package.json').version || '1.0.0'; } catch { return '1.0.0'; } })();

// ─── CONFIG ──────────────────────────────────────────────
// Bootstrap + lecture robuste (Final Max). Deux objectifs :
//  1) MISE À JOUR SÛRE : le dépôt ne contient QUE config.default.json (modèle),
//     jamais le config.json local. Au 1er lancement on copie le modèle.
//  2) ANTI-BRICK : un config.json corrompu (BOM Notepad, JSON cassé) ne tue plus
//     le démarrage — on retombe sur le modèle, puis sur un défaut minimal.
function loadConfigBootstrap() {
  try {
    if (!fs.existsSync(CONFIG_PATH) && fs.existsSync(CONFIG_DEFAULT_PATH)) {
      fs.copyFileSync(CONFIG_DEFAULT_PATH, CONFIG_PATH);
      console.log('🆕 Premier lancement — config.json créé depuis le modèle.');
    }
  } catch (e) { console.error('Bootstrap config échoué :', e.message); }
  for (const p of [CONFIG_PATH, CONFIG_DEFAULT_PATH]) {   // config locale → modèle
    try {
      if (!fs.existsSync(p)) continue;
      const c = JSON.parse(fs.readFileSync(p, 'utf8').replace(/^\uFEFF/, ''));  // strip BOM
      if (c && typeof c === 'object') return c;
    } catch (e) { console.error('⚠️  config illisible (' + path.basename(p) + ') : ' + e.message); }
  }
  console.warn('⚠️  Aucune config valide — démarrage avec des valeurs minimales.');
  return { tiktok: { username: '', region: 'eu-ttp2' }, server: { port: 3200 }, gta: {} };
}
const cfg = loadConfigBootstrap();
cfg.tiktok = cfg.tiktok || {};
cfg.server = cfg.server || {};
// Défauts appliqués en mémoire : un config.json incomplet garde le comportement par défaut.
cfg.tts = { enabled: true, voice: 'fr-FR-RemyMultilingualNeural', volume: 0.8, read_username: false, ...(cfg.tts || {}) };
cfg.gta = { dossier_jeu: GTA_DEFAUT, ...(cfg.gta || {}) };

let TIKTOK_USERNAME = (process.env.TIKTOK_USERNAME || cfg.tiktok.username || '').replace(/^@/, '');
let SESSION_ID      = process.env.SESSION_ID      || '';
let SIGN_API_KEY    = process.env.SIGN_API_KEY    || (cfg.tiktok && cfg.tiktok.signApiKey) || ''; // clé EulerStream (quota de signature)
let TT_TARGET_IDC   = process.env.TT_TARGET_IDC   || cfg.tiktok.region || 'eu-ttp2';
const PORT          = parseInt(process.env.PORT, 10) || parseInt(cfg.server.port, 10) || 3200;

if (!SESSION_ID) {
  console.log('\n⚙️  SESSION_ID manquant — configurez GTA Max :');
  console.log(`   http://localhost:${PORT}/setup\n`);
}

// Une trame ou une promesse défectueuse reste visible dans le journal.
process.on('uncaughtException', e => console.error('⚠️ Exception non gérée :', e.stack || e));
process.on('unhandledRejection', e => console.error('⚠️ Promesse rejetée :', (e && e.stack) || e));

// ─── HELPERS ─────────────────────────────────────────────
function saveConfig(newCfg) { fs.writeFileSync(CONFIG_PATH, JSON.stringify(newCfg, null, 2), 'utf8'); }

// Réécrit les deux secrets dans .env SANS toucher aux autres lignes (PORT,
// ALLOW_LAN_CONTROL, commentaires). Celui de Final Max réécrivait le fichier
// entier avec ses deux clés : un PORT= posé à la main disparaissait au premier
// enregistrement du Session ID.
function saveEnv() {
  let lignes = [];
  try { lignes = fs.readFileSync(ENV_PATH, 'utf8').split(/\r?\n/); } catch {}
  const poser = (cle, val) => {
    const i = lignes.findIndex(l => new RegExp('^\\s*' + cle + '\\s*=').test(l));
    if (!val) return;
    if (i >= 0) lignes[i] = cle + '=' + val; else lignes.push(cle + '=' + val);
  };
  while (lignes.length && !lignes[lignes.length - 1].trim()) lignes.pop();
  if (!lignes.length) lignes = ['# Secrets GTA Max — ne jamais partager ce fichier'];
  poser('SESSION_ID', SESSION_ID);
  poser('SIGN_API_KEY', SIGN_API_KEY);
  fs.writeFileSync(ENV_PATH, lignes.join('\n') + '\n', 'utf8');
}

function parseBody(req) {
  return new Promise((resolve, reject) => {
    let body = '';
    req.on('data', c => { body += c; if (body.length > 50000) reject(new Error('Too large')); });
    req.on('end', () => { try { resolve(body ? JSON.parse(body) : {}); } catch { reject(new Error('Invalid JSON')); } });
    req.on('error', reject);
  });
}

function json(res, data, code = 200) {
  res.writeHead(code, { 'Content-Type': 'application/json; charset=utf-8' });
  res.end(JSON.stringify(data));
}

// ─── Garde d'écriture ────────────────────────────────────────────────
// Les routes qui MODIFIENT l'état (POST) ne sont acceptées que depuis la machine
// locale : ça bloque le sabotage depuis le réseau local (un voisin du wifi qui
// ferait apparaître des voitures de police en boucle via POST /api/test/don) ET le
// CSRF navigateur cross-site (un site malveillant qui POST vers localhost).
// Les routes GET restent ouvertes (un overlay OBS sur une 2e machine doit pouvoir
// charger la page). Pour un setup 2-PC où l'on règle à distance, mettre
// ALLOW_LAN_CONTROL=1 dans .env.
const ALLOW_LAN_CONTROL = process.env.ALLOW_LAN_CONTROL === '1';
function isLoopbackAddr(a) {
  return a === '127.0.0.1' || a === '::1' || a === '::ffff:127.0.0.1';
}
function isLocalOrigin(origin) {
  if (!origin) return true;            // pas d'en-tête Origin (curl, etc.) → décidé par l'adresse IP
  try { const h = new URL(origin).hostname; return h === 'localhost' || h === '127.0.0.1' || h === '[::1]' || h === '::1'; }
  catch { return false; }
}
function mutationAllowed(req) {
  if (ALLOW_LAN_CONTROL) return true;
  const addr = (req.socket && req.socket.remoteAddress) || '';
  return isLoopbackAddr(addr) && isLocalOrigin(req.headers.origin);
}

// ── Anti DNS-rebinding (en-tête Host) ───────────────────────────────
// Une attaque par DNS rebinding fait charger au streamer une page attacker.com
// (TTL court) puis rebinde attacker.com → 127.0.0.1 : les requêtes (HTTP + upgrade
// WebSocket) partent alors vers CE serveur avec Host: attacker.com. remoteAddress
// est loopback, donc les gardes IP seules ne suffisent pas. On exige donc que le
// Host soit un LITTÉRAL IP (loopback ou LAN) ou « localhost » — jamais un NOM de
// domaine. Une IP nue ne peut pas être une cible de rebinding (l'attaque a besoin
// d'un nom d'hôte). Ça préserve le cas 2-PC (overlay OBS sur une autre machine, qui
// charge http://<IP-LAN>:3200/overlay en GET) tout en fermant le rebinding.
// ALLOW_LAN_CONTROL=1 → tout Host accepté (l'utilisateur assume l'ouverture réseau).
function hostAllowed(hostHeader) {
  if (ALLOW_LAN_CONTROL) return true;
  const h = String(hostHeader || '').trim().toLowerCase();
  if (!h) return true;                                   // pas de Host (outil local non-navigateur) → autres gardes
  const host = (h[0] === '[') ? h.slice(1, h.indexOf(']')) : h.split(':')[0]; // [::1]:3200 → ::1 · 127.0.0.1:3200 → 127.0.0.1
  if (host === 'localhost' || host === '::1') return true;
  return /^\d{1,3}(\.\d{1,3}){3}$/.test(host);           // littéral IPv4 (loopback + LAN) ; les noms de domaine sont refusés
}

// ─── GTA V : le pont et son état ─────────────────────────
function dossierJeu() { return String(cfg.gta.dossier_jeu || GTA_DEFAUT); }
function cheminFile() { return path.join(dossierJeu(), 'scripts', NOM_FICHIER); }
const pont = creerPont({ fichier: cheminFile, log: console });

// Lecture seule (setup + démarrage) : rien ici n'écrit dans le dossier du jeu.
function etatGta() {
  const d = dossierJeu();
  const existe = (...p) => { try { return fs.existsSync(path.join(d, ...p)); } catch { return false; } };
  return {
    dossier:     d,
    dossierOk:   existe('GTA5.exe'),
    scriptHookV: existe('ScriptHookV.dll'),
    shvdn:       existe('ScriptHookVDotNet.asi') && existe('ScriptHookVDotNet3.dll'),
    plugin:      etatPlugin(d),                    // 'absent' | 'a_jour' | 'perime'
    file:        pont.diagnostic(),
  };
}

// ─── TTS — LECTURE VOCALE DU CHAT ─────────────────────────
// La file vit dans file-tts.js (règles Final Max : 4 en attente, saut à 7 s,
// cache FIFO de 20 mp3). L'overlay joue GET /api/tts/<id> : le son sort dans la
// source navigateur OBS, sans routage audio du bureau.
const tts = creerFileTts({ synthesize: ttsSynthesize, broadcast, reglages: () => cfg.tts, log: console });

// ─── ÉVÉNEMENTS TIKTOK ────────────────────────────────────
const evenements = creerEvenements({
  tts,
  envoyerCommande: pont.envoyerCommande,
  broadcast,
  log: console,
  fichierCredits: () => path.join(dossierJeu(), 'scripts', 'GTAMax.credits-voix.json'),
});

// ─── TESTER SANS LIVE ─────────────────────────────────────
// TikTok n'envoie rien hors live. Le setup fabrique donc des messages et des
// cadeaux (simulation.js) donnés aux VRAIS handlers avec { test: true } : même
// chemin qu'en live (voix, alerte, SPAWN_POLICE).
// Le live simulé est coupé dès que le vrai live se connecte (setTikTokStatus).
const simulation = creerSimulation({ evenements, log: console });

function borne(v, min, max, defaut) {
  const n = parseInt(v, 10);
  return Number.isFinite(n) ? Math.min(max, Math.max(min, n)) : defaut;
}
// Un pseudo TikTok n'a pas d'espace : « Jean Dupont » devient « Jean_Dupont ».
function pseudoDeTest(v) {
  return String(v == null ? '' : v).trim().replace(/^@/, '').replace(/\s+/g, '_').slice(0, 30) || 'testeur';
}
// Le délai laisse le temps de revenir sur GTA avant que la voiture arrive.
function plusTardTest(delaiS, fn) {
  if (delaiS > 0) setTimeout(() => { try { fn(); } catch (e) { console.error('⚠️  Test en erreur :', (e && e.stack) || e); } }, delaiS * 1000);
  else fn();
}
// Ce que le streamer doit savoir s'il n'entendra/ne verra rien.
function noteTest(avecJeu) {
  if (!clients.size) return ' ⚠️ Aucun overlay ouvert : ouvre OBS ou « Voir l\'overlay » pour entendre et voir.';
  if (!avecJeu) return '';
  const g = etatGta();
  return !g.dossierOk ? ' ⚠️ Dossier GTA V introuvable : la voiture ne partira pas.'
    : g.plugin === 'absent' ? ' ⚠️ Plugin non installé : lance « Installer plugin GTA.bat ».'
    : g.plugin === 'perime' ? ' ⚠️ Plugin pas à jour : relance « Installer plugin GTA.bat ».' : '';
}

// ─── SERVEUR HTTP ─────────────────────────────────────────
// LISTE BLANCHE STRICTE : seules ces URL servent un fichier. Tout le reste est
// 404 — pas de .env, pas de config.json, pas de data.json, pas de code serveur,
// pas de node_modules. (Final Max filtrait par extension + liste noire ; ici il
// n'y a que trois fichiers clients, on les nomme.)
const STATIC = {
  '/':            ['setup.html',   'text/html; charset=utf-8'],
  '/setup':       ['setup.html',   'text/html; charset=utf-8'],
  '/overlay':     ['overlay.html', 'text/html; charset=utf-8'],
  '/overlay.js':  ['overlay.js',   'application/javascript; charset=utf-8'],
  '/overlay.css': ['overlay.css',  'text/css; charset=utf-8'],
};

// ─── EMPREINTE ANTI-CACHE DES ASSETS CLIENTS ──────────────
// OBS (CEF) garde un cache disque PERSISTANT par source navigateur : un simple
// rechargement peut resservir l'ANCIEN overlay.js malgré les en-têtes no-cache
// — le correctif « ne prend pas » à l'antenne alors qu'il est bien sur le disque.
// On tamponne donc chaque src/href LOCAL avec le mtime du fichier : le fichier
// change → l'URL change → le cache ne peut plus mentir. Les URL externes
// (https://fonts.googleapis.com) ne matchent pas (le motif exige un chemin "/…").
function assetStamp(rel) {
  try { return String(Math.round(fs.statSync(path.join(__dirname, rel)).mtimeMs)); }
  catch { return ''; }
}
function stampAssets(html) {
  return html.replace(/(\s(?:src|href)=)(["'])\/([\w.\-/]+\.(?:js|css))\2/gi,
    (m, pre, q, rel) => { const v = assetStamp(rel); return v ? `${pre}${q}/${rel}?v=${v}${q}` : m; });
}

const httpServer = http.createServer(async (req, res) => {
  const url = req.url.split('?')[0];

  // Anti DNS-rebinding : refuse tout Host qui n'est pas loopback/LAN littéral (cf. hostAllowed).
  if (!hostAllowed(req.headers.host)) { res.writeHead(403, { 'Content-Type': 'text/plain' }); res.end('Forbidden host'); return; }

  // CORS preflight — n'autorise que les origines locales (jamais cross-site)
  if (req.method === 'OPTIONS') {
    const headers = { 'Access-Control-Allow-Methods': 'GET,POST', 'Access-Control-Allow-Headers': 'Content-Type' };
    const o = req.headers.origin;
    if (o && isLocalOrigin(o)) headers['Access-Control-Allow-Origin'] = o;
    res.writeHead(204, headers); res.end(); return;
  }

  // Garde anti-sabotage : seules les requêtes locales peuvent modifier l'état
  if (req.method !== 'GET' && !mutationAllowed(req)) {
    json(res, { ok: false, message: 'Contrôle réseau bloqué — accès local uniquement (voir ALLOW_LAN_CONTROL).' }, 403);
    return;
  }

  try {
    // ══════════════════════════════════════════════════════
    //  API CONFIG
    // ══════════════════════════════════════════════════════
    if (req.method === 'GET' && url === '/api/config') {
      // config.json édité à la main / corrompu ne doit pas faire planter la page :
      // on retombe sur la config EN MÉMOIRE (déjà validée), clonée.
      let c;
      try { c = JSON.parse(fs.readFileSync(CONFIG_PATH, 'utf8').replace(/^\uFEFF/, '')); }
      catch { c = JSON.parse(JSON.stringify(cfg)); }
      // Defense-in-depth : ne JAMAIS renvoyer les secrets, même d'un config.json legacy.
      // Le front n'a besoin que des booléens _hasSession/_hasSignKey.
      if (c.tiktok) { delete c.tiktok.signApiKey; delete c.tiktok.session_id; delete c.tiktok.sessionId; }
      c.tts = { ...cfg.tts, ...(c.tts || {}) };
      c.gta = { ...cfg.gta, ...(c.gta || {}) };
      json(res, { ...c, _version: APP_VERSION, _port: PORT, _hasSession: !!SESSION_ID, _hasSignKey: !!SIGN_API_KEY });
      return;
    }

    if (req.method === 'GET' && url === '/api/version') { json(res, { version: APP_VERSION }); return; }

    // Téléchargement des logs (backup .1 puis log courant, ordre chronologique).
    if (req.method === 'GET' && url === '/api/logs') {
      let out = '';
      if (fs.existsSync(LOG_PATH + '.1')) out += fs.readFileSync(LOG_PATH + '.1', 'utf8');
      if (fs.existsSync(LOG_PATH))        out += fs.readFileSync(LOG_PATH, 'utf8');
      res.writeHead(200, { 'Content-Type': 'text/plain; charset=utf-8', 'Content-Disposition': 'attachment; filename="gtamax.log"' });
      res.end(out || 'Aucun log pour le moment.');
      return;
    }

    if (req.method === 'POST' && url === '/api/config') {
      const body = await parseBody(req);
      // Strip du BOM comme au boot (anti-brick) : un config.json passé par
      // Notepad/PowerShell ne doit pas rendre la sauvegarde /setup impossible.
      let c;
      try { c = JSON.parse(fs.readFileSync(CONFIG_PATH, 'utf8').replace(/^\uFEFF/, '')); }
      catch { c = JSON.parse(JSON.stringify(cfg)); }
      const messages = [];

      if (body.tiktok && typeof body.tiktok === 'object') {
        c.tiktok = { ...(c.tiktok || {}) };
        if (typeof body.tiktok.username === 'string') c.tiktok.username = body.tiktok.username.trim().replace(/^@/, '').slice(0, 60);
        if (typeof body.tiktok.region === 'string' && /^[\w-]{2,20}$/.test(body.tiktok.region.trim())) c.tiktok.region = body.tiktok.region.trim();
      }
      if (body.tts && typeof body.tts === 'object') {
        const t = { ...cfg.tts, ...(c.tts || {}) };
        if (typeof body.tts.enabled === 'boolean')       t.enabled = body.tts.enabled;
        if (typeof body.tts.read_username === 'boolean') t.read_username = body.tts.read_username;
        if (typeof body.tts.voice === 'string' && /^[a-z]{2,3}-[A-Z]{2}-\w+Neural$/.test(body.tts.voice)) t.voice = body.tts.voice;
        const vol = Number(body.tts.volume);
        if (body.tts.volume !== undefined && Number.isFinite(vol)) t.volume = Math.min(1, Math.max(0, vol));
        c.tts = t;
      }
      if (body.gta && typeof body.gta === 'object' && typeof body.gta.dossier_jeu === 'string') {
        // Collé depuis l'explorateur, souvent entre guillemets ou avec un \ final.
        const d = body.gta.dossier_jeu.trim().replace(/^"+|"+$/g, '').replace(/[\\/]+$/, '');
        if (!d || d.length > 260) throw new Error('Dossier GTA V invalide');
        c.gta = { ...(c.gta || {}), dossier_jeu: d };
      }
      if (body.server && typeof body.server === 'object' && body.server.port !== undefined) {
        const p = parseInt(body.server.port, 10);
        if (!(p >= 1024 && p <= 65535)) throw new Error('Port invalide (1024 à 65535)');
        c.server = { ...(c.server || {}), port: p };
        if (p !== PORT) messages.push(`Port ${p} enregistré : redémarre GTA Max pour l'appliquer (l'overlay OBS devra pointer sur :${p}).`);
      }
      saveConfig(c);

      // Synchroniser la config EN MÉMOIRE (effet à chaud, sauf le port).
      cfg.tiktok = c.tiktok || cfg.tiktok;
      cfg.tts = { ...cfg.tts, ...(c.tts || {}) };
      cfg.gta = { ...cfg.gta, ...(c.gta || {}) };
      if (body.tts && cfg.tts.enabled === false) tts.vider();   // voix coupée : ce qui attendait est jeté aussi

      // Identifiants TikTok : appliqués EN DIRECT (pas besoin de redémarrer)
      let reconnecter = false;
      if (body.tiktok) {
        TIKTOK_USERNAME = String(cfg.tiktok.username || '').replace(/^@/, '');
        TT_TARGET_IDC   = String(cfg.tiktok.region || 'eu-ttp2');
        reconnecter = true;
      }
      if (typeof body.session_id === 'string' && body.session_id.trim()) {
        const raw = body.session_id;
        const m = raw.match(/sessionid["'\s:=]+([a-z0-9]+)/i) || raw.match(/([a-z0-9]{16,})/i);
        SESSION_ID = m ? m[1] : raw.trim();   // nettoie un "sessionid=..." collé par erreur
        saveEnv();
        reconnecter = true;
      }
      if (typeof body.sign_api_key === 'string' && body.sign_api_key.trim()) { // clé EulerStream → .env (vide = inchangée)
        SIGN_API_KEY = body.sign_api_key.trim();
        saveEnv();
        reconnecter = true;
      }
      // Seul un changement d'identifiants relance la connexion : régler la voix ou
      // le dossier du jeu ne doit pas couper le live TikTok.
      let msg = 'Enregistré ✅';
      if (reconnecter) {
        if (SESSION_ID && TIKTOK_USERNAME) {
          tiktokStatus.sessionExpired = false; reconnectDelay = 10000;
          if (reconnectTimer) { clearTimeout(reconnectTimer); reconnectTimer = null; }
          connectTikTok();
          msg = 'Enregistré ✅ Connexion à @' + TIKTOK_USERNAME + ' en cours…';
        } else if (!SESSION_ID) {
          msg = 'Enregistré. ⚠️ Ajoute ton Session ID pour connecter ton live.';
        } else {
          msg = 'Enregistré. ⚠️ Ajoute ton pseudo TikTok pour connecter ton live.';
        }
      }
      json(res, { ok: true, message: [msg, ...messages].join(' ') });
      return;
    }

    // ══════════════════════════════════════════════════════
    //  API TIKTOK — statut et reconnexion
    // ══════════════════════════════════════════════════════
    if (req.method === 'GET' && url === '/api/tiktok/status') { json(res, tiktokStatus); return; }

    if (req.method === 'POST' && url === '/api/tiktok/reconnect') {
      if (!SESSION_ID) { json(res, { ok: false, message: 'Session ID manquant — configurez-le dans le setup' }); return; }
      tiktokStatus.sessionExpired = false; // Reset pour autoriser la reconnexion
      reconnectDelay = 10000;
      if (reconnectTimer) { clearTimeout(reconnectTimer); reconnectTimer = null; }
      connectTikTok();
      console.log('🔄 Reconnexion manuelle demandée');
      json(res, { ok: true, message: 'Reconnexion en cours…' });
      return;
    }

    // ══════════════════════════════════════════════════════
    //  API TTS — lecture vocale du chat (voix neurales Edge, cf. tts.js)
    // ══════════════════════════════════════════════════════
    // Audio généré (mp3 en mémoire) : l'overlay joue l'URL reçue dans le broadcast {type:'tts'}.
    // GET ouvert (comme les statiques) : un overlay sur une 2e machine doit pouvoir le lire.
    if (req.method === 'GET' && url.startsWith('/api/tts/')) {
      let id;
      try { id = decodeURIComponent(url.slice('/api/tts/'.length)); } catch { id = ''; }
      const buf = tts.audio(id);
      if (!buf) { res.writeHead(404); res.end('Not found'); return; }   // évincé du cache (FIFO) → l'overlay saute
      res.writeHead(200, { 'Content-Type': 'audio/mpeg', 'Content-Length': buf.length, 'Cache-Control': 'no-store' });
      res.end(buf);
      return;
    }
    // Bouton « Tester la voix » — force : marche même voix coupée (préécoute avant d'activer).
    if (req.method === 'POST' && url === '/api/tts/test') {
      tts.ajouter('GTA Max', 'Test de la voix du chat. Si tu m\'entends dans OBS, tout est bon !', true);
      json(res, { ok: true, message: 'Test envoyé — écoute l\'overlay dans OBS 🔊' });
      return;
    }

    // ══════════════════════════════════════════════════════
    //  API GTA V — état du jeu/plugin, don de test
    // ══════════════════════════════════════════════════════
    if (req.method === 'GET' && url === '/api/gta/status') { json(res, etatGta()); return; }

    // ══════════════════════════════════════════════════════
    //  API TEST — tester sans live (cf. « TESTER SANS LIVE »)
    // ══════════════════════════════════════════════════════
    if (req.method === 'GET' && url === '/api/overlay/status') { json(res, { connectes: clients.size }); return; }

    // Un message de chat : lu par la voix ; les messages « ! » sont ignorés.
    if (req.method === 'POST' && url === '/api/test/chat') {
      const body = await parseBody(req);
      const message = String(body.message == null ? '' : body.message).trim().slice(0, 300);
      if (!message) { json(res, { ok: false, message: 'Écris un message à envoyer.' }, 400); return; }
      const pseudo = pseudoDeTest(body.pseudo), delai = borne(body.delai_s, 0, 60, 0);
      plusTardTest(delai, () => evenements.onChat(trameChat(pseudo, message), { test: true }));
      const quoi = message.startsWith('!') ? 'Commande de test' : 'Message de test';
      json(res, { ok: true, message: `${quoi} ${delai ? 'envoyé dans ' + delai + ' s' : 'envoyé'} 💬` + noteTest(false) });
      return;
    }

    // Un cadeau (ou une rafale de cadeaux) : alerte, merci vocal, voiture en jeu.
    // Corps vide = le bouton rapide « Tester un don » (testeur, Rose ×1).
    if (req.method === 'POST' && url === '/api/test/don') {
      const body = await parseBody(req);
      const pseudo = pseudoDeTest(body.pseudo);
      const cadeau = String(body.cadeau == null ? '' : body.cadeau).trim().slice(0, 50) || 'Rose';
      const nombre = borne(body.nombre, 1, 99, 1), rafale = borne(body.rafale, 1, 10, 1), delai = borne(body.delai_s, 0, 60, 0);
      // Voiture : '' = celle du viewer, 'hasard' = une du catalogue par cadeau, sinon le modèle choisi.
      const voiture = body.voiture === 'hasard' || MODELES.includes(body.voiture) ? body.voiture : '';
      const choisir = () => voiture === 'hasard' ? MODELES[Math.floor(Math.random() * MODELES.length)] : voiture || undefined;
      plusTardTest(delai, () => { for (let i = 0; i < rafale; i++) evenements.onGift(trameDon(pseudo, cadeau, nombre), { test: true, voiture: choisir() }); });
      const quoi = rafale > 1 ? `${rafale} cadeaux de test` : 'Cadeau de test';
      json(res, { ok: true, message: `${quoi} ${delai ? 'envoyé' + (rafale > 1 ? 's' : '') + ' dans ' + delai + ' s' : 'envoyé' + (rafale > 1 ? 's' : '')} : alerte + voiture de police 🚨` + noteTest(true) });
      return;
    }

    // Le live simulé : de faux viewers écrivent et offrent des cadeaux (10 min max).
    if (req.method === 'GET' && url === '/api/test/live') { json(res, simulation.etat()); return; }
    if (req.method === 'POST' && url === '/api/test/live') {
      const body = await parseBody(req);
      if (body.actif) {
        // Jamais de faux cadeaux mélangés à un vrai live.
        if (tiktokStatus.connected) {
          json(res, { ok: false, message: 'Tu es en live : le live simulé est bloqué pour ne pas mélanger de faux cadeaux au vrai live.', ...simulation.etat() }, 409);
          return;
        }
        simulation.demarrer();
        json(res, { ok: true, message: 'Live simulé lancé (10 min max) 🧪' + noteTest(true), ...simulation.etat() });
      } else {
        simulation.arreter('arrêté depuis le setup');
        json(res, { ok: true, message: 'Live simulé arrêté.', ...simulation.etat() });
      }
      return;
    }

    // ══════════════════════════════════════════════════════
    //  FICHIERS STATIQUES (liste blanche stricte, cf. STATIC)
    // ══════════════════════════════════════════════════════
    const statique = req.method === 'GET' && Object.hasOwn(STATIC, url) ? STATIC[url] : null;
    if (!statique) { res.writeHead(404, { 'Content-Type': 'text/plain' }); res.end('Not found'); return; }
    fs.readFile(path.join(__dirname, statique[0]), (err, data) => {
      if (err) { res.writeHead(404); res.end('Not found'); return; }
      // Pas de cache : sinon le navigateur/OBS peut servir une ancienne version et
      // faire croire qu'un correctif n'a pas pris effet. Les pages sont légères.
      let body = data;
      if (statique[0].endsWith('.html')) { try { body = Buffer.from(stampAssets(data.toString('utf8')), 'utf8'); } catch {} }
      res.writeHead(200, { 'Content-Type': statique[1], 'Cache-Control': 'no-cache, no-store, must-revalidate', 'Pragma': 'no-cache', 'Expires': '0' });
      res.end(body);
    });
  } catch (e) {
    // Corps JSON invalide, valeur refusée… : réponse propre, jamais une requête pendue.
    if (!res.headersSent) json(res, { ok: false, message: (e && e.message) || String(e) }, 400);
  }
});

// NB: écoute sur toutes les interfaces pour que `localhost` (IPv6 ::1) ET `127.0.0.1`
// fonctionnent (binder 127.0.0.1 seul casse `localhost` sur Windows). La fuite de
// secrets reste fermée par la liste blanche statique ; le contrôle depuis le réseau
// local est bloqué par mutationAllowed() (routes POST locales uniquement).
// Port déjà pris (souvent : un autre GTA Max tourne encore) → message clair + arrêt,
// sinon l'EADDRINUSE part dans uncaughtException qui maintient un process zombie.
httpServer.on('error', (e) => {
  if (e.code === 'EADDRINUSE') {
    console.error(`\n❌ Le port ${PORT} est déjà utilisé.`);
    console.error(`   GTA Max tourne probablement déjà dans une autre fenêtre.`);
    console.error(`   Fermez l'ancienne fenêtre puis relancez.\n`);
  } else {
    console.error('❌ Erreur du serveur HTTP :', e.message);
  }
  process.exit(1);
});

// ─── VERSION DU CODE CLIENT ──────────────────────────────
// mtime le plus récent des fichiers de l'overlay, envoyé à chaque connexion :
// l'overlay compare avec la version qu'il a chargée et se recharge tout seul si le
// code a changé. Sans ça, une source OBS restée ouverte tourne indéfiniment sur
// l'ancien JS en mémoire. ⚠️ La liste doit couvrir TOUS les <script>/<link> locaux
// de overlay.html.
function clientVersion() {
  return ['overlay.html', 'overlay.js', 'overlay.css']
    .reduce((m, f) => { try { return Math.max(m, fs.statSync(path.join(__dirname, f)).mtimeMs); } catch { return m; } }, 0);
}

// ─── WEBSOCKET ────────────────────────────────────────────
// Anti-CSWSH : un navigateur envoie toujours un en-tête Origin sur l'upgrade WS. On n'accepte
// que les pages servies par CE serveur (host de l'Origin == host de la requête). Pas d'Origin
// (client non-navigateur, ex. outil local) → accepté. ⚠️ L'égalité Origin==Host ne protège PAS
// du DNS rebinding (Origin ET Host valent alors attacker.com) : d'où hostAllowed() en plus.
// L'overlay ne fait qu'ÉCOUTER : tout message entrant est ignoré, le plafond est donc bas.
const WS_MAX_PAYLOAD = 64 * 1024;
const wss = new WebSocketServer({ server: httpServer, maxPayload: WS_MAX_PAYLOAD, verifyClient: (info) => {
  const req = info.req || {};
  if (!hostAllowed(req.headers && req.headers.host)) return false;   // ferme le DNS rebinding sur l'upgrade WS
  const origin = info.origin || (req.headers && req.headers.origin) || '';
  if (!origin) return true;
  try { return new URL(origin).host === String((req.headers && req.headers.host) || ''); }
  catch { return false; }
} });

const clients = new Set();
const progression = creerSuivi({ fichier: () => path.join(dossierJeu(), 'scripts', 'GTAMax.points'), broadcast });
progression.actualiser();
const progressionTimer = setInterval(() => progression.actualiser(), 250);
progressionTimer.unref();
wss.on('close', () => clearInterval(progressionTimer));

wss.on('connection', (ws, req) => {
  const addr = (req && req.socket && req.socket.remoteAddress) || '';
  clients.add(ws);
  console.log(`✅ Overlay connecté (${clients.size})`);
  ws.send(JSON.stringify({ type: 'app_config', client_version: clientVersion() }));
  ws.send(JSON.stringify({ type: 'tiktok_status', ...tiktokStatus }));
  ws.send(JSON.stringify(progression.etat()));
  ws.isAlive = true;
  ws.on('pong', () => { ws.isAlive = true; });
  // Un 'error' non écouté fait planter le process (EventEmitter) : ce listener évite un
  // crash serveur sur toute erreur socket (reset, EPIPE, trame trop grosse…).
  ws.on('error', (err) => { console.error(`⚠️ Erreur socket WS (${addr}) — ${(err && err.message) || err}`); });
  ws.on('message', () => {});   // l'overlay n'envoie rien d'utile : on ignore
  ws.on('close', () => { clients.delete(ws); console.log(`🔌 Overlay déconnecté (${clients.size} restant)`); });
});

// Mort SALE (crash / mise en veille / coupure réseau) : pas de close TCP → le
// ws.on('close') ne se déclenche jamais. On détecte ici, toutes les 5 s.
const pingInterval = setInterval(() => {
  for (const ws of clients) {
    if (!ws.isAlive) { ws.terminate(); clients.delete(ws); continue; }
    ws.isAlive = false;
    try { ws.ping(); } catch {}
  }
}, 5000);
wss.on('close', () => clearInterval(pingInterval));

function broadcast(data) {
  if (!data || typeof data.type !== 'string') return;
  const msg = JSON.stringify(data);
  for (const c of clients) if (c.readyState === 1) c.send(msg);
}

// ─── STATUT TIKTOK ────────────────────────────────────────
let tiktokStatus = { connected: false, username: TIKTOK_USERNAME, reason: '', sessionExpired: false };

function setTikTokStatus(connected, reason = '') {
  // Détecter expiration de session (seulement si un SESSION_ID est réellement configuré :
  // sinon "Session ID manquant" serait classé à tort comme une session expirée).
  const expired = !connected && !!SESSION_ID && (
    /session/i.test(reason) || /login/i.test(reason) ||
    /unauthorized/i.test(reason) || /auth/i.test(reason) ||
    /banned/i.test(reason) || /invalid/i.test(reason)
  );
  tiktokStatus = { connected, username: TIKTOK_USERNAME, reason, sessionExpired: expired };
  broadcast({ type: 'tiktok_status', ...tiktokStatus });
  // Le vrai live arrive : plus un seul faux cadeau à partir de maintenant.
  if (connected && simulation.etat().actif) simulation.arreter('ton vrai live TikTok vient de se connecter');
  if (expired) console.error('\n🔑 SESSION EXPIRÉE — Reconfigurer sur http://localhost:' + PORT + '/setup\n');
}

// ─── CONNEXION TIKTOK ─────────────────────────────────────
let tiktok = null, reconnectTimer = null, reconnectDelay = 10000, reconnectCapMs = 30000;

// Les trois événements utilisés, chacun vers son handler nommé (evenements.js). Le try/catch
// est ici pour qu'un handler qui lève sur une trame malformée ne remonte jamais
// dans la boucle de lecture du connecteur.
const HANDLERS_TIKTOK = {
  chat:   evenements.onChat,
  gift:   evenements.onGift,
  like:   evenements.onLike,
};

function connectTikTok() {
  if (!SESSION_ID) { setTikTokStatus(false, 'Session ID manquant'); return; }
  if (!TIKTOK_USERNAME) { setTikTokStatus(false, 'Pseudo TikTok manquant'); return; }
  if (reconnectTimer) { clearTimeout(reconnectTimer); reconnectTimer = null; }
  // Fermer proprement une éventuelle connexion précédente (changement de pseudo/session sans redémarrage)
  if (tiktok) { try { if (tiktok.removeAllListeners) tiktok.removeAllListeners(); if (tiktok.disconnect) tiktok.disconnect(); } catch (e) {} tiktok = null; }

  tiktok = new TikTokLiveConnection(TIKTOK_USERNAME, {
    enableExtendedGiftInfo: false,
    fetchRoomInfoOnConnect: false,
    signApiKey: SIGN_API_KEY || undefined, // EulerStream (https://www.eulerstream.com) — sans clé = tier anonyme rate-limité
    // v2.4 : options standard sessionId + ttTargetIdc (l'ancien bundle "session" cassait le
    // handshake WebSocket — "Unexpected server response: 200")
    sessionId: SESSION_ID,
    ttTargetIdc: TT_TARGET_IDC,
  });

  tiktok.on('disconnected', reason => {
    const r = (reason && (reason.message || reason.reason)) || (typeof reason === 'string' ? reason : 'Déconnecté');
    console.log(`⚠️  Déconnecté : ${r}`);
    setTikTokStatus(false, r);
    scheduleReconnect();
  });

  for (const [evenement, handler] of Object.entries(HANDLERS_TIKTOK)) {
    tiktok.on(evenement, (data) => {
      try { handler(data); }
      catch (e) { console.error(`⚠️  Événement ${evenement} en erreur (live maintenu) :`, (e && e.stack) || e); }
    });
  }

  tiktok.connect()
    .then(() => {
      reconnectDelay = 10000;
      console.log(`🎮 Connecté au live TikTok de @${TIKTOK_USERNAME}\n`);
      setTikTokStatus(true);
    })
    .catch(err => {
      const m = String((err && err.message) || err);
      const rateLimited = /retry-after|rate.?limit|429|signature/i.test(m);
      console.error('❌ Connexion impossible :', m + (rateLimited && !SIGN_API_KEY ? '  → rate limit EulerStream : ajoute une clé SIGN_API_KEY (gratuite sur eulerstream.com)' : ''));
      setTikTokStatus(false, rateLimited ? 'Rate limit signature — ajoute SIGN_API_KEY' : m);
      // Palier max du backoff : 60s si vrai rate limit EulerStream (ne pas marteler l'API),
      // 30s sinon — un "Unexpected server response" est en général une micro-panne transitoire
      // côté TikTok/EulerStream pendant le handshake WS ; à 120s de palier, une mauvaise passe
      // en plein live faisait perdre jusqu'à 2 min de chat d'un coup.
      reconnectCapMs = rateLimited ? 60000 : 30000;
      if (rateLimited) reconnectDelay = Math.max(reconnectDelay, 60000);
      scheduleReconnect();
    });
}

function scheduleReconnect() {
  if (tiktokStatus.sessionExpired) return; // Ne pas reconnecter si session expirée
  if (reconnectTimer) return;
  console.log(`🔄 Reconnexion dans ${reconnectDelay / 1000}s...`);
  reconnectTimer = setTimeout(() => {
    reconnectTimer = null;
    reconnectDelay = Math.min(reconnectDelay * 2, reconnectCapMs);
    connectTikTok();
  }, reconnectDelay);
}

// ─── DÉMARRAGE ────────────────────────────────────────────
httpServer.listen(PORT, () => {
  const g = etatGta();
  console.log(`📦 GTA Max v${APP_VERSION}`);
  console.log(`🌐 Réglages        → http://localhost:${PORT}/setup`);
  console.log(`   Overlay OBS     → http://localhost:${PORT}/overlay   (source Navigateur 1080×1920)`);
  console.log(`🎮 GTA V           → ${g.dossier}`);
  if (!g.dossierOk)           console.warn('⚠️  GTA5.exe introuvable dans ce dossier — à régler dans le setup.');
  else if (!g.shvdn)          console.warn('⚠️  ScriptHookVDotNet v3 absent du dossier du jeu — voir README.');
  else if (g.plugin === 'absent') console.warn('⚠️  Plugin GTAMax non installé — lance « Installer plugin GTA.bat ».');
  else if (g.plugin === 'perime') console.warn('⚠️  Plugin GTAMax plus vieux que sa source — relance « Installer plugin GTA.bat ».');
  else                        console.log('✅ Plugin GTAMax installé et à jour.');
  console.log('');
  connectTikTok();
});
