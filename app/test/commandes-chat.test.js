/* Tests du PARSEUR et du ROUTEUR de commandes chat (commandes-chat.js).
   Runner : node --test. Lancer depuis app/ : npm test

   C'est la couture du gameplay futur : tout message qui commence par « ! »
   devient { commande, args, pseudo } et part vers le registre COMMANDES.
   En v1 le registre ne contient que !aide. Ces tests fixent le contrat que
   les futures commandes trouveront : nom en minuscules, arguments intacts,
   rien de ce qui ressemble à une commande n'atteint la voix. */
const test = require('node:test');
const assert = require('node:assert');
const { parserCommande, creerRouteur, COMMANDES } = require('../commandes-chat.js');

const muet = { log() {}, warn() {}, error() {} };

// ── Parseur ───────────────────────────────────────────────────────────────

test('parseur : !Commande arg1 arg2 → commande en minuscules, arguments intacts', () => {
  assert.deepStrictEqual(parserCommande('  !AIDE  Moi  Vite ', 'Bob'),
    { commande: 'aide', args: ['Moi', 'Vite'], pseudo: 'Bob' });
});

test('parseur : commande sans argument', () => {
  assert.deepStrictEqual(parserCommande('!aide', 'bob'), { commande: 'aide', args: [], pseudo: 'bob' });
});

test('parseur : ponctuation collée au nom tolérée (!aide? !aide!)', () => {
  assert.strictEqual(parserCommande('!aide?', 'bob').commande, 'aide');
  assert.strictEqual(parserCommande('!Aide!', 'bob').commande, 'aide');
});

test('parseur : pas une commande', () => {
  assert.strictEqual(parserCommande('salut !aide', 'bob'), null);
  assert.strictEqual(parserCommande('!', 'bob'), null);
  assert.strictEqual(parserCommande('! aide', 'bob'), null);
  assert.strictEqual(parserCommande('!!!', 'bob'), null);
  assert.strictEqual(parserCommande('', 'bob'), null);
  assert.strictEqual(parserCommande(undefined, 'bob'), null);
});

// ── Registre ──────────────────────────────────────────────────────────────

test('registre v1 : !aide seul', () => {
  assert.deepStrictEqual(Object.keys(COMMANDES), ['aide']);
  assert.strictEqual(typeof COMMANDES.aide.executer, 'function');
});

// ── Routeur ───────────────────────────────────────────────────────────────

test('routage : !aide répond par la voix, une fois toutes les 30 s au plus', () => {
  const dits = [];
  let t = 0;
  const router = creerRouteur({ maintenant: () => t, log: muet });
  const ctx = { repondre: s => dits.push(s) };
  assert.strictEqual(router(parserCommande('!aide', 'a'), ctx), true);
  t = 10000; router(parserCommande('!aide', 'b'), ctx);
  t = 31000; router(parserCommande('!aide', 'c'), ctx);
  assert.strictEqual(dits.length, 2);
  assert.match(dits[0], /aucune commande/i);
});

test('routage : commande inconnue → false, journalisée', () => {
  const lignes = [];
  const router = creerRouteur({ log: { ...muet, log: s => lignes.push(s) } });
  assert.strictEqual(router(parserCommande('!vol 3', 'a'), {}), false);
  assert.match(lignes[0], /inconnue/);
});

test('routage : les noms hérités d\'Object ne sont pas des commandes', () => {
  const router = creerRouteur({ log: muet });
  for (const nom of ['constructor', 'tostring', 'hasownproperty', '__proto__']) {
    assert.strictEqual(router({ commande: nom, args: [], pseudo: 'x' }, {}), false, nom);
  }
});

test('routage : un handler qui lève est attrapé (le live continue)', () => {
  const erreurs = [];
  const router = creerRouteur({
    commandes: { boum: { executer() { throw new Error('bug'); } } },
    log: { ...muet, error: s => erreurs.push(s) },
  });
  assert.doesNotThrow(() => router({ commande: 'boum', args: [], pseudo: 'x' }, {}));
  assert.match(erreurs[0], /bug/);
});

test('routage : un registre sur mesure reçoit la commande et le contexte', () => {
  const recu = [];
  const router = creerRouteur({ commandes: { police: { executer: (cmd, ctx) => recu.push([cmd, ctx.marque]) } }, log: muet });
  router(parserCommande('!police sheriff', 'Bob'), { marque: 42 });
  assert.deepStrictEqual(recu, [[{ commande: 'police', args: ['sheriff'], pseudo: 'Bob' }, 42]]);
});

test('routage : rien à router (null) → false, sans bruit', () => {
  const router = creerRouteur({ log: muet });
  assert.strictEqual(router(null, {}), false);
});
