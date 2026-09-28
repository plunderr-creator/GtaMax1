/* Bandeau TikTok 9:16 — points, classement, alertes et voix. */
'use strict';

/* ══════════════════════════════════════
   TTS — lecture vocale du chat (mp3 générés serveur, joués en série)
   (Final Max, verbatim : le serveur a déjà sauté ce qui était trop vieux, on
    re-saute ici ce qui a vieilli pendant qu'un autre message parlait.)
   ══════════════════════════════════════ */
let _ttsQueue = [], _ttsPlaying = false;
const TTS_MAX_AGE_MS = 8000;
function ttsPlay(data){
  if (!data || !data.id) return;
  if (_ttsQueue.length >= 5) return;
  _ttsQueue.push(data); ttsNext();
}
function ttsNext(){
  if (_ttsPlaying) return;
  let d = _ttsQueue.shift();
  while (d && d.ts && Date.now() - d.ts > TTS_MAX_AGE_MS) d = _ttsQueue.shift();
  if (!d) return;
  _ttsPlaying = true;
  const a = new Audio('/api/tts/' + encodeURIComponent(String(d.id)));
  const v = parseFloat(d.volume); a.volume = Number.isFinite(v) ? Math.max(0, Math.min(1, v)) : 0.8;
  const bulle = bulleVoix(d, a);
  let _done = false;
  const done = () => { if (_done) return; _done = true; clearTimeout(watchdog); bulle.fin(); _ttsPlaying = false; ttsNext(); };
  const watchdog = setTimeout(done, 12000);
  a.onended = done; a.onerror = done;
  a.play().catch(done);
}

// Bulle de la voix : pseudo, texte révélé mot par mot au rythme du son, barres au volume réel.
let _voixCtx = null, _voixCache = null;
function analyseur(a) {
  try {
    _voixCtx = _voixCtx || new AudioContext();
    // Contexte bloqué (navigateur sans clic) : on ne branche pas, sinon le son serait muet.
    if (_voixCtx.state !== 'running') { _voixCtx.resume(); return null; }
    const an = _voixCtx.createAnalyser(); an.fftSize = 64;
    _voixCtx.createMediaElementSource(a).connect(an); an.connect(_voixCtx.destination);
    return an;
  } catch (e) { return null; }
}
function bulleVoix(d, a) {
  const boite = document.getElementById('voix');
  const texte = document.getElementById('voix-texte');
  const barres = boite.querySelectorAll('.voix-barres i');
  clearTimeout(_voixCache);
  document.getElementById('voix-pseudo').textContent = String(d.viewer || 'Chat');
  const mots = String(d.text || '').split(/\s+/).filter(Boolean);
  texte.replaceChildren(...mots.map(m => element('span', 'mot', m + ' ')));
  boite.classList.add('visible');
  const an = analyseur(a), niveaux = an && new Uint8Array(an.frequencyBinCount);
  boite.classList.toggle('sans-analyse', !an);
  let fini = false;
  (function image() {
    if (fini) return;
    const k = a.duration > 0 ? a.currentTime / a.duration : 0;
    const vus = Math.ceil(k * mots.length * 1.08);
    texte.childNodes.forEach((m, i) => m.classList.toggle('dit', i < vus));
    if (an) {
      an.getByteFrequencyData(niveaux);
      barres.forEach((b, i) => { b.style.transform = 'scaleY(' + Math.max(.15, niveaux[i * 3 + 1] / 255) + ')'; });
    }
    requestAnimationFrame(image);
  })();
  return { fin() {
    fini = true;
    texte.childNodes.forEach(m => m.classList.add('dit'));
    barres.forEach(b => { b.style.transform = ''; });
    // Pause courte : si un autre message suit, la bulle reste ouverte et enchaîne.
    _voixCache = setTimeout(() => { boite.classList.remove('visible'); }, 600);
  } };
}

// Une file commune : dons, points et garage se succedent dans le meme ruban.
const annonces = [];
let annonceEnCours = false;
function pagesTexte(texte) {
  const mots = String(texte || '').slice(0, 400).match(/\S{1,65}/gu) || [''];
  const pages = []; let page = '';
  for (const mot of mots) {
    if (page && page.length + mot.length + 1 > 76) { pages.push(page); page = ''; }
    page += (page ? ' ' : '') + mot;
  }
  if (page) pages.push(page);
  return pages.length ? pages : [''];
}
function annonceViewer(titre, texte, type = 'garage') {
  if (annonces.length >= 20) annonces.shift();
  annonces.push({ titre, pages: pagesTexte(texte), type, date: Date.now() });
  prochaineAnnonce();
}
function prochaineAnnonce() {
  if (annonceEnCours) return;
  let annonce = annonces.shift();
  while (annonce && Date.now() - annonce.date > 20000) annonce = annonces.shift();
  if (!annonce) return;
  annonceEnCours = true;
  const ruban = document.getElementById('ruban');
  const el = document.getElementById('progression-annonce');
  ruban.classList.add('occupe'); ruban.dataset.type = annonce.type;
  if (annonce.type === 'don') secouer(document.getElementById('classement'), 'secousse');
  if (annonce.type === 'niveau') secouer(document.getElementById('classement'), 'eclat');
  let index = 0;
  function afficherPage() {
    document.getElementById('progression-annonce-titre').textContent = annonce.titre + (annonce.pages.length > 1 ? ' ' + (index + 1) + '/' + annonce.pages.length : '');
    document.getElementById('progression-annonce-texte').textContent = annonce.pages[index];
    el.classList.add('visible');
    setTimeout(() => {
      el.classList.remove('visible');
      setTimeout(() => {
        if (++index < annonce.pages.length) afficherPage();
        else { ruban.classList.remove('occupe'); annonceEnCours = false; prochaineAnnonce(); }
      }, 220);
    }, annonce.type === 'garage' ? 5000 : 3800);
  }
  afficherPage();
}
function alerteDon(d) {
  if (!d) return;
  const pseudo = String(d.identifiant || d.pseudo || 'Quelqu’un');
  const nombre = Math.max(1, parseInt(d.nombre, 10) || 1);
  const likes = d.type === 'alerte_like';
  annonceViewer(likes ? 'RENFORT !' : 'CADEAU !', pseudo + ' · ' + String(d.cadeau || 'Cadeau')
    + (nombre > 1 ? ' ×' + nombre : '') + ' · La police arrive !', 'don');
}
function gainViewer(d) {
  if (!(d.gain > 0)) return;
  annonceViewer('+' + d.gain + (d.gain === 1 ? ' POINT' : ' POINTS'), String(d.pseudo) + (d.nouveauNiveau
    ? ' passe NIVEAU ' + d.niveau + ' ! Nouveau modèle : ' + d.modele : ' marque au contact !'), d.nouveauNiveau ? 'niveau' : 'gain');
}
function nombreCourt(n) {
  const v = Math.max(0, Number(n) || 0);
  return v >= 1000000 ? (v / 1000000).toFixed(v % 1000000 ? 1 : 0).replace('.', ',') + ' M'
    : v >= 10000 ? (v / 1000).toFixed(v % 1000 ? 1 : 0).replace('.', ',') + ' k'
    : String(v);
}
function element(tag, classe, texte) {
  const el = document.createElement(tag); el.className = classe;
  if (texte !== undefined) el.textContent = String(texte);
  return el;
}
// Animations : scores précédents et positions des cartes, par pseudo.
const anciensTotaux = new Map();
let boostsAvant = new Set();    // viewers déjà en boost au rendu précédent
let barragesAvant = new Set();  // idem pour le barrage
const rangsAvant = new Map();   // rang de voiture (!ok lvlup) au rendu précédent
const lvlupDepuis = new Map();  // pseudo → début de l'animation lvlup (survit aux re-rendus)
const LVLUP_MS = 2600;
function compter(el, de, a) {
  const debut = performance.now();
  (function pas(t) {
    const k = Math.min(1, (t - debut) / 700);
    el.textContent = nombreCourt(Math.round(de + (a - de) * (1 - Math.pow(1 - k, 3))));
    if (k < 1) requestAnimationFrame(pas);
  })(debut);
}
function secouer(el, classe) {
  el.classList.remove(classe); void el.offsetWidth; el.classList.add(classe);
}
function classement(data) {
  const liste = document.getElementById('classement-liste');
  const anciennesPositions = new Map();
  for (const c of liste.children) anciennesPositions.set(c.dataset.pseudo, c.getBoundingClientRect());
  liste.replaceChildren();
  const joueurs = Array.isArray(data.joueurs) ? data.joueurs.slice(0, 5) : [];
  document.getElementById('classement-vide').hidden = joueurs.length > 0;
  document.getElementById('classement-etat').textContent = data.disponible ? 'POINTS SAUVÉS' : 'EN ATTENTE DU JEU';
  for (let i = 0; i < joueurs.length; i++) {
    const p = joueurs[i];
    const carte = element('li', 'carte' + (i === 0 ? ' premier' : '') + (i > 2 ? ' secondaire' : ''));
    carte.title = p.pseudo + ' · ' + p.total + ' points cumulés · ' + p.disponibles + ' disponibles';
    const identite = element('div', 'identite');
    const rang = element('span', 'rang', i === 0 ? '' : '#' + (i + 1));
    if (i === 0) {
      // Icone locale, sans image externe ni HTML provenant du chat.
      const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
      svg.setAttribute('class', 'couronne'); svg.setAttribute('viewBox', '0 0 32 28'); svg.setAttribute('aria-hidden', 'true');
      const pth = document.createElementNS('http://www.w3.org/2000/svg', 'path');
      pth.setAttribute('d','M2 7 9 13 16 2 23 13 30 7 27 23H5ZM6 25H26V28H6Z'); svg.append(pth); rang.append(svg);
      rang.setAttribute('aria-label','Premier');
    }
    identite.append(rang, element('div', 'nom', p.pseudo));
    // Sorts achetés (!ok acheter) : emoji × stock ; sort en cours = emoji qui pulse.
    const enCours = new Set((Array.isArray(data.sorts) ? data.sorts : [])
      .filter(s => String(s.pseudo).toLowerCase() === p.pseudo.toLowerCase()).map(s => s.sort));
    const sorts = element('span', 'sorts');
    for (const [sort, emoji, stock] of [['boost', '🚀', p.boosts], ['barrage', '🚧', p.barrages]]) {
      if (!(stock > 0) && !enCours.has(sort)) continue;
      sorts.append(element('span', 'sort' + (enCours.has(sort) ? ' actif' : ''), emoji + (stock > 1 ? '×' + stock : '')));
    }
    const chiffres = element('div', 'chiffres');
    const score = nombreCourt(p.total);
    chiffres.append(element('span', 'total' + (score.length > 5 ? ' long' : ''), score), element('span','unite','PTS'));
    if (sorts.childNodes.length) chiffres.append(sorts);
    const infos = element('div','infos');
    infos.append(element('span','niveau','NIV. ' + p.niveau));
    const jauge = element('div','jauge'); const barre = element('span','');
    barre.style.width = (p.prochain == null ? 100 : Math.max(0,Math.min(100,100 * (p.total - p.seuil) / Math.max(1,p.prochain - p.seuil)))) + '%';
    jauge.append(barre); carte.append(identite,chiffres,infos,jauge); liste.append(carte);
    carte.dataset.pseudo = p.pseudo;
    // !ok lvlup : le rang de la voiture monte → flash, étincelles, tampon « LVL UP ».
    const cle = p.pseudo.toLowerCase(), rangVoiture = Math.max(p.moteur || 0, p.freins || 0, p.transmission || 0);
    if (rangsAvant.has(cle) && rangVoiture > rangsAvant.get(cle)) lvlupDepuis.set(cle, Date.now());
    rangsAvant.set(cle, rangVoiture);
    const ecoule = Date.now() - (lvlupDepuis.get(cle) || 0);
    if (ecoule < LVLUP_MS) {
      carte.classList.add('lvlup');
      carte.style.setProperty('--lvlup-delai', -ecoule + 'ms');
      const tampon = element('span', 'lvlup-tampon');
      tampon.append(element('b', '', 'LVL UP'), element('span', '', 'VOITURE RANG ' + rangVoiture));
      carte.append(element('span', 'lvlup-balayage'), tampon);
      for (let k = 0; k < 6; k++) {
        const e = element('span', 'lvlup-etincelle', k % 2 ? '⚙️' : '✨');
        e.style.left = (8 + k * 16) + '%'; e.style.setProperty('--d', (k * 90 - ecoule) + 'ms');
        carte.append(e);
      }
    } else lvlupDepuis.delete(cle);
    if (enCours.has('barrage')) {
      // Barrage en cours : gyrophares rouge/bleu, rubalise qui défile, 3 voitures qui tombent au lancement.
      carte.classList.add('barre');
      carte.style.setProperty('--phase-barrage', -(Date.now() % 1000) + 'ms');
      carte.append(element('span', 'gyro'), element('span', 'rubalise'));
      if (!barragesAvant.has(p.pseudo.toLowerCase())) {
        carte.classList.add('barrage-debut');
        for (let k = 0; k < 3; k++) { const v = element('span', 'chute', '🚓'); v.style.left = (18 + k * 28) + '%'; v.style.animationDelay = (k * 120) + 'ms'; carte.append(v); }
      }
    }
    if (enCours.has('boost')) {
      // Boost en cours : couleurs qui défilent, carte qui tremble, traits de vitesse.
      // Phase calée sur l'horloge : un nouveau rendu ne redémarre pas les animations.
      carte.classList.add('booste');
      carte.style.setProperty('--phase', -(Date.now() % 2400) + 'ms');
      carte.append(element('span', 'traits'), element('span', 'cadre-boost'));
      if (!boostsAvant.has(p.pseudo.toLowerCase())) {
        carte.classList.add('boost-debut');
        carte.append(element('span', 'fusee', '🚀'));
      }
    }
    const avant = anciensTotaux.get(p.pseudo);
    const pos = anciennesPositions.get(p.pseudo);
    if (pos) {
      // Glisse depuis l'ancienne place (dépassement dans le classement).
      const r = carte.getBoundingClientRect();
      if (pos.left !== r.left || pos.top !== r.top) carte.animate([
        { transform: 'translate(' + (pos.left - r.left) + 'px,' + (pos.top - r.top) + 'px)' }, { transform: 'none' }
      ], { duration: 500, easing: 'cubic-bezier(.2,.8,.2,1)' });
    } else if (anciensTotaux.size) carte.classList.add('arrivee');
    if (avant !== undefined && p.total > avant) {
      compter(chiffres.firstChild, avant, p.total);
      // Point marqué : impact (💥 sur le score, onde de choc, éclats) puis « +N » qui s'envole.
      carte.classList.add('monte');
      carte.append(element('span', 'point-onde'), element('span', 'point-onde deux'), element('span', 'point-boum', '💥'));
      for (let k = 0; k < 8; k++) {
        const e = element('span', 'point-eclat');
        e.style.setProperty('--angle', (k * 45 + 20) + 'deg');
        carte.append(e);
      }
      carte.append(element('span', 'bulle', '+' + (p.total - avant)));
    }
  }
  boostsAvant = new Set((Array.isArray(data.sorts) ? data.sorts : []).filter(s => s.sort === 'boost')
    .map(s => String(s.pseudo).toLowerCase()));
  barragesAvant = new Set((Array.isArray(data.sorts) ? data.sorts : []).filter(s => s.sort === 'barrage')
    .map(s => String(s.pseudo).toLowerCase()));
  anciensTotaux.clear();
  for (const p of joueurs) anciensTotaux.set(p.pseudo, p.total);
}
let clientVersion = null;
function checkClientVersion(v) {
  if (!Number.isFinite(v) || v <= 0) return;
  if (clientVersion === null) { clientVersion = v; return; }
  if (v === clientVersion) return;
  const recharger = () => { if (annonceEnCours || annonces.length) setTimeout(recharger,1000); else location.reload(); };
  recharger();
}
let ws, wsRetry = 5000;
function wsConnect() {
  ws = new WebSocket((location.protocol === 'https:' ? 'wss://' : 'ws://') + location.host);
  ws.onopen = () => { wsRetry = 5000; };
  ws.onclose = () => {
    document.getElementById('classement-etat').textContent = 'RECONNEXION';
    setTimeout(wsConnect,wsRetry); wsRetry = Math.min(wsRetry * 1.5,120000);
  };
  ws.onerror = () => {};
  ws.onmessage = ev => { try {
    const d = JSON.parse(ev.data); if (!d || typeof d.type !== 'string') return;
    switch(d.type) {
      case 'app_config': checkClientVersion(d.client_version); break;
      case 'tts': ttsPlay(d); break;
      case 'progression': classement(d); break;
      case 'progression_gain': gainViewer(d); break;
      case 'garage_retour': annonceViewer('GARAGE',d.texte); break;
      case 'alerte_like': case 'alerte_don': alerteDon(d); break;
    }
  } catch (e) { console.warn('Événement overlay ignoré :',e.message); } };
}
window.GTAMaxOverlay = { alerte: alerteDon, classement, gain: gainViewer };
wsConnect();
