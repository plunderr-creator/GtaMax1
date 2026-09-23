/* Tests du PONT vers le jeu (gta-bridge.js) — écriture de la file de commandes.
   Runner : node --test. Lancer depuis app/ : npm test

   LE PROTOCOLE (voir la spec, § 2)
   Un seul fichier, deux propriétaires : Node le CRÉE (tmp complet puis rename,
   et seulement s'il n'existe pas), le plugin le CONSOMME (rename en .en-cours,
   lecture, suppression). Ici on joue le plugin à la main avec des renames. Ce
   que ces tests verrouillent : le plugin ne voit jamais un fichier à moitié
   écrit, un fichier pas encore consommé n'est jamais écrasé (sinon des
   commandes disparaissent), et une commande trop vieille ne part jamais (pas
   de vague de voitures de police une heure après les dons). */
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { creerPont, formaterLigne, NOM_FICHIER } = require('../gta-bridge.js');

const muet = { log() {}, warn() {}, error() {} };
const dossier = () => fs.mkdtempSync(path.join(os.tmpdir(), 'gtamax-pont-'));
const dors = ms => new Promise(r => setTimeout(r, ms));

test('nom du fichier : celui que cherche le plugin', () => {
  assert.strictEqual(NOM_FICHIER, 'gtamax_queue.txt');
});

test('format : VERBE t=… puis clés, valeurs encodées façon URL', () => {
  assert.strictEqual(formaterLigne('SPAWN_POLICE', { donateur: 'bob', cadeau: 'Hand Hearts' }, 42),
    'SPAWN_POLICE t=42 donateur=bob cadeau=Hand%20Hearts');
});

test('format : aucune valeur ne peut casser la ligne (espace, =, retour, émoji, surrogate orphelin)', () => {
  const l = formaterLigne('SPAWN_POLICE', { donateur: 'a b=c\nSPAWN_POLICE x=1', cadeau: '🌹\uD800' }, 1);
  assert.strictEqual(l.split('\n').length, 1);
  assert.strictEqual(l.split(' ').length, 4);
  assert.strictEqual(decodeURIComponent(l.split(' ')[2].split('=')[1]), 'a b=c\nSPAWN_POLICE x=1');
});

test('format : clé sans valeur (undefined/null) omise', () => {
  assert.strictEqual(formaterLigne('PING', { a: undefined, b: null, c: 0 }, 7), 'PING t=7 c=0');
});

test('format : verbe ou clé invalides refusés (erreur de programmation, pas de live)', () => {
  assert.throws(() => formaterLigne('spawn', {}, 1), TypeError);
  assert.throws(() => formaterLigne('', {}, 1), TypeError);
  assert.throws(() => formaterLigne('SPAWN', { 'Mauvaise Clé': 1 }, 1), TypeError);
  assert.throws(() => formaterLigne('SPAWN', { t: 1 }, 1), TypeError);   // t est réservé à l'horodatage
});

test('écriture atomique : le fichier final est complet, aucun .tmp ne traîne', () => {
  const d = dossier();
  const f = path.join(d, NOM_FICHIER);
  const pont = creerPont({ fichier: f, log: muet });
  pont.envoyerCommande('SPAWN_POLICE', { donateur: 'a' });
  assert.match(fs.readFileSync(f, 'utf8'), /^SPAWN_POLICE t=\d+ donateur=a\n$/);
  assert.deepStrictEqual(fs.readdirSync(d), [NOM_FICHIER]);
});

test('fichier pas encore consommé : jamais écrasé, les commandes attendent puis partent ensemble', async () => {
  const d = dossier();
  const f = path.join(d, NOM_FICHIER);
  const pont = creerPont({ fichier: f, retryMs: 20, log: muet });
  pont.envoyerCommande('SPAWN_POLICE', { donateur: 'a' });
  pont.envoyerCommande('SPAWN_POLICE', { donateur: 'b' });
  pont.envoyerCommande('SPAWN_POLICE', { donateur: 'c' });
  assert.deepStrictEqual(fs.readFileSync(f, 'utf8').trim().split('\n').map(l => l.split(' ').pop()), ['donateur=a']);
  assert.strictEqual(pont.diagnostic().enAttente, 2);
  await dors(60);
  assert.strictEqual(pont.diagnostic().enAttente, 2);                    // toujours pas consommé : on attend
  fs.renameSync(f, path.join(d, 'gtamax_queue.en-cours'));               // le plugin prend le fichier
  await dors(120);
  const lignes = fs.readFileSync(f, 'utf8').trim().split('\n');
  assert.deepStrictEqual(lignes.map(l => l.split(' ').pop()), ['donateur=b', 'donateur=c']);
  assert.strictEqual(pont.diagnostic().enAttente, 0);
});

test('péremption : une commande de plus de 60 s n\'est jamais écrite', async () => {
  const d = dossier();
  const f = path.join(d, NOM_FICHIER);
  let t = 0;
  fs.writeFileSync(f, 'BLOQUE t=0\n');                                   // fichier resté d'une autre session
  const pont = creerPont({ fichier: f, retryMs: 20, maintenant: () => t, log: muet });
  pont.envoyerCommande('SPAWN_POLICE', { donateur: 'vieux' });
  t = 61000;
  fs.unlinkSync(f);
  await dors(80);
  assert.strictEqual(fs.existsSync(f), false);
  assert.strictEqual(pont.diagnostic().enAttente, 0);
});

test('file bloquée 10 s : le journal le dit une fois, pas à chaque essai', async () => {
  const d = dossier();
  const f = path.join(d, NOM_FICHIER);
  let t = 0;
  const avert = [];
  fs.writeFileSync(f, 'BLOQUE t=0\n');
  const pont = creerPont({ fichier: f, retryMs: 10, alerteMs: 10000, maintenant: () => t,
    log: { log() {}, error() {}, warn: s => avert.push(s) } });
  pont.envoyerCommande('SPAWN_POLICE', {});
  t = 11000; await dors(40);
  t = 12000; await dors(40);
  assert.strictEqual(avert.filter(s => /pas consommée/.test(s)).length, 1);
});

test('dossier du jeu absent : pas d\'exception, la commande reste en attente', () => {
  const f = path.join(os.tmpdir(), 'nexiste-pas-' + Date.now(), 'scripts', NOM_FICHIER);
  const pont = creerPont({ fichier: f, log: muet });
  assert.doesNotThrow(() => pont.envoyerCommande('SPAWN_POLICE', {}));
  assert.strictEqual(pont.diagnostic().enAttente, 1);
  assert.strictEqual(fs.existsSync(f + '.tmp'), false);
});

test('chemin dynamique : un nouveau dossier de jeu (setup) est pris en compte à chaud', () => {
  const d1 = dossier(), d2 = dossier();
  let courant = d1;
  const pont = creerPont({ fichier: () => path.join(courant, NOM_FICHIER), log: muet });
  pont.envoyerCommande('SPAWN_POLICE', { donateur: 'a' });
  courant = d2;
  pont.envoyerCommande('SPAWN_POLICE', { donateur: 'b' });
  assert.match(fs.readFileSync(path.join(d2, NOM_FICHIER), 'utf8'), /donateur=b/);
});
