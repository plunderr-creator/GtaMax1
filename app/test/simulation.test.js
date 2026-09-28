/* Tests du LIVE SIMULÉ et des trames de test (simulation.js).
   Runner : node --test. Lancer depuis app/ : npm test

   Le live simulé fabrique des messages et des cadeaux de faux viewers pour
   tester sans être en live. Horloge factice : les minuteries sont rangées
   dans une liste et on « avance le temps » à la main — aucun test n'attend
   pour de vrai. */
const test = require('node:test');
const assert = require('node:assert');
const { creerSimulation, trameChat, trameDon, PIECES_CADEAUX } = require('../simulation.js');
const { creerEvenements, extraireUtilisateur } = require('../evenements.js');

const muet = { log() {}, warn() {}, error() {} };

// Horloge factice : planifier/annuler/maintenant + avancer(ms) qui déclenche
// les minuteries échues dans l'ordre.
function horloge() {
  let t = 0, seq = 0;
  const taches = new Map();
  return {
    maintenant: () => t,
    planifier: (fn, ms) => { const id = ++seq; taches.set(id, { fn, a: t + ms }); return id; },
    annuler: (id) => { taches.delete(id); },
    avancer(ms) {
      const fin = t + ms;
      for (;;) {
        let suivante = null;
        for (const [id, x] of taches) if (x.a <= fin && (!suivante || x.a < suivante[1].a)) suivante = [id, x];
        if (!suivante) break;
        taches.delete(suivante[0]);
        t = suivante[1].a;
        suivante[1].fn();
      }
      t = fin;
    },
    enAttente: () => taches.size,
  };
}

function espions() {
  const recu = [];
  return {
    recu,
    evenements: {
      onChat: (data, opts) => recu.push(['chat', data, opts]),
      onGift: (data, opts) => recu.push(['gift', data, opts]),
    },
  };
}

// ── Trames ────────────────────────────────────────────────────────────────

test('trames : même forme que tiktok-live-connector, lues par les vrais extracteurs', () => {
  const c = trameChat('lucas_13', 'salut', 'Lucas 🔥');
  assert.deepStrictEqual(extraireUtilisateur(c), { pseudo: 'lucas_13', nom: 'Lucas 🔥' });
  assert.strictEqual(c.content, 'salut');
  const d = trameDon('lucas_13', 'Galaxy', 3);
  assert.deepStrictEqual({ nom: d.gift.name, diamants: d.gift.diamondCount, n: d.repeatCount, fin: d.repeatEnd },
    { nom: 'Galaxy', diamants: PIECES_CADEAUX.Galaxy, n: 3, fin: true });
  assert.strictEqual(trameDon('x', 'Cadeau inconnu', 1).gift.diamondCount, 1);
});

// ── Live simulé ───────────────────────────────────────────────────────────

test('démarrage : un message dans les 2 s, un cadeau dans les 10 s, tout en { test: true }', () => {
  const h = horloge(), e = espions();
  const sim = creerSimulation({ evenements: e.evenements, ...h, hasard: () => 0.5, log: muet });
  assert.strictEqual(sim.demarrer(), true);
  h.avancer(2000);
  assert.ok(e.recu.some(r => r[0] === 'chat'), 'aucun message après 2 s');
  h.avancer(8000);
  assert.ok(e.recu.some(r => r[0] === 'gift'), 'aucun cadeau après 10 s');
  assert.ok(e.recu.every(r => r[2] && r[2].test === true), 'une trame sans { test: true }');
});

test('cadence : chat toutes les 5-10 s, cadeau toutes les 10-20 s', () => {
  const h = horloge(), e = espions();
  const sim = creerSimulation({ evenements: e.evenements, ...h, hasard: () => 0.5, log: muet });
  sim.demarrer();
  h.avancer(120000);
  const chats = e.recu.filter(r => r[0] === 'chat').length;
  const dons = e.recu.filter(r => r[0] === 'gift').length;
  assert.ok(chats >= 12 && chats <= 24, chats + ' messages en 2 min');
  assert.ok(dons >= 6 && dons <= 12, dons + ' cadeaux en 2 min');
});

test('arrêt : plus rien ne part, aucune minuterie ne reste', () => {
  const h = horloge(), e = espions();
  const sim = creerSimulation({ evenements: e.evenements, ...h, hasard: () => 0.3, log: muet });
  sim.demarrer();
  h.avancer(15000);
  assert.strictEqual(sim.arreter('test'), true);
  const avant = e.recu.length;
  h.avancer(120000);
  assert.strictEqual(e.recu.length, avant);
  assert.strictEqual(h.enAttente(), 0);
  assert.deepStrictEqual(sim.etat(), { actif: false, resteMs: 0 });
  assert.strictEqual(sim.arreter('encore'), false);
});

test('durée max : arrêt automatique, journalisé', () => {
  const h = horloge(), e = espions(), lignes = [];
  const sim = creerSimulation({ evenements: e.evenements, ...h, hasard: () => 0.5, dureeMaxMs: 30000,
    log: { ...muet, log: s => lignes.push(s) } });
  sim.demarrer();
  assert.strictEqual(sim.etat().resteMs, 30000);
  h.avancer(60000);
  assert.strictEqual(sim.etat().actif, false);
  assert.ok(lignes.some(l => /arrêté/.test(l) && /durée/.test(l)), lignes.join(' | '));
  assert.strictEqual(h.enAttente(), 0);
});

test('double démarrage refusé (jamais deux boucles en parallèle)', () => {
  const h = horloge(), e = espions();
  const sim = creerSimulation({ evenements: e.evenements, ...h, hasard: () => 0.5, log: muet });
  assert.strictEqual(sim.demarrer(), true);
  assert.strictEqual(sim.demarrer(), false);
  h.avancer(60000);
  const chats = e.recu.filter(r => r[0] === 'chat').length;
  assert.ok(chats <= 12, chats + ' messages en 1 min : deux boucles ?');
});

test('avec les vrais handlers : voix, alerte, SPAWN_POLICE', () => {
  const h = horloge();
  const b = { dits: [], jeu: [], ecran: [] };
  const ev = creerEvenements({
    tts: { ajouter: (p, t) => b.dits.push([p, t]) },
    envoyerCommande: (v, a) => b.jeu.push([v, a]),
    broadcast: m => b.ecran.push(m),
    log: muet,
  });
  const sim = creerSimulation({ evenements: ev, ...h, hasard: () => 0.5, log: muet });
  sim.demarrer();
  h.avancer(60000);
  sim.arreter();
  assert.ok(b.dits.length > 0, 'rien lu');
  assert.ok(b.ecran.some(m => m.type === 'alerte_don'), 'aucune alerte');
  assert.ok(b.jeu.some(j => j[0] === 'SPAWN_POLICE'), 'aucune voiture');
});
