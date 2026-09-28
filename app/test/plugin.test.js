/* Tests du PLUGIN C# (scripts/GTAMax.cs + scripts/Survie.cs) sous node --test.
   Runner : node --test. Lancer depuis app/ : npm test

   COMMENT
   Le plugin tourne dans GTA V, pas ici. Mais sa logique pure (parseur de ligne,
   péremption, consommation de la file et règles de poursuite) est isolée hors du bloc `#if !TESTS_SANS_JEU`. On compile donc
   GTAMax.cs avec /define:TESTS_SANS_JEU + un harnais C# (test/csharp/
   TestsPlugin.cs), on lance l'exe, et chaque ligne « OK … / ECHEC … » devient
   un sous-test.
   Le harnais sait aussi jouer le plugin sur une VRAIE file écrite par le pont
   Node (mode « consommer ») : c'est le test de protocole de bout en bout.
   Dernier test : si ScriptHookVDotNet3.dll est dans le dossier du jeu, on
   compile le plugin COMPLET contre lui (dans un dossier temporaire, rien n'est
   installé) — c'est ce qui attrape un nom d'API SHVDN faux.
   Sauté proprement hors Windows / sans .NET Framework (pas de csc.exe). */
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const { trouverCsc, compiler, empreinte, SOURCES } = require('../outils/plugin.js');
const { lireConfig } = require('../outils/lire-config.js');
const { creerPont } = require('../gta-bridge.js');

const HARNAIS = path.join(__dirname, 'csharp', 'TestsPlugin.cs');
const SANS_CSC = !trouverCsc() && 'csc.exe absent (Windows + .NET Framework 4.x requis)';
const tmp = (nom) => path.join(os.tmpdir(), `gtamax-${nom}-${process.pid}-${Date.now()}`);
const dors = ms => new Promise(r => setTimeout(r, ms));

// Le harnais n'est compilé qu'une fois pour tout le fichier.
let exe = null;
function harnais() {
  if (exe) return exe;
  const sortie = tmp('tests-plugin') + '.exe';
  const c = compiler({ sources: [...SOURCES, HARNAIS], sortie, defines: ['TESTS_SANS_JEU'], cible: 'exe' });
  assert.ok(c.ok, 'compilation du harnais :\n' + c.sortie);
  exe = sortie;
  return exe;
}
test.after(() => { if (exe) { try { fs.unlinkSync(exe); } catch {} } });

test('plugin C# : logique pure (parseur, file et poursuite)', { skip: SANS_CSC }, async (t) => {
  const r = spawnSync(harnais(), [], { encoding: 'utf8', windowsHide: true });
  const lignes = String(r.stdout || '').split(/\r?\n/).filter(l => /^(OK|ECHEC) /.test(l));
  assert.ok(lignes.length >= 35, 'trop peu de résultats :\n' + r.stdout + r.stderr);
  for (const l of lignes) {
    await t.test(l.replace(/^(OK|ECHEC) /, '').replace(/ : .*$/, ''), () => { assert.ok(l.startsWith('OK '), l); });
  }
  assert.strictEqual(r.status, 0, r.stdout + r.stderr);
});

test('protocole Node → C# : chaque commande reçue une fois, dans l\'ordre, valeurs intactes', { skip: SANS_CSC }, async () => {
  const d = fs.mkdtempSync(path.join(os.tmpdir(), 'gtamax-protocole-'));
  const pont = creerPont({ fichier: path.join(d, 'gtamax_queue.txt'), retryMs: 20, log: { log() {}, warn() {}, error() {} } });
  pont.envoyerCommande('SPAWN_POLICE', { donateur: 'bob', cadeau: 'Hand Hearts' });
  pont.envoyerCommande('SPAWN_POLICE', { donateur: 'Zoé 🌹', cadeau: 'Rose' });   // attend : le 1er fichier n'est pas consommé
  pont.envoyerCommande('SPAWN_POLICE', { donateur: 'a=b c', cadeau: '100%' });
  const consommer = () => String(spawnSync(harnais(), ['consommer', d], { encoding: 'utf8', windowsHide: true }).stdout || '')
    .split(/\r?\n/).filter(l => l.startsWith('CMD\t')).map(l => l.split('\t').slice(1));
  const recues = [];
  recues.push(...consommer());     // 1er tick du plugin : le fichier de « bob »
  await dors(150);                 // le pont voit la place libre et écrit les deux autres d'un coup
  recues.push(...consommer());
  await dors(150);
  recues.push(...consommer());     // plus rien : rien n'est rejoué
  assert.deepStrictEqual(recues, [
    ['SPAWN_POLICE', 'donateur=bob', 'cadeau=Hand Hearts'],
    ['SPAWN_POLICE', 'donateur=Zoé 🌹', 'cadeau=Rose'],
    ['SPAWN_POLICE', 'donateur=a=b c', 'cadeau=100%'],
  ]);
  assert.deepStrictEqual(fs.readdirSync(d), []);
});

test('empreinte du plugin : insensible aux fins de ligne (git autocrlf)', () => {
  const lf = tmp('lf') + '.cs', crlf = tmp('crlf') + '.cs';
  try {
    fs.writeFileSync(lf, 'class A {\n  int x;\n}\n');
    fs.writeFileSync(crlf, '﻿class A {\r\n  int x;\r\n}\r\n');
    assert.strictEqual(empreinte(lf), empreinte(crlf));
  } finally {
    for (const f of [lf, crlf]) { try { fs.unlinkSync(f); } catch {} }
  }
});

const SHVDN3 = path.join(String(lireConfig().gta.dossier_jeu || ''), 'ScriptHookVDotNet3.dll');
test('plugin C# : compile contre le vrai ScriptHookVDotNet3.dll', {
  skip: SANS_CSC || (!fs.existsSync(SHVDN3) && 'ScriptHookVDotNet3.dll introuvable dans le dossier du jeu'),
}, (t) => {
  const dll = tmp('plugin') + '.dll';
  try {
    const c = compiler({ sources: SOURCES, sortie: dll, references: [SHVDN3] });
    assert.ok(c.ok, 'compilation du plugin :\n' + c.sortie);
    // Un avertissement « obsolète » après une mise à jour de SHVDN n'est pas une
    // panne : il est affiché, pas bloquant. À traiter à la prochaine retouche.
    if (c.sortie) t.diagnostic('avertissements du compilateur :\n' + c.sortie);
  } finally {
    try { fs.unlinkSync(dll); } catch {}
  }
});
