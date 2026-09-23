/**
 * roster.js — la table des viewers (fonctions pures, testables sans IO).
 *
 * C'est LA table sur laquelle tout gameplay futur se posera. En v1 elle ne
 * porte AUCUN champ de jeu, seulement :
 *   { pseudo_minuscule: { premiere_vue, derniere_vue, dons_total } }
 *   - premiere_vue / derniere_vue : horodatages en ms epoch ;
 *   - dons_total : cumul en PIÈCES TikTok (diamondCount × repeatCount),
 *     pas un nombre de cadeaux — une Rose (1) et un Univers (44 999) ne
 *     pèsent pas pareil, et c'est cette valeur qu'une économie voudra lire.
 * La clé est le pseudo TikTok (uniqueId) en minuscules : c'est l'identifiant
 * stable, le surnom affiché change quand le viewer le veut.
 * Ici on décide ; c'est persistance.js qui écrit.
 */

function cle(pseudo) {
  return String(pseudo == null ? '' : pseudo).trim().toLowerCase().slice(0, 60);
}

// Number(null) vaut 0 et Number('') aussi : une fiche éditée à la main avec
// "premiere_vue": null daterait le viewer de 1970. Seuls un nombre ou un texte
// numérique non vide comptent ; tout le reste prend la valeur par défaut.
function nombre(x, defaut) {
  const n = typeof x === 'number' ? x : (typeof x === 'string' && x.trim() ? Number(x) : NaN);
  return Number.isFinite(n) ? n : defaut;
}

// Le viewer est passé devant le live (message, cadeau, like, follow, partage).
// Crée la fiche au premier passage, sinon ne touche QUE derniere_vue. Une fiche
// abîmée à la main (champ manquant, texte à la place d'un nombre) est réparée
// champ par champ, jamais remplacée : premiere_vue est la seule trace du jour
// où le viewer est arrivé, elle ne se reconstruit pas.
function vu(viewers, pseudo, maintenant) {
  const k = cle(pseudo);
  if (!k || !viewers || typeof viewers !== 'object') return null;
  let v = viewers[k];
  if (!v || typeof v !== 'object') v = viewers[k] = {};
  v.premiere_vue = nombre(v.premiere_vue, maintenant);
  v.derniere_vue = maintenant;
  v.dons_total   = Math.max(0, nombre(v.dons_total, 0));
  return v;
}

// Cadeau reçu : passage + cumul des pièces (jamais négatif, jamais NaN).
function ajouterDon(viewers, pseudo, pieces, maintenant) {
  const v = vu(viewers, pseudo, maintenant);
  if (!v) return null;
  v.dons_total += Math.max(0, nombre(pieces, 0));
  return v;
}

module.exports = { cle, vu, ajouterDon };
