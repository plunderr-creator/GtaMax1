const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { lireProfils, creerSuivi, niveau } = require('../progression');
const b64 = s => Buffer.from(s).toString('base64');
test('anciens points repris, achats distincts du niveau, classement par total', () => {
  const p = lireProfils(`${b64('Alice')}:30:11:2:0:0:police2\n${b64('Bob')}:15\n`);
  assert.equal(p[0].niveau, 4); assert.equal(p[0].disponibles, 19); assert.equal(p[0].modele, 'municipale');
  assert.equal(p[1].niveau, 3); assert.equal(p[1].disponibles, 15);
  assert.deepEqual([0,4,5,14,15,29,30,59,60,99,100,500].map(niveau), [1,1,2,2,3,3,4,4,5,5,6,6]);
  const s = lireProfils(`${b64('Cleo')}:10:8:0:0:0:origine:2:1\n`)[0];
  assert.equal(s.boosts, 2); assert.equal(s.barrages, 1); assert.equal(p[0].boosts, 0);
  assert.throws(() => lireProfils(`${b64('a')}:2:0:0:0:0:origine:10:0`));
  for (const contenu of ['incorrect', `${b64('a')}:2:9:0:0:0:origine`, `${b64('a')}:2:0:4:0:0:origine`])
    assert.throws(() => lireProfils(contenu));
  assert.equal(lireProfils(`${b64('a')}:30:0:0:0:0:rs3`)[0].modele, 'rs3');
  assert.equal(lireProfils(`${b64('a')}:2:0:0:0:0:scooter`)[0].modele, 'municipale');
});
test('suivi : reprise silencieuse, +1 et niveau, achat sans gain, panne sans remise à zéro', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'gtamax-progression-'));
  const fichier = path.join(dir, 'GTAMax.points');
  const recus = [];
  try {
    fs.writeFileSync(fichier, `${b64('Alice')}:4\n`);
    const suivi = creerSuivi({ fichier: () => fichier, broadcast: m => recus.push(m), log: { warn() {} } });
    suivi.actualiser(); assert.equal(recus.length, 1);
    suivi.actualiser(); assert.equal(recus.length, 1);
    fs.writeFileSync(fichier, `${b64('Alice')}:5\n`); suivi.actualiser();
    assert.equal(recus.at(-1).gain, 1); assert.equal(recus.at(-1).nouveauNiveau, true);
    assert.equal(recus.at(-1).modele, 'police');
    fs.writeFileSync(fichier, `${b64('Alice')}:5:3:1:0:0:origine\n`); suivi.actualiser();
    assert.equal(recus.at(-1).type, 'progression'); assert.equal(suivi.etat().joueurs[0].disponibles, 2);
    fs.writeFileSync(fichier, 'corrompu'); suivi.actualiser();
    assert.equal(suivi.etat().disponible, false); assert.equal(suivi.etat().joueurs[0].total, 5);
    fs.writeFileSync(fichier, `${b64('Alice')}:5:3:1:0:0:origine\n`); suivi.actualiser();
    assert.equal(suivi.etat().disponible, true);
    assert.equal(recus.filter(m => m.type === 'progression_gain').length, 1);
    const recharge = creerSuivi({ fichier: () => fichier, broadcast: m => recus.push(m) });
    recharge.actualiser(); assert.equal(recus.at(-1).type, 'progression');
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
});
test('retours du garage : anciens ignorés, nouveaux diffusés une seule fois', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'gtamax-retours-'));
  const fichier = path.join(dir, 'GTAMax.points'); const recus = []; const now = 100000;
  try {
    fs.writeFileSync(fichier, '');
    fs.writeFileSync(fichier + '.messages', `${now}-1:${b64('ancien')}`);
    const suivi = creerSuivi({ fichier: () => fichier, broadcast: m => recus.push(m), maintenant: () => now });
    suivi.actualiser();
    fs.appendFileSync(fichier + '.messages', `\n${now}-2:${b64('achat valide')}`);
    suivi.actualiser(); suivi.actualiser();
    assert.deepEqual(recus.filter(m => m.type === 'garage_retour'), [{ type: 'garage_retour', texte: 'achat valide' }]);
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
});
test('sorts en cours : diffusés dans l\'état, retirés à la fin', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'gtamax-sorts-'));
  const fichier = path.join(dir, 'GTAMax.points'); const recus = []; let now = 100000;
  try {
    fs.writeFileSync(fichier, `${b64('Alice')}:10:3:0:0:0:origine:0:0\n`);
    fs.writeFileSync(fichier + '.sorts', `${now}-1:${b64('Alice')}:boost:15000\n${now}-2:${b64('Bob')}:inconnu:15000\n${now - 60000}-3:${b64('Bob')}:barrage:20000`);
    const suivi = creerSuivi({ fichier: () => fichier, broadcast: m => recus.push(m), maintenant: () => now });
    suivi.actualiser();
    assert.deepEqual(suivi.etat().sorts, [{ pseudo: 'Alice', sort: 'boost', fin: 115000 }]);
    const avant = recus.length; suivi.actualiser(); assert.equal(recus.length, avant);
    now = 115000; suivi.actualiser();
    assert.deepEqual(suivi.etat().sorts, []); assert.deepEqual(recus.at(-1).sorts, []);
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
});
