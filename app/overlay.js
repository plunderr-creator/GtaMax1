/* GTA MAX — overlay (source navigateur OBS).
   Deux rôles, rien d'autre à l'écran :
     1. jouer la voix du chat (mp3 générés par le serveur, en série) ;
     2. afficher l'alerte de don, une à la fois, dans l'ordre d'arrivée.
   WebSocket, rechargement auto et lecture TTS repris de Final Max v0.3.0. */
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
  let _done = false;
  const done = () => { if (_done) return; _done = true; clearTimeout(watchdog); _ttsPlaying = false; ttsNext(); };
  const watchdog = setTimeout(done, 12000);
  a.onended = done; a.onerror = done;
  a.play().catch(done);
}

/* ══════════════════════════════════════
   ALERTE DON — file d'attente, une alerte après l'autre
   ══════════════════════════════════════ */
const ALERTE_MS   = 5000;   // durée d'affichage (entrée comprise)
const SORTIE_MS   = 500;    // = durée de l'animation `sortie` (overlay.css)
const ALERTE_MAX  = 50;     // au-delà, une rafale géante est coupée plutôt que de durer des minutes
const _alertes = [];
let _alerteEnCours = false;

function alerteDon(d){
  if (!d) return;
  if (_alertes.length >= ALERTE_MAX){ console.warn('Alerte ignorée (file pleine) :', d.pseudo); return; }
  _alertes.push(d);
  alerteSuivante();
}

function alerteSuivante(){
  if (_alerteEnCours) return;
  const d = _alertes.shift();
  if (!d) return;
  _alerteEnCours = true;
  afficherAlerte(d);
  setTimeout(() => {
    cacherAlerte();
    setTimeout(() => { _alerteEnCours = false; alerteSuivante(); }, SORTIE_MS);
  }, ALERTE_MS);
}

function afficherAlerte(d){
  const pseudo = String(d.pseudo || 'Quelqu\'un');
  const nombre = Math.max(1, parseInt(d.nombre, 10) || 1);
  const elPseudo = document.getElementById('alerte-pseudo');
  elPseudo.textContent = pseudo;
  const taille = Array.from(pseudo).length;
  elPseudo.className = 'pseudo' + (taille > 22 ? ' tres-long' : taille > 14 ? ' long' : '');
  // Construit en nœuds texte : un nom de cadeau ou un pseudo n'est jamais du HTML.
  const detail = document.getElementById('alerte-detail');
  detail.textContent = '';
  const cadeau = document.createElement('span');
  cadeau.className = 'cadeau';
  cadeau.textContent = String(d.cadeau || 'un cadeau') + (nombre > 1 ? ' ×' + nombre : '');
  const police = document.createElement('span');
  police.className = 'police';
  police.textContent = 'la police arrive !';
  detail.append('envoie ', cadeau, ' — ', police);

  const el = document.getElementById('alerte');
  el.classList.remove('sortie', 'visible');
  void el.offsetWidth;               // relance l'animation d'entrée même d'une alerte à l'autre
  el.classList.add('visible');
  document.getElementById('lueur').classList.add('active');
}

function cacherAlerte(){
  const el = document.getElementById('alerte');
  el.classList.remove('visible');
  el.classList.add('sortie');
  document.getElementById('lueur').classList.remove('active');
  setTimeout(() => { if (!el.classList.contains('visible')) el.classList.remove('sortie'); }, SORTIE_MS);
}

/* ══════════════════════════════════════
   RECHARGEMENT AUTO quand le code de l'overlay change
   ══════════════════════════════════════ */
// Sans ça, une source OBS restée ouverte tourne indéfiniment sur l'ancien JS.
// On attend la fin de l'alerte en cours : un rechargement la couperait net.
let _clientVersion = null;
function checkClientVersion(v){
  if (typeof v !== 'number' || !isFinite(v) || v <= 0) return;
  if (_clientVersion === null){ _clientVersion = v; return; }
  if (v === _clientVersion) return;
  const recharger = () => { if (_alerteEnCours || _alertes.length) setTimeout(recharger, 1000); else location.reload(); };
  recharger();
}

/* ══════════════════════════════════════
   WEBSOCKET — reconnexion automatique
   ══════════════════════════════════════ */
let ws, _wsRetry = 5000;
function wsConnect(){
  ws = new WebSocket((location.protocol === 'https:' ? 'wss://' : 'ws://') + location.host);
  ws.onopen = () => { _wsRetry = 5000; };
  ws.onclose = () => { setTimeout(wsConnect, _wsRetry); _wsRetry = Math.min(_wsRetry * 1.5, 120000); };
  ws.onerror = () => {};
  ws.onmessage = (ev) => { try {
    const data = JSON.parse(ev.data); if (!data || typeof data.type !== 'string') return;
    switch (data.type){
      case 'app_config':    checkClientVersion(data.client_version); break;
      case 'tts':           ttsPlay(data); break;
      case 'alerte_don':    alerteDon(data); break;
      case 'tiktok_status': break;   // ignoré volontairement : l'overlay n'affiche rien d'autre que l'alerte
    }
  } catch (e) {} };
}

// Aperçu à la main depuis la console : GTAMaxOverlay.alerte({ pseudo:'Bob', cadeau:'Rose', nombre:3 })
window.GTAMaxOverlay = { alerte: alerteDon };

wsConnect();
