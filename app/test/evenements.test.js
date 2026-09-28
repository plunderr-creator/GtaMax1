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

const muet = { log() {}, warn() {}, error() {} };

function banc() {
  const b = { dits: [], jeu: [], ecran: [], lignes: [],  };
  b.ev = creerEvenements({
    tts: { ajouter: (pseudo, texte) => b.dits.push([pseudo, texte]) },
    envoyerCommande: (verbe, args) => b.jeu.push([verbe, args]),
    broadcast: m => b.ecran.push(m),
    log: { ...muet, log: s => b.lignes.push(s) },
  });
  return b;
}
const chat = (id, texte, extra = {}) => ({ user: { displayId: id, nickname: id }, content: texte, ...extra });
const don = (id, nom, diamants, extra = {}) => ({ user: { displayId: id, nickname: 'Le ' + id }, gift: { name: nom, diamondCount: diamants }, repeatCount: 1, repeatEnd: true, ...extra });

test('extraireUtilisateur : pseudo = displayId (repli uniqueId), nom = surnom nettoyé', () => {
  assert.deepStrictEqual(extraireUtilisateur({ user: { displayId: 'Bob_42', nickname: 'Bob 🔥' } }),
    { pseudo: 'Bob_42', nom: 'Bob 🔥' });
  assert.strictEqual(extraireUtilisateur({ user: { uniqueId: 'alice' } }).pseudo, 'alice');
  assert.strictEqual(extraireUtilisateur({ uniqueId: 'carl' }).nom, 'carl');       // pas de surnom → le pseudo
  assert.strictEqual(extraireUtilisateur({ user: { displayId: 'x', nickname: 'A‮evil\u0007' } }).nom, 'Aevil');
  assert.strictEqual(extraireUtilisateur({}).pseudo, '');
});

test('chat : sans cadeau → pas lu ; 1 pièce offerte = 1 message lu', () => {
  const b = banc();
  b.ev.onChat(chat('Bob', 'gratuit'));
  assert.strictEqual(b.dits.length, 0);
  b.ev.onGift(don('bob', 'Rose', 1, { repeatCount: 2 }));
  b.dits.length = 0;
  for (const m of ['un', 'deux', 'trois']) b.ev.onChat(chat('Bob', m));
  assert.deepStrictEqual(b.dits, [['Bob', 'un'], ['Bob', 'deux']]);
});

test('chat : même msgId deux fois (rejeu TikTok) → une seule lecture', () => {
  const b = banc();
  b.ev.onGift(don('Bob', 'Rose', 5)); b.dits.length = 0;
  b.ev.onChat(chat('Bob', 'coucou', { common: { msgId: '123' } }));
  b.ev.onChat(chat('Bob', 'coucou', { common: { msgId: '123' } }));
  assert.strictEqual(b.dits.length, 1);
});

test('test : voiture imposée du catalogue, jamais en live ni hors catalogue', () => {
  const b = banc();
  b.ev.onGift(don('testeur', 'Rose', 1), { test: true, voiture: 'scooter' });
  b.ev.onGift(don('testeur', 'Rose', 1), { test: true, voiture: 'fbi2' });
  b.ev.onGift(don('bob', 'Rose', 1), { voiture: 'scooter' });
  assert.deepStrictEqual(b.jeu.map(j => j[1].voiture), ['scooter', undefined, undefined]);
});

test('chat : message vide, sans pseudo ou pavé de plus de 300 caractères → ignoré', () => {
  const b = banc();
  b.ev.onChat(chat('Bob', '   '));
  b.ev.onChat(chat('', 'orphelin'));
  b.ev.onChat(chat('Bob', 'x'.repeat(301)));
  assert.strictEqual(b.dits.length, 0);
});

test('don : alerte à l\'écran + merci vocal + SPAWN_POLICE', () => {
  const b = banc();
  b.ev.onGift(don('bob', 'Rose', 1, { repeatCount: 5 }));
  assert.deepStrictEqual(b.ecran, [{ type: 'alerte_don', pseudo: 'Le bob', identifiant: 'bob', cadeau: 'Rose', nombre: 5, pieces: 5 }]);
  assert.deepStrictEqual(b.jeu, [['SPAWN_POLICE', { donateur: 'bob', cadeau: 'Rose', points: 5 }]]);
  assert.deepStrictEqual(b.dits, [['', 'Merci bob pour Rose fois 5 !']]);
});

test('don en série : rien tant que la série n\'est pas finie, une seule voiture à la fin', () => {
  const b = banc();
  b.ev.onGift(don('bob', 'Rose', 1, { repeatCount: 1, repeatEnd: false }));
  b.ev.onGift(don('bob', 'Rose', 1, { repeatCount: 2, repeatEnd: false }));
  assert.strictEqual(b.jeu.length, 0);
  assert.strictEqual(b.ecran.length, 0);
  b.ev.onGift(don('bob', 'Rose', 1, { repeatCount: 3, repeatEnd: true }));
  assert.strictEqual(b.jeu.length, 1);
});

test('don : pont vers le jeu en panne → l\'alerte et le merci passent quand même', () => {
  const b = banc();
  const erreurs = [];
  const ev = creerEvenements({
    tts: { ajouter: (p, t) => b.dits.push([p, t]) },
    envoyerCommande: () => { throw new TypeError('pont cassé'); },
    broadcast: m => b.ecran.push(m),
    log: { ...muet, error: s => erreurs.push(s) },
  });
  assert.doesNotThrow(() => ev.onGift(don('bob', 'Rose', 1)));
  assert.strictEqual(b.ecran.length, 1);
  assert.strictEqual(b.dits.length, 1);
  assert.match(erreurs[0], /pont cassé/);
});

// ── Tester sans live : { test: true } = même chemin ────────

test('test : message de chat lu par la voix, journal 🧪', () => {
  const b = banc();
  b.ev.onGift(don('testeur', 'Rose', 1), { test: true }); b.dits.length = 0;
  b.ev.onChat(chat('testeur', 'salut la police'), { test: true });
  assert.deepStrictEqual(b.dits, [['testeur', 'salut la police']]);
  assert.ok(b.lignes.some(l => l.startsWith('🧪') && l.includes('salut la police')));
});

test('test : cadeau → alerte + merci + SPAWN_POLICE', () => {
  const b = banc();
  b.ev.onGift(don('testeur', 'Galaxy', 1000, { repeatCount: 2 }), { test: true });
  assert.deepStrictEqual(b.ecran, [{ type: 'alerte_don', pseudo: 'Le testeur', identifiant: 'testeur', cadeau: 'Galaxy', nombre: 2, pieces: 2000 }]);
  assert.deepStrictEqual(b.jeu, [['SPAWN_POLICE', { donateur: 'testeur', cadeau: 'Galaxy' }]]);
  assert.strictEqual(b.dits.length, 1);
});

test('like : tous les 250 likes du live (tous viewers) → SPAWN_POLICE', () => {
  const b = banc();
  b.ev.onLike({ user: { displayId: 'Bob' }, count: 200 });
  assert.strictEqual(b.jeu.length, 0);
  b.ev.onLike({ user: { displayId: 'Alice' }, count: 60 });   // 260 → 1re voiture
  assert.deepStrictEqual(b.jeu, [['SPAWN_POLICE', { donateur: 'Alice', cadeau: '250 likes' }]]);
  b.ev.onLike({ user: { displayId: 'Bob' }, count: 500 });    // 760 → 500 et 750 franchis
  assert.strictEqual(b.jeu.length, 3);
  assert.deepStrictEqual(b.ecran.map(e => [e.type, e.identifiant, e.cadeau]), [
    ['alerte_like', 'Alice', '250 likes'], ['alerte_like', 'Bob', '250 likes'], ['alerte_like', 'Bob', '250 likes'],
  ]);
  assert.strictEqual(b.dits.length, 3);                       // annonce vocale à chaque voiture
});

test('les trois handlers sont des fonctions nommées', () => {
  const b = banc();
  for (const n of ['onChat', 'onGift', 'onLike']) {
    assert.strictEqual(typeof b.ev[n], 'function');
    assert.strictEqual(b.ev[n].name, n);
  }
});

test('chat : commandes inconnues ignorées sans réponse vocale ni action en jeu', () => {
  const b = banc();
  for (const message of ['!inconnue', '!police', '!!!']) b.ev.onChat(chat('Bob', message));
  assert.deepStrictEqual(b.dits, []);
  assert.deepStrictEqual(b.jeu, []);
});

test('garage : identité issue du compte, casse tolérée, anti-rejeu et anti-spam par viewer', () => {
  const b = banc();
  b.ev.onChat(chat('Alice', '!OK LVLUP', { common: { msgId: 'achat-1' } }));
  b.ev.onChat(chat('Alice', '!OK LVLUP', { common: { msgId: 'achat-1' } }));
  b.ev.onChat(chat('alice', '!ok lvlup'));
  b.ev.onChat(chat('Bob', '!ok garage'));
  b.ev.onChat(chat('Charlie', '!ok lvlup Alice'));
  assert.deepStrictEqual(b.jeu, [
    ['GARAGE', { donateur: 'Alice', action: 'lvlup', valeur: '' }],
    ['GARAGE', { donateur: 'Bob', action: 'garage', valeur: '' }],
  ]);
  assert.deepStrictEqual(b.dits, []);
});

test('préfixe !ok : barrage, aide seule, anciennes commandes ignorées, pas lu par la voix', () => {
  const b = banc();
  b.ev.onChat(chat('Alice', '!ok Barrage'));
  b.ev.onChat(chat('Bob', '!ok'));
  b.ev.onChat(chat('Charlie', '!garage'));
  b.ev.onChat(chat('Dan', '!sort barrage'));
  b.ev.onChat(chat('Eve', '!okgarage'));
  b.ev.onChat(chat('Fred', '!ok BOOST'));
  b.ev.onChat(chat('Gus', '!ok boost moteur'));
  b.ev.onChat(chat('Hugo', '!ok acheter Boost'));
  b.ev.onChat(chat('Ivy', '!ok acheter moteur'));
  assert.deepStrictEqual(b.jeu, [
    ['GARAGE', { donateur: 'Alice', action: 'sort', valeur: 'barrage' }],
    ['GARAGE', { donateur: 'Bob', action: 'aide', valeur: '' }],
    ['GARAGE', { donateur: 'Fred', action: 'sort', valeur: 'boost' }],
    ['GARAGE', { donateur: 'Hugo', action: 'acheter', valeur: 'boost' }],
    ['GARAGE', { donateur: 'Ivy', action: 'acheter', valeur: '' }],
  ]);
  assert.deepStrictEqual(b.dits, []);
});

test('likes : trames vides, négatives ou sans pseudo ne déclenchent rien', () => {
  const b = banc();
  for (const data of [null, {}, { count: 250 }, { user: { displayId: 'Bob' }, count: -250 }]) b.ev.onLike(data);
  assert.deepStrictEqual(b.jeu, []);
  b.ev.onLike({ user: { displayId: 'Bob' }, count: 249 });
  assert.strictEqual(b.jeu.length, 0);
  b.ev.onLike({ user: { displayId: 'Bob' }, count: 1 });
  assert.strictEqual(b.jeu.length, 1);
});

test('crédits voix : gardés sur disque après un redémarrage du serveur', () => {
  const fs = require('node:fs'), os = require('node:os'), path = require('node:path');
  const fichier = path.join(fs.mkdtempSync(path.join(os.tmpdir(), 'gtamax-credits-')), 'credits-voix.json');
  const dits = [];
  const creer = () => creerEvenements({ tts: { ajouter: (p, t) => dits.push(t) }, envoyerCommande() {}, broadcast() {}, log: muet, fichierCredits: fichier });
  creer().onGift(don('bob', 'Rose', 1, { repeatCount: 2 }));
  dits.length = 0;
  const apres = creer();
  apres.onChat(chat('Bob', 'un')); apres.onChat(chat('Bob', 'deux')); apres.onChat(chat('Bob', 'trois'));
  assert.deepStrictEqual(dits, ['un', 'deux']);
  assert.deepStrictEqual(JSON.parse(fs.readFileSync(fichier, 'utf8')), {});
});
