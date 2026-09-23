/* Tests de bout en bout du SERVEUR — server.js lancé pour de vrai.
   Runner : node --test. Lancer depuis app/ : npm test

   COMMENT (même méthode que Final Max)
   Pas de mock : le serveur est lancé dans un dossier bac à sable (son propre
   config.json, son propre data.json, un port libre, un faux dossier GTA V avec
   un GTA5.exe vide et un scripts\ vide), puis interrogé par HTTP et par
   WebSocket comme le ferait l'overlay OBS. node_modules est monté en jonction.
   Sans SESSION_ID, connectTikTok() rend la main tout de suite : aucun test ne
   touche TikTok. La voix est coupée dans la config du bac : aucun test ne
   touche le service Edge. */
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const http = require('node:http');
const net = require('node:net');
const { spawn } = require('node:child_process');
const WebSocket = require('ws');

const APP = path.join(__dirname, '..');
const FICHIERS = ['server.js', 'tts.js', 'file-tts.js', 'gta-bridge.js', 'commandes-chat.js', 'evenements.js',
  'persistance.js', 'roster.js', 'package.json', 'config.default.json',
  'overlay.html', 'overlay.js', 'overlay.css', 'setup.html',
  path.join('outils', 'plugin.js'), path.join('outils', 'lire-config.js')];

let bac = null, jeu = null, serveur = null, PORT = 0, journal = '';
const dors = (ms) => new Promise(r => setTimeout(r, ms));

function portLibre() {
  return new Promise((resolve, reject) => {
    const s = net.createServer();
    s.once('error', reject);
    s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => resolve(p)); });
  });
}

// Requête HTTP brute : on veut pouvoir mentir sur Host et Origin, ce que fetch() ne permet pas.
function requete(methode, chemin, { corps, entetes = {} } = {}) {
  return new Promise((resolve, reject) => {
    const data = corps === undefined ? null : Buffer.from(typeof corps === 'string' ? corps : JSON.stringify(corps), 'utf8');
    const req = http.request({
      host: '127.0.0.1', port: PORT, path: chemin, method: methode,
      headers: { ...(data ? { 'Content-Type': 'application/json', 'Content-Length': data.length } : {}), ...entetes },
    }, (res) => {
      let out = '';
      res.setEncoding('utf8');
      res.on('data', c => out += c);
      res.on('end', () => resolve({ code: res.statusCode, corps: out, entetes: res.headers }));
    });
    req.on('error', reject);
    req.end(data || undefined);
  });
}

function ouvrirOverlay(entetes) {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket('ws://127.0.0.1:' + PORT, { headers: entetes || {} });
    const recus = [];
    ws.on('message', (raw) => { try { recus.push(JSON.parse(raw)); } catch {} });
    ws.on('error', reject);
    ws.on('open', () => resolve({ ws, recus }));
  });
}

async function attendre(recus, type, max = 4000) {
  const fin = Date.now() + max;
  while (Date.now() < fin) {
    const m = recus.filter(x => x.type === type);
    if (m.length) return m[m.length - 1];
    await dors(25);
  }
  throw new Error(`aucun message « ${type} » reçu en ${max} ms (reçus : ${recus.map(r => r.type).join(', ') || 'rien'})`);
}

test.before(async () => {
  bac = fs.mkdtempSync(path.join(os.tmpdir(), 'gtamax-test-'));
  fs.mkdirSync(path.join(bac, 'outils'));
  for (const f of FICHIERS) fs.copyFileSync(path.join(APP, f), path.join(bac, f));
  fs.symlinkSync(path.join(APP, 'node_modules'), path.join(bac, 'node_modules'),
    process.platform === 'win32' ? 'junction' : 'dir');
  jeu = path.join(bac, 'jeu');
  fs.mkdirSync(path.join(jeu, 'scripts'), { recursive: true });
  fs.writeFileSync(path.join(jeu, 'GTA5.exe'), '');
  PORT = await portLibre();
  fs.writeFileSync(path.join(bac, 'config.json'), JSON.stringify({
    tiktok: { username: '', region: 'eu-ttp2' },
    server: { port: PORT },
    tts: { enabled: false, voice: 'fr-FR-HenriNeural', volume: 0.8, read_username: false },
    gta: { dossier_jeu: jeu },
  }, null, 2));
  // Un .env déjà rempli à la main : ses lignes doivent survivre à l'enregistrement du code de session.
  fs.writeFileSync(path.join(bac, '.env'), '# réglage local\nALLOW_LAN_CONTROL=0\n');
  const env = { ...process.env };
  delete env.SESSION_ID; delete env.SIGN_API_KEY; delete env.TIKTOK_USERNAME; delete env.ALLOW_LAN_CONTROL;
  env.PORT = String(PORT);
  serveur = spawn(process.execPath, ['server.js'], { cwd: bac, env, stdio: ['ignore', 'pipe', 'pipe'] });
  serveur.stdout.on('data', d => { journal += d; });
  serveur.stderr.on('data', d => { journal += d; });
  const fin = Date.now() + 15000;
  while (Date.now() < fin) {
    try { const r = await requete('GET', '/api/version'); if (r.code === 200) return; } catch {}
    await dors(100);
  }
  throw new Error('serveur jamais prêt :\n' + journal);
});

test.after(async () => {
  if (serveur) { serveur.kill(); await dors(300); }
  try { fs.rmSync(bac, { recursive: true, force: true }); } catch {}
});

// ── Pages et liste blanche ────────────────────────────────────────────────

test('pages : /overlay, /setup et / répondent, assets tamponnés anti-cache', async () => {
  const o = await requete('GET', '/overlay');
  assert.strictEqual(o.code, 200);
  assert.match(o.corps, /\/overlay\.js\?v=\d+/);
  assert.strictEqual((await requete('GET', '/setup')).code, 200);
  assert.strictEqual((await requete('GET', '/')).code, 200);
  assert.strictEqual((await requete('GET', '/overlay.css')).code, 200);
});

test('liste blanche : code serveur, secrets, données et node_modules jamais servis', async () => {
  for (const chemin of ['/server.js', '/tts.js', '/gta-bridge.js', '/config.json', '/data.json', '/.env',
    '/package.json', '/outils/plugin.js', '/node_modules/ws/index.js', '/%2e%2e/%2e%2e/windows/win.ini', '/constructor']) {
    assert.strictEqual((await requete('GET', chemin)).code, 404, chemin);
  }
});

test('anti DNS-rebinding : un Host qui est un nom de domaine est refusé', async () => {
  assert.strictEqual((await requete('GET', '/api/config', { entetes: { host: 'attacker.com' } })).code, 403);
  assert.strictEqual((await requete('GET', '/overlay', { entetes: { host: 'localhost:' + PORT } })).code, 200);
});

// ── Config ────────────────────────────────────────────────────────────────

test('GET /api/config : réglages sans aucun secret', async () => {
  const c = JSON.parse((await requete('GET', '/api/config')).corps);
  assert.strictEqual(c._hasSession, false);
  assert.strictEqual(c.gta.dossier_jeu, jeu);
  assert.strictEqual(c.tts.voice, 'fr-FR-HenriNeural');
  assert.strictEqual(c._port, PORT);
  const brut = JSON.stringify(c);
  assert.ok(!/signApiKey|session_id|sessionId/.test(brut));
});

test('POST /api/config : la voix se règle à chaud et se persiste', async () => {
  const r = await requete('POST', '/api/config', { corps: { tts: { volume: 0.3, voice: 'fr-FR-DeniseNeural' } } });
  assert.strictEqual(r.code, 200, r.corps);
  const c = JSON.parse((await requete('GET', '/api/config')).corps);
  assert.strictEqual(c.tts.volume, 0.3);
  assert.strictEqual(c.tts.voice, 'fr-FR-DeniseNeural');
  const disque = JSON.parse(fs.readFileSync(path.join(bac, 'config.json'), 'utf8'));
  assert.strictEqual(disque.tts.volume, 0.3);
});

test('POST /api/config : valeurs absurdes refusées ou ignorées', async () => {
  assert.strictEqual((await requete('POST', '/api/config', { corps: { server: { port: 80 } } })).code, 400);
  await requete('POST', '/api/config', { corps: { tts: { voice: '<script>', volume: 'fort' } } });
  const c = JSON.parse((await requete('GET', '/api/config')).corps);
  assert.strictEqual(c.tts.voice, 'fr-FR-DeniseNeural');
  assert.strictEqual(c.tts.volume, 0.3);
  assert.strictEqual((await requete('POST', '/api/config', { corps: '{ cassé' })).code, 400);
});

test('POST depuis une autre origine (CSRF) : refusé', async () => {
  const r = await requete('POST', '/api/test/don', { corps: {}, entetes: { origin: 'http://evil.example' } });
  assert.strictEqual(r.code, 403);
});

// ── GTA V : état et don de test ───────────────────────────────────────────

test('GET /api/gta/status : dossier trouvé, SHVDN et plugin absents', async () => {
  const g = JSON.parse((await requete('GET', '/api/gta/status')).corps);
  assert.strictEqual(g.dossierOk, true);
  assert.strictEqual(g.shvdn, false);
  assert.strictEqual(g.plugin, 'absent');
  assert.strictEqual(g.file.fichier, path.join(jeu, 'scripts', 'gtamax_queue.txt'));
});

test('don de test : alerte sur l\'overlay ET SPAWN_POLICE dans la file du jeu', async () => {
  const { ws, recus } = await ouvrirOverlay();
  try {
    const cfgMsg = await attendre(recus, 'app_config');
    assert.ok(cfgMsg.client_version > 0);
    const r = await requete('POST', '/api/test/don', { corps: {} });
    assert.strictEqual(r.code, 200, r.corps);
    const alerte = await attendre(recus, 'alerte_don');
    assert.deepStrictEqual({ pseudo: alerte.pseudo, cadeau: alerte.cadeau, nombre: alerte.nombre },
      { pseudo: 'Testeur', cadeau: 'Rose', nombre: 1 });
    const file = fs.readFileSync(path.join(jeu, 'scripts', 'gtamax_queue.txt'), 'utf8');
    assert.match(file, /^SPAWN_POLICE t=\d+ donateur=testeur cadeau=Rose\n$/);
    assert.deepStrictEqual(fs.readdirSync(path.join(jeu, 'scripts')), ['gtamax_queue.txt']);   // aucun .tmp
  } finally { ws.close(); }
});

test('WebSocket : une page d\'une autre origine ne peut pas s\'abonner', async () => {
  await assert.rejects(ouvrirOverlay({ Origin: 'http://evil.example' }));
});

test('TTS : un id inconnu (évincé du cache) → 404 propre', async () => {
  assert.strictEqual((await requete('GET', '/api/tts/nexiste-pas')).code, 404);
});

// Dernier du fichier : il pose un SESSION_ID (sans pseudo TikTok, donc sans
// aucune tentative de connexion réseau).
test('code de session : nettoyé, rangé dans .env sans écraser le reste, jamais renvoyé', async () => {
  const secret = 'abcdef0123456789abcdef';
  const r = await requete('POST', '/api/config', { corps: { session_id: '"sessionid=' + secret + '"' } });
  assert.strictEqual(r.code, 200, r.corps);
  assert.ok(!r.corps.includes(secret));
  const envTexte = fs.readFileSync(path.join(bac, '.env'), 'utf8');
  assert.match(envTexte, /^SESSION_ID=abcdef0123456789abcdef$/m);
  assert.match(envTexte, /^# réglage local$/m);
  assert.match(envTexte, /^ALLOW_LAN_CONTROL=0$/m);
  const c = await requete('GET', '/api/config');
  assert.strictEqual(JSON.parse(c.corps)._hasSession, true);
  assert.ok(!c.corps.includes(secret));
});
