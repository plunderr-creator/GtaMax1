/**
 * outils/lire-config.js — lecture de config.json HORS du serveur (lanceur .bat,
 * installateur du plugin). Même robustesse que le serveur : BOM Notepad retiré,
 * config.json illisible → modèle config.default.json → valeurs minimales.
 */
const fs   = require('fs');
const path = require('path');

const APP = path.join(__dirname, '..');

function lireJson(p) {
  try { return JSON.parse(fs.readFileSync(p, 'utf8').replace(/^﻿/, '')); }
  catch { return null; }
}

function lireConfig() {
  const c = lireJson(path.join(APP, 'config.json')) || lireJson(path.join(APP, 'config.default.json')) || {};
  c.server = c.server || {};
  c.gta    = c.gta    || {};
  return c;
}

// Même priorité que le serveur : variable d'environnement PORT > PORT de .env
// (dotenv n'écrase jamais une variable déjà posée) > config.json > 3200.
function lirePort() {
  const env = parseInt(process.env.PORT, 10);
  if (env > 0 && env < 65536) return env;
  try {
    const m = fs.readFileSync(path.join(APP, '.env'), 'utf8').match(/^\s*PORT\s*=\s*(\d+)\s*$/m);
    if (m) return parseInt(m[1], 10);
  } catch {}
  const p = parseInt(lireConfig().server.port, 10);
  return p > 0 && p < 65536 ? p : 3200;
}

module.exports = { lireConfig, lirePort };
