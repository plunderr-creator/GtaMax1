/* Tests de la FILE TTS serveur (file-tts.js).
   Runner : node --test. Lancer depuis app/ : npm test

   Les règles viennent du live de Final Max : le chat va plus vite que la voix.
   On garde au plus 4 messages en attente, on saute ceux qui attendent depuis
   plus de 7 s (un retard audible ne se rattrape pas), et on garde les 20
   derniers mp3 en mémoire pour GET /api/tts/<id>. La synthèse est simulée :
   ces tests ne touchent jamais le service Edge. */
const test = require('node:test');
const assert = require('node:assert');
const { creerFileTts, nettoyer, TTS_MAX_QUEUE, TTS_MAX_AGE_MS, TTS_CACHE_MAX } = require('../file-tts.js');

// Banc : horloge factice, synthèses en attente qu'on termine une à une.
function banc(reglages = {}) {
  const envois = [], appels = [], enCours = [];
  let t = 1000;
  const f = creerFileTts({
    synthesize: (texte, voix) => {
      appels.push({ texte, voix });
      return new Promise((ok, ko) => enCours.push({ ok: () => ok(Buffer.from(texte)), ko }));
    },
    broadcast: m => envois.push(m),
    reglages: () => ({ enabled: true, voice: 'fr-FR-HenriNeural', volume: 0.5, read_username: false, ...reglages }),
    maintenant: () => t,
    log: { log() {}, warn() {}, error() {} },
  });
  const laisser = () => new Promise(r => setImmediate(r));
  return {
    f, envois, appels,
    avance: ms => { t += ms; },
    finir: async () => { const s = enCours.shift(); if (s) s.ok(); await laisser(); },
    echouer: async () => { const s = enCours.shift(); if (s) s.ko(new Error('service injoignable')); await laisser(); },
  };
}

test('constantes de Final Max : 4 en attente, 7 s, 20 mp3', () => {
  assert.strictEqual(TTS_MAX_QUEUE, 4);
  assert.strictEqual(TTS_MAX_AGE_MS, 7000);
  assert.strictEqual(TTS_CACHE_MAX, 20);
});

test('nettoyer : émojis retirés, répétitions réduites à deux, espaces écrasés', () => {
  assert.strictEqual(nettoyer('trooop   bien 😂😂😂 !!!!'), 'troop bien !!');
  assert.strictEqual(nettoyer('😂😂🔥'), '');
});

test('plafond : au-delà de 4 en attente, on saute (jamais de retard qui s\'accumule)', () => {
  const b = banc();
  for (let i = 0; i < 10; i++) b.f.ajouter('v' + i, 'message ' + i);
  assert.strictEqual(b.appels.length, 1);            // le 1er part tout de suite en synthèse
  assert.strictEqual(b.f.taille(), TTS_MAX_QUEUE);   // 4 attendent, les 5 autres sont perdus
});

test('âge : un message de plus de 7 s est sauté au moment de sa synthèse', async () => {
  const b = banc();
  b.f.ajouter('a', 'premier');
  b.f.ajouter('b', 'vieux');
  b.avance(8000);
  b.f.ajouter('c', 'frais');
  await b.finir();                                   // fin de « premier » → on saute « vieux »
  assert.deepStrictEqual(b.appels.map(a => a.texte), ['premier', 'frais']);
});

test('broadcast : id servi par audio(id), voix et volume de la config', async () => {
  const b = banc();
  b.f.ajouter('Bob', 'salut');
  await b.finir();
  const m = b.envois[0];
  assert.strictEqual(m.type, 'tts');
  assert.strictEqual(m.viewer, 'Bob');
  assert.strictEqual(m.text, 'salut');
  assert.strictEqual(m.volume, 0.5);
  assert.strictEqual(m.ts, 1000);
  assert.strictEqual(b.appels[0].voix, 'fr-FR-HenriNeural');
  assert.ok(Buffer.isBuffer(b.f.audio(m.id)));
});

test('cache FIFO : 20 mp3 max, le plus vieux part', async () => {
  const b = banc();
  const ids = [];
  for (let i = 0; i < TTS_CACHE_MAX + 3; i++) {
    b.f.ajouter('a', 'm' + i);
    await b.finir();
    ids.push(b.envois[b.envois.length - 1].id);
  }
  assert.strictEqual(new Set(ids).size, ids.length);  // ids uniques
  assert.strictEqual(b.f.audio(ids[0]), undefined);
  assert.ok(b.f.audio(ids[ids.length - 1]));
});

test('échec du service : message sauté, la file continue', async () => {
  const b = banc();
  b.f.ajouter('a', 'perdu');
  b.f.ajouter('b', 'suivant');
  await b.echouer();
  await b.finir();
  assert.deepStrictEqual(b.envois.map(e => e.text), ['suivant']);
});

test('voix coupée : rien, sauf le test forcé', () => {
  const b = banc({ enabled: false });
  b.f.ajouter('a', 'coupé');
  assert.strictEqual(b.appels.length, 0);
  b.f.ajouter('GTA Max', 'test', true);
  assert.strictEqual(b.appels.length, 1);
});

test('read_username : le pseudo nettoyé précède le message', () => {
  const b = banc({ read_username: true });
  b.f.ajouter('Bob🔥', 'coucou');
  assert.strictEqual(b.appels[0].texte, 'Bob. coucou');
});

test('pavé : lecture bornée à 220 caractères', () => {
  const b = banc();
  b.f.ajouter('a', 'x'.repeat(10) + ' ' + 'abc '.repeat(200));
  assert.ok(b.appels[0].texte.length <= 220);
});

test('vider : ce qui attendait est jeté (bascule de la voix)', () => {
  const b = banc();
  for (let i = 0; i < 4; i++) b.f.ajouter('a', 'm' + i);
  b.f.vider();
  assert.strictEqual(b.f.taille(), 0);
});
