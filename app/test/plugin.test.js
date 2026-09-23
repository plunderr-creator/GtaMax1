/* Tests du PLUGIN C# (scripts/GTAMax.cs) sous node --test.
   Runner : node --test. Lancer depuis app/ : npm test

   COMMENT
   Le plugin tourne dans GTA V, pas ici. Mais sa logique pure (parseur de ligne,
   péremption, registre d'actions, file de spawn, parc de 8, règles) est isolée
   hors du bloc `#if !TESTS_SANS_JEU`. On compile donc GTAMax.cs avec
   /define:TESTS_SANS_JEU + un harnais C# (test/csharp/TestsPlugin.cs), on lance
   l'exe, et chaque ligne « OK … / ECHEC … » devient un sous-test.
   Deuxième test : si ScriptHookVDotNet3.dll est dans le dossier du jeu, on
   compile le plugin COMPLET contre lui (dans un dossier temporaire, rien n'est
   installé) — c'est ce qui attrape un nom d'API SHVDN faux.
   Sauté proprement hors Windows / sans .NET Framework (pas de csc.exe). */
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const { trouverCsc, compiler, SOURCE } = require('../outils/plugin.js');
const { lireConfig } = require('../outils/lire-config.js');

const HARNAIS = path.join(__dirname, 'csharp', 'TestsPlugin.cs');
const SANS_CSC = !trouverCsc() && 'csc.exe absent (Windows + .NET Framework 4.x requis)';
const tmp = (nom) => path.join(os.tmpdir(), `gtamax-${nom}-${process.pid}-${Date.now()}`);

test('plugin C# : logique pure (parseur, registre, file de spawn, parc)', { skip: SANS_CSC }, async (t) => {
  const exe = tmp('tests-plugin') + '.exe';
  try {
    const c = compiler({ sources: [SOURCE, HARNAIS], sortie: exe, defines: ['TESTS_SANS_JEU'], cible: 'exe' });
    assert.ok(c.ok, 'compilation du harnais :\n' + c.sortie);
    const r = spawnSync(exe, [], { encoding: 'utf8', windowsHide: true });
    const lignes = String(r.stdout || '').split(/\r?\n/).filter(l => /^(OK|ECHEC) /.test(l));
    assert.ok(lignes.length >= 30, 'trop peu de résultats :\n' + r.stdout + r.stderr);
    for (const l of lignes) {
      await t.test(l.replace(/^(OK|ECHEC) /, '').replace(/ : .*$/, ''), () => { assert.ok(l.startsWith('OK '), l); });
    }
    assert.strictEqual(r.status, 0, r.stdout + r.stderr);
  } finally {
    try { fs.unlinkSync(exe); } catch {}
  }
});

test('empreinte du plugin : insensible aux fins de ligne (git autocrlf)', () => {
  const { empreinte } = require('../outils/plugin.js');
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
    const c = compiler({ sources: [SOURCE], sortie: dll, references: [SHVDN3] });
    assert.ok(c.ok, 'compilation du plugin :\n' + c.sortie);
    // Un avertissement « obsolète » après une mise à jour de SHVDN n'est pas une
    // panne : il est affiché, pas bloquant. À traiter à la prochaine retouche.
    if (c.sortie) t.diagnostic('avertissements du compilateur :\n' + c.sortie);
  } finally {
    try { fs.unlinkSync(dll); } catch {}
  }
});
