/* Tests du ROSTER (roster.js) et de la PERSISTANCE (persistance.js).
   Runner : node --test. Lancer depuis app/ : npm test

   Le roster est la table sur laquelle tout gameplay futur se posera : sa forme
   ({ premiere_vue, derniere_vue, dons_total } par pseudo en minuscules) est un
   contrat. La persistance est celle de Final Max (écriture atomique, copie de
   sûreté périodique, relecture de secours) : elle ne doit jamais perdre le
   roster en silence. */
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const roster = require('../roster.js');
const { creerPersistance, doitCopierLaSauvegarde } = require('../persistance.js');

const muet = { log() {}, warn() {}, error() {} };
const bac = () => fs.mkdtempSync(path.join(os.tmpdir(), 'gtamax-persist-'));

// ── Roster ────────────────────────────────────────────────────────────────

test('roster : premier passage crée la fiche, clé en minuscules', () => {
  const v = {};
  roster.vu(v, 'Bob_42', 1000);
  assert.deepStrictEqual(v, { bob_42: { premiere_vue: 1000, derniere_vue: 1000, dons_total: 0 } });
});

test('roster : repassage ne touche que derniere_vue', () => {
  const v = {};
  roster.vu(v, 'bob', 1000);
  roster.vu(v, 'BOB', 5000);
  assert.deepStrictEqual(v.bob, { premiere_vue: 1000, derniere_vue: 5000, dons_total: 0 });
});

test('roster : dons_total cumule des pièces, jamais négatif', () => {
  const v = {};
  roster.ajouterDon(v, 'bob', 5, 1);
  roster.ajouterDon(v, 'bob', 100, 2);
  roster.ajouterDon(v, 'bob', -50, 3);
  roster.ajouterDon(v, 'bob', 'n\'importe quoi', 4);
  assert.strictEqual(v.bob.dons_total, 105);
  assert.strictEqual(v.bob.derniere_vue, 4);
});

test('roster : pseudo vide ignoré', () => {
  const v = {};
  assert.strictEqual(roster.vu(v, '  ', 1), null);
  assert.strictEqual(roster.ajouterDon(v, '', 10, 1), null);
  assert.deepStrictEqual(v, {});
});

test('roster : une fiche abîmée à la main est réparée, pas écrasée', () => {
  // data.json édité à la main : champ manquant ou texte à la place d'un nombre.
  const v = { bob: { premiere_vue: 1000, dons_total: 'x' } };
  roster.ajouterDon(v, 'bob', 10, 2000);
  assert.deepStrictEqual(v.bob, { premiere_vue: 1000, derniere_vue: 2000, dons_total: 10 });
});

// ── Copie de sûreté : périodique, plus à chaque écriture ──────────────────

test('copie de sûreté : jamais copiée → tout de suite', () => {
  assert.strictEqual(doitCopierLaSauvegarde(1000, 0, 60000), true);
});

test('copie de sûreté : récente → pas de nouvelle copie', () => {
  assert.strictEqual(doitCopierLaSauvegarde(100000, 99000, 60000), false);
});

test('copie de sûreté : intervalle écoulé → on recopie', () => {
  assert.strictEqual(doitCopierLaSauvegarde(160000, 100000, 60000), true);
});

test('copie de sûreté : horloge qui recule → on recopie plutôt que plus jamais', () => {
  assert.strictEqual(doitCopierLaSauvegarde(1000, 999999, 60000), true);
});

// ── Persistance (IO réelle dans un dossier temporaire) ────────────────────

test('persistance : premier lancement → roster vide', () => {
  const p = creerPersistance({ dossier: bac(), log: muet });
  assert.deepStrictEqual(p.donnees, { viewers: {} });
});

test('persistance : écrit puis relit, sans fichier temporaire qui traîne', () => {
  const d = bac();
  const p = creerPersistance({ dossier: d, log: muet });
  roster.ajouterDon(p.donnees.viewers, 'alice', 30, 1);
  p.sauverMaintenant();
  assert.deepStrictEqual(fs.readdirSync(d).sort(), ['data.backup.json', 'data.json']);
  const q = creerPersistance({ dossier: d, log: muet });
  assert.strictEqual(q.donnees.viewers.alice.dons_total, 30);
});

test('persistance : data.json corrompu → repart de la copie de sûreté', () => {
  const d = bac();
  const p = creerPersistance({ dossier: d, log: muet });
  roster.vu(p.donnees.viewers, 'alice', 1);
  p.sauverMaintenant();
  fs.writeFileSync(path.join(d, 'data.json'), '{ cassé');
  const q = creerPersistance({ dossier: d, log: muet });
  assert.ok(q.donnees.viewers.alice);
});

test('persistance : rien de valide → le fichier corrompu est conservé pour analyse', () => {
  const d = bac();
  fs.writeFileSync(path.join(d, 'data.json'), '{ cassé');
  const p = creerPersistance({ dossier: d, log: muet });
  assert.deepStrictEqual(p.donnees, { viewers: {} });
  assert.ok(fs.readdirSync(d).some(f => f.startsWith('data.json.corrompu-')));
});

test('persistance : BOM Notepad et champ viewers absent tolérés', () => {
  const d = bac();
  fs.writeFileSync(path.join(d, 'data.json'), '﻿{"autre":1}');
  const p = creerPersistance({ dossier: d, log: muet });
  assert.deepStrictEqual(p.donnees.viewers, {});
  assert.strictEqual(p.donnees.autre, 1);   // un champ inconnu (futur gameplay) n'est jamais jeté
});

test('persistance : sauver() regroupe une rafale en une écriture', async () => {
  const d = bac();
  const p = creerPersistance({ dossier: d, delaiMs: 30, log: muet });
  roster.vu(p.donnees.viewers, 'alice', 1);
  p.sauver(); p.sauver(); p.sauver();
  assert.strictEqual(fs.existsSync(path.join(d, 'data.json')), false);
  await new Promise(r => setTimeout(r, 80));
  assert.ok(JSON.parse(fs.readFileSync(path.join(d, 'data.json'), 'utf8')).viewers.alice);
});

test('persistance : une activité continue n\'empêche jamais d\'écrire', async () => {
  // Un debounce pur repousserait l'écriture tant que les appels continuent.
  const d = bac();
  const p = creerPersistance({ dossier: d, delaiMs: 40, log: muet });
  roster.vu(p.donnees.viewers, 'alice', 1);
  const fin = Date.now() + 200;
  while (Date.now() < fin) { p.sauver(); await new Promise(r => setTimeout(r, 10)); }
  assert.ok(fs.existsSync(path.join(d, 'data.json')), 'aucune écriture pendant 200 ms d\'activité');
});
