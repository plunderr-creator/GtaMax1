/* Tests des HANDLERS d'événements TikTok (evenements.js).
   Runner : node --test. Lancer depuis app/ : npm test

   Les données imitent les trames de tiktok-live-connector 2.4.0 telles que
   Final Max les lit en live (user.displayId, content, gift.name,
   gift.diamondCount, repeatCount/repeatEnd, count). Les dépendances (voix,
   pont vers le jeu, overlay) sont des espions : ces tests vérifient QUI est
   appelé avec QUOI, sans TikTok, sans Edge, sans GTA V. */
const test = require('node:test');
const assert = require('node:assert');
const { creerEvenements, extraireUtilisateur } = require('../evenements.js');
const { creerRouteur } = require('../commandes-chat.js');

const muet = { log() {}, warn() {}, error() {} };

function banc() {
  const b = { dits: [], jeu: [], ecran: [], lignes: [], sauvegardes: 0, viewers: {}, t: 5000 };
  b.ev = creerEvenements({
    tts: { ajouter: (pseudo, texte) => b.dits.push([pseudo, texte]) },
    envoyerCommande: (verbe, args) => b.jeu.push([verbe, args]),
    routerCommande: creerRouteur({ maintenant: () => b.t, log: muet }),
    viewers: () => b.viewers,
    sauver: () => { b.sauvegardes++; },
    broadcast: m => b.ecran.push(m),
    maintenant: () => b.t,
    log: { ...muet, log: s => b.lignes.push(s) },
  });
  return b;
}
const chat = (id, texte, extra = {}) => ({ user: { displayId: id, nickname: id }, content: texte, ...extra });
const don = (id, nom, diamants, extra = {}) => ({ user: { displayId: id, nickname: 'Le ' + id }, gift: { name: nom, diamondCount: diamants }, repeatCount: 1, repeatEnd: true, ...extra });

test('extraireUtilisateur : pseudo = displayId (repli uniqueId), nom = surnom nettoyé', () => {
  assert.deepStrictEqual(extraireUtilisateur({ user: { displayId: 'Bob_42', nickname: 'Bob 🔥' } }),
    { cle: 'bob_42', pseudo: 'Bob_42', nom: 'Bob 🔥' });
  assert.strictEqual(extraireUtilisateur({ user: { uniqueId: 'alice' } }).pseudo, 'alice');
  assert.strictEqual(extraireUtilisateur({ uniqueId: 'carl' }).nom, 'carl');       // pas de surnom → le pseudo
  assert.strictEqual(extraireUtilisateur({ user: { displayId: 'x', nickname: 'A‮evil\u0007' } }).nom, 'Aevil');
  assert.strictEqual(extraireUtilisateur({}).pseudo, '');
});

test('chat : message normal → lu par la voix, viewer au roster', () => {
  const b = banc();
  b.ev.onChat(chat('Bob', 'salut tout le monde'));
  assert.deepStrictEqual(b.dits, [['Bob', 'salut tout le monde']]);
  assert.deepStrictEqual(b.viewers.bob, { premiere_vue: 5000, derniere_vue: 5000, dons_total: 0 });
  assert.ok(b.sauvegardes > 0);
});

test('chat : une commande « ! » n\'est jamais lue ; !aide répond par la voix', () => {
  const b = banc();
  b.ev.onChat(chat('Bob', '!aide'));
  assert.strictEqual(b.dits.length, 1);
  assert.strictEqual(b.dits[0][0], '');                      // la réponse, pas le message
  assert.match(b.dits[0][1], /aucune commande/i);
  b.ev.onChat(chat('Bob', '!inconnue et du texte'));
  b.ev.onChat(chat('Bob', '!!!'));
  assert.strictEqual(b.dits.length, 1);                      // ni lue, ni répondue
});

test('chat : même msgId deux fois (rejeu TikTok) → une seule lecture', () => {
  const b = banc();
  b.ev.onChat(chat('Bob', 'coucou', { common: { msgId: '123' } }));
  b.ev.onChat(chat('Bob', 'coucou', { common: { msgId: '123' } }));
  assert.strictEqual(b.dits.length, 1);
});

test('chat : message vide, sans pseudo ou pavé de plus de 300 caractères → ignoré', () => {
  const b = banc();
  b.ev.onChat(chat('Bob', '   '));
  b.ev.onChat(chat('', 'orphelin'));
  b.ev.onChat(chat('Bob', 'x'.repeat(301)));
  assert.strictEqual(b.dits.length, 0);
});

test('don : alerte à l\'écran + merci vocal + SPAWN_POLICE + roster en pièces', () => {
  const b = banc();
  b.ev.onGift(don('bob', 'Rose', 1, { repeatCount: 5 }));
  assert.deepStrictEqual(b.ecran, [{ type: 'alerte_don', pseudo: 'Le bob', cadeau: 'Rose', nombre: 5, pieces: 5 }]);
  assert.deepStrictEqual(b.jeu, [['SPAWN_POLICE', { donateur: 'bob', cadeau: 'Rose' }]]);
  assert.deepStrictEqual(b.dits, [['', 'Merci bob pour Rose fois 5 !']]);
  assert.strictEqual(b.viewers.bob.dons_total, 5);
});

test('don en série : rien tant que la série n\'est pas finie, une seule voiture à la fin', () => {
  const b = banc();
  b.ev.onGift(don('bob', 'Rose', 1, { repeatCount: 1, repeatEnd: false }));
  b.ev.onGift(don('bob', 'Rose', 1, { repeatCount: 2, repeatEnd: false }));
  assert.strictEqual(b.jeu.length, 0);
  assert.strictEqual(b.ecran.length, 0);
  b.ev.onGift(don('bob', 'Rose', 1, { repeatCount: 3, repeatEnd: true }));
  assert.strictEqual(b.jeu.length, 1);
  assert.strictEqual(b.viewers.bob.dons_total, 3);
});

test('don : pont vers le jeu en panne → l\'alerte et le merci passent quand même', () => {
  const b = banc();
  const erreurs = [];
  const ev = creerEvenements({
    tts: { ajouter: (p, t) => b.dits.push([p, t]) },
    envoyerCommande: () => { throw new TypeError('pont cassé'); },
    routerCommande: () => false,
    viewers: () => b.viewers, sauver() {}, broadcast: m => b.ecran.push(m),
    maintenant: () => 1, log: { ...muet, error: s => erreurs.push(s) },
  });
  assert.doesNotThrow(() => ev.onGift(don('bob', 'Rose', 1)));
  assert.strictEqual(b.ecran.length, 1);
  assert.strictEqual(b.dits.length, 1);
  assert.match(erreurs[0], /pont cassé/);
});

// ── Tester sans live : { test: true } = même chemin, roster intact ────────

test('test : message de chat lu par la voix, roster intact, journal 🧪', () => {
  const b = banc();
  b.ev.onChat(chat('testeur', 'salut la police'), { test: true });
  assert.deepStrictEqual(b.dits, [['testeur', 'salut la police']]);
  assert.deepStrictEqual(b.viewers, {});
  assert.strictEqual(b.sauvegardes, 0);
  assert.ok(b.lignes.some(l => l.startsWith('🧪') && l.includes('salut la police')));
});

test('test : une commande « ! » passe par le vrai registre', () => {
  const b = banc();
  b.ev.onChat(chat('testeur', '!aide'), { test: true });
  assert.match(b.dits[0][1], /aucune commande/i);
  assert.deepStrictEqual(b.viewers, {});
});

test('test : cadeau → alerte + merci + SPAWN_POLICE, roster intact', () => {
  const b = banc();
  b.ev.onGift(don('testeur', 'Galaxy', 1000, { repeatCount: 2 }), { test: true });
  assert.deepStrictEqual(b.ecran, [{ type: 'alerte_don', pseudo: 'Le testeur', cadeau: 'Galaxy', nombre: 2, pieces: 2000 }]);
  assert.deepStrictEqual(b.jeu, [['SPAWN_POLICE', { donateur: 'testeur', cadeau: 'Galaxy' }]]);
  assert.strictEqual(b.dits.length, 1);
  assert.deepStrictEqual(b.viewers, {});
  assert.strictEqual(b.sauvegardes, 0);
});

test('like : agrégé par viewer, journalisé au vidage, roster à jour', () => {
  const b = banc();
  b.ev.onLike({ user: { displayId: 'Bob' }, count: 15 });
  b.ev.onLike({ user: { displayId: 'bob' }, count: 5 });
  b.ev.onLike({ user: { displayId: 'Alice' }, count: 0 });
  assert.ok(b.viewers.bob);
  assert.strictEqual(b.viewers.alice, undefined);             // 0 like = rien
  b.ev.viderLikes();
  assert.deepStrictEqual(b.lignes.filter(l => l.includes('LIKES')), ['❤️ LIKES  Bob → 20']);
  b.ev.viderLikes();
  assert.strictEqual(b.lignes.filter(l => l.includes('LIKES')).length, 1);   // vidé
});

test('follow : dédoublonné (TikTok le renvoie plusieurs fois)', () => {
  const b = banc();
  b.ev.onFollow({ user: { displayId: 'Bob' } });
  b.ev.onFollow({ user: { displayId: 'BOB' } });
  assert.strictEqual(b.lignes.filter(l => l.includes('FOLLOW')).length, 1);
  assert.ok(b.viewers.bob);
});

test('share : journalisé, roster à jour, aucune action', () => {
  const b = banc();
  b.ev.onShare({ user: { displayId: 'Bob' } });
  assert.strictEqual(b.lignes.filter(l => l.includes('SHARE')).length, 1);
  assert.ok(b.viewers.bob);
  assert.strictEqual(b.jeu.length + b.ecran.length + b.dits.length, 0);
});

test('les cinq handlers sont des fonctions nommées', () => {
  const b = banc();
  for (const n of ['onChat', 'onGift', 'onLike', 'onFollow', 'onShare']) {
    assert.strictEqual(typeof b.ev[n], 'function');
    assert.strictEqual(b.ev[n].name, n);
  }
});
