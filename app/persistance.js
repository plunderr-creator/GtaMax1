/**
 * persistance.js — data.json (le roster), repris de Final Max v0.3.0.
 *
 * Même squelette que Final Max (server.js « PERSISTANCE » + lib.js), sorti du
 * serveur pour être testable :
 *   - lecture robuste : data.json, puis la copie de sûreté, sinon on démarre
 *     vide EN CONSERVANT le fichier corrompu pour analyse ;
 *   - écriture ATOMIQUE (fichier temporaire complet puis rename) ;
 *   - sauvegarde débouncée (500 ms) ;
 *   - copie de sûreté PÉRIODIQUE (60 s), plus à chaque écriture.
 * Les champs inconnus de data.json sont gardés tels quels : l'état d'un futur
 * gameplay vivra à côté de `viewers` sans que ce fichier ait à le connaître.
 */
const fs   = require('fs');
const path = require('path');

const DATA_DEFAULT = () => ({ viewers: {} });

// Faut-il refaire la copie de sûreté maintenant ? (lib.js de Final Max, verbatim)
// Elle était refaite à CHAQUE sauvegarde (relecture + JSON.parse + copie du
// fichier entier, en boucle pendant un live). Son rôle est de garder « la
// dernière version valide » sous la main si data.json se corrompt : une copie
// d'il y a une minute remplit ce rôle, une copie à chaque écriture non.
// Une dernière copie « dans le futur » (heure système reculée, VM suspendue)
// rend true : sinon l'écart resterait négatif et la copie ne repartirait jamais.
function doitCopierLaSauvegarde(now, derniereCopie, intervalle) {
  const t = Number(derniereCopie) || 0;
  if (t <= 0) return true;
  const ms = Number(intervalle);
  if (!(ms > 0)) return true;      // intervalle nul/absent → copie à chaque écriture
  if (t > now) return true;        // horloge qui recule
  return now - t >= ms;
}

function creerPersistance({ dossier, delaiMs = 500, copieMs = 60000, log = console }) {
  const DATA_PATH = path.join(dossier, 'data.json');
  const BAK_PATH  = path.join(dossier, 'data.backup.json');  // dernière version valide
  const TMP_PATH  = path.join(dossier, 'data.tmp.json');     // écriture atomique

  // Lecture robuste : data.json, puis la copie de sûreté. Ne perd jamais tout en silence.
  function charger() {
    for (const p of [DATA_PATH, BAK_PATH]) {
      try {
        if (!fs.existsSync(p)) continue;
        const raw = fs.readFileSync(p, 'utf8').replace(/^﻿/, '');   // strip BOM (édition Notepad)
        if (!raw.trim()) continue;                                        // fichier vide → on ignore
        const parsed = JSON.parse(raw);
        if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) continue;
        if (p === BAK_PATH) log.warn('⚠️  data.json illisible → récupéré depuis la sauvegarde de secours (data.backup.json)');
        return parsed;
      } catch (e) {
        log.error('❌ Lecture impossible (' + path.basename(p) + ') : ' + e.message);
      }
    }
    // Aucune source valide : on PRÉSERVE un éventuel fichier corrompu, on n'écrase rien en silence.
    try {
      if (fs.existsSync(DATA_PATH)) {
        const dump = DATA_PATH + '.corrompu-' + Date.now();
        fs.copyFileSync(DATA_PATH, dump);
        log.warn('⚠️  Aucune sauvegarde valide. Fichier corrompu conservé : ' + path.basename(dump));
      }
    } catch {}
    return DATA_DEFAULT();
  }

  const donnees = charger();
  // `viewers` garanti objet : data.json d'une autre version, ou édité à la main.
  if (!donnees.viewers || typeof donnees.viewers !== 'object' || Array.isArray(donnees.viewers)) donnees.viewers = {};

  let timer = null, derniereCopie = 0;

  // Écriture ATOMIQUE : fichier temporaire complet, puis rename → data.json n'est
  // JAMAIS à moitié écrit, même si le PC s'éteint pendant la sauvegarde. La copie
  // de sûreté est celle du fichier qu'on VIENT d'écrire : valide par construction.
  function sauverMaintenant() {
    clearTimeout(timer); timer = null;
    try {
      fs.writeFileSync(TMP_PATH, JSON.stringify(donnees, null, 2), 'utf8');
      fs.renameSync(TMP_PATH, DATA_PATH);
      const now = Date.now();
      if (doitCopierLaSauvegarde(now, derniereCopie, copieMs)) {
        try { fs.copyFileSync(DATA_PATH, BAK_PATH); derniereCopie = now; } catch {}
      }
    } catch (e) {
      log.error('❌ Erreur sauvegarde data.json : ' + e.message);
    }
  }

  function sauver() {
    clearTimeout(timer);
    timer = setTimeout(sauverMaintenant, delaiMs);   // debounce : une rafale de likes = une écriture
  }

  return { donnees, sauver, sauverMaintenant };
}

module.exports = { creerPersistance, doitCopierLaSauvegarde };
