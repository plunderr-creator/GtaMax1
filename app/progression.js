'use strict';
const fs = require('node:fs');
const SEUILS = [0, 5, 15, 30, 60, 100];
const MODELES = ['municipale', 'police', 'golf', 'rs3', 'm5', 'scooter'];
const SORTS = ['boost', 'barrage'];
function niveau(total) { return SEUILS.filter(s => total >= s).length; }
function decoder(texte) {
  if (!/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(texte)) throw Error('Texte invalide');
  return Buffer.from(texte, 'base64').toString('utf8');
}
function lireProfils(texte) {
  const profils = new Map();
  for (const ligne of texte.replace(/^\uFEFF/, '').split(/\r?\n/)) {
    if (!ligne.trim()) continue;
    const c = ligne.trim().split(':');
    if (c.length !== 2 && c.length !== 7 && c.length !== 9) throw Error('Sauvegarde de progression invalide');
    const nombres = [...c.slice(1, c.length === 2 ? 2 : 6), ...c.slice(7, 9)].map(x => /^\d+$/.test(x) ? Number(x) : NaN);
    if (nombres.some(x => !Number.isSafeInteger(x) || x < 0 || x > 2147483647)) throw Error('Points invalides');
    const [total, depenses = 0, moteur = 0, freins = 0, transmission = 0, boosts = 0, barrages = 0] = nombres;
    const pseudo = decoder(c[0]), n = niveau(total), lu = MODELES.indexOf(c[6] || ''), modele = lu >= 0 && lu < n ? c[6] : MODELES[0];
    if (!pseudo.trim() || depenses > total || [moteur, freins, transmission].some(x => x > 3) || boosts > 9 || barrages > 9) throw Error('Profil invalide');
    profils.set(pseudo.toLowerCase(), { pseudo, total, disponibles: total - depenses, niveau: n,
      prochain: SEUILS[n] ?? null, seuil: SEUILS[n - 1], moteur, freins, transmission, modele, boosts, barrages });
  }
  return [...profils.values()].sort((a,b) => b.total - a.total || a.pseudo.localeCompare(b.pseudo, 'fr'));
}

// Lecture seule : aucune modification des points depuis le serveur ou OBS.
function creerSuivi({ fichier, broadcast, maintenant = Date.now, log = console }) {
  let precedent = null, contenu = null, cheminAvant = null, erreurAvant = null;
  let etat = { type: 'progression', disponible: false, joueurs: [], totalViewers: 0, sorts: [] };
  let sortsActifs = [], cleSorts = '[]';
  let messagesInitialises = false;
  const messagesVus = new Set();
  function actualiser() {
    const chemin = fichier();
    if (chemin !== cheminAvant) {
      precedent = null; contenu = null; cheminAvant = chemin;
      messagesInitialises = false; messagesVus.clear();
      sortsActifs = []; cleSorts = '[]';
      etat = { type: 'progression', disponible: false, joueurs: [], totalViewers: 0, sorts: [] };
    }
    try {
      const texte = fs.readFileSync(chemin, 'utf8');
      if (texte !== contenu || !etat.disponible) {
        const joueurs = lireProfils(texte);
        const avant = new Map((precedent || []).map(p => [p.pseudo.toLowerCase(), p]));
        etat = { type: 'progression', disponible: true, joueurs: joueurs.slice(0, 5), totalViewers: joueurs.length, sorts: sortsActifs };
        broadcast(etat);
        if (precedent !== null) for (const p of joueurs) {
          const ancien = avant.get(p.pseudo.toLowerCase());
          const gain = p.total - (ancien?.total || 0);
          if (gain > 0) broadcast({ type: 'progression_gain', pseudo: p.pseudo, gain,
            niveau: p.niveau, nouveauNiveau: p.niveau > (ancien?.niveau || 1),
            modele: p.niveau > (ancien?.niveau || 1) ? MODELES[p.niveau - 1] : null });
        }
        precedent = joueurs; contenu = texte;
      }
      erreurAvant = null;
    } catch (e) {
      if (etat.disponible || precedent === null) {
        etat = { ...etat, disponible: false };
        if (erreurAvant !== e.code + e.message) broadcast(etat);
      }
      // Un fichier temporairement absent ne remplace jamais les derniers scores valides.
      if (e.code === 'ENOENT' && precedent === null) precedent = [];
      if (e.code !== 'ENOENT' && erreurAvant !== e.code + e.message) log.warn('Progression non lisible : ' + e.message);
      erreurAvant = e.code + e.message;
    }
    try {
      const lignes = fs.readFileSync(chemin + '.messages', 'utf8').split(/\r?\n/).filter(Boolean);
      for (const ligne of lignes) {
        const i = ligne.indexOf(':'), id = ligne.slice(0, i);
        if (i < 0 || messagesVus.has(id)) continue;
        const date = Number(id.split('-')[0]);
        if (messagesInitialises && Number.isFinite(date) && maintenant() - date >= 0 && maintenant() - date < 15000)
          broadcast({ type: 'garage_retour', texte: decoder(ligne.slice(i + 1)).slice(0, 400) });
        messagesVus.add(id);
      }
      while (messagesVus.size > 200) messagesVus.delete(messagesVus.values().next().value);
    } catch (e) { /* Pas encore de retour du jeu, ou remplacement en cours. */ }
    messagesInitialises = true;
    // Sorts en cours (!ok boost, !ok barrage) : emoji sur la carte du viewer tant qu'ils durent.
    const actifs = new Map();
    try {
      for (const ligne of fs.readFileSync(chemin + '.sorts', 'utf8').split(/\r?\n/)) {
        const c = ligne.trim().split(':');
        if (c.length !== 4 || !SORTS.includes(c[2])) continue;
        const date = Number(c[0].split('-')[0]), duree = Number(c[3]), fin = date + duree;
        if (!Number.isFinite(date) || !(duree > 0 && duree <= 60000) || fin <= maintenant() || date > maintenant() + 5000) continue;
        let pseudo; try { pseudo = decoder(c[1]).slice(0, 60); } catch (e) { continue; }
        const cle = pseudo.toLowerCase() + ':' + c[2];
        if (!actifs.has(cle) || actifs.get(cle).fin < fin) actifs.set(cle, { pseudo, sort: c[2], fin });
      }
    } catch (e) { /* Aucun sort lancé pour l'instant. */ }
    const liste = [...actifs.values()], cle = JSON.stringify(liste);
    if (cle !== cleSorts) { cleSorts = cle; sortsActifs = liste; etat = { ...etat, sorts: liste }; broadcast(etat); }
    return etat;
  }
  return { actualiser, etat: () => etat };
}
module.exports = { lireProfils, niveau, creerSuivi, MODELES };
