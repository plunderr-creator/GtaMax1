/**
 * outils/plugin.js — compile et installe le plugin jeu (scripts/GTAMax.cs).
 *
 * Usage : node outils/plugin.js installer   (c'est ce que lance « Installer plugin GTA.bat »)
 *
 * On compile NOUS-MÊMES en GTAMax.dll avec le csc du .NET Framework 4.x (présent
 * sur tout Windows 10/11), contre ScriptHookVDotNet3.dll :
 *   - SHVDN reconnaît l'API v3 à la référence de la DLL (un .cs brut déposé dans
 *     scripts\ serait compilé contre l'API v2 sauf à le nommer *.3.cs) ;
 *   - une erreur de compilation se voit ICI, dans la fenêtre, et pas en jeu au
 *     milieu d'un live dans ScriptHookVDotNet.log.
 * ⚠️ Ce csc ne connaît que C# 5 : pas de $"", pas de ?., pas de nameof.
 *
 * Empreinte : l'installateur écrit scripts\GTAMax.version (SHA-1 de la source
 * compilée). Le serveur la compare à la source du dépôt pour dire « plugin à
 * jour / périmé / absent » dans le setup — une date de fichier mentirait après
 * un simple git checkout.
 */
const fs = require('fs');
const os = require('os');
const path = require('path');
const crypto = require('crypto');
const { spawnSync } = require('child_process');

const RACINE     = path.join(__dirname, '..', '..');
const SOURCE     = path.join(RACINE, 'scripts', 'GTAMax.cs');
const INI_MODELE = path.join(RACINE, 'scripts', 'GTAMax.ini');

function trouverCsc() {
  const win = process.env.WINDIR || process.env.SystemRoot || 'C:\\Windows';
  for (const fw of ['Framework64', 'Framework']) {
    const p = path.join(win, 'Microsoft.NET', fw, 'v4.0.30319', 'csc.exe');
    if (fs.existsSync(p)) return p;
  }
  return null;
}

// Rend { ok, sortie } — sortie = messages du compilateur (UTF-8 : /utf8output,
// sinon les accents sortent dans la page de code OEM de la console).
function compiler({ sources, sortie, references = [], defines = [], cible = 'library' }) {
  const csc = trouverCsc();
  if (!csc) return { ok: false, sortie: 'csc.exe introuvable : le .NET Framework 4.x est-il installé ?' };
  const args = ['/nologo', '/utf8output', '/target:' + cible, '/optimize+', '/warn:4', '/out:' + sortie,
    ...references.map(r => '/reference:' + r),
    ...(defines.length ? ['/define:' + defines.join(';')] : []),
    ...sources];
  const r = spawnSync(csc, args, { encoding: 'utf8', windowsHide: true });
  const texte = ((r.stdout || '') + (r.stderr || '') + (r.error ? String(r.error.message) : '')).trim();
  return { ok: r.status === 0, sortie: texte };
}

function empreinte(fichier) {
  return crypto.createHash('sha1').update(fs.readFileSync(fichier)).digest('hex');
}

// 'absent' | 'a_jour' | 'perime' — lecture seule, appelé par /api/gta/status.
function etatPlugin(dossierJeu) {
  try {
    if (!fs.existsSync(path.join(dossierJeu, 'scripts', 'GTAMax.dll'))) return 'absent';
    const installee = fs.readFileSync(path.join(dossierJeu, 'scripts', 'GTAMax.version'), 'utf8').trim();
    return installee === empreinte(SOURCE) ? 'a_jour' : 'perime';
  } catch { return 'perime'; }
}

function installer(dossierJeu) {
  if (!dossierJeu || !fs.existsSync(path.join(dossierJeu, 'GTA5.exe'))) {
    return { ok: false, message: `GTA5.exe introuvable dans « ${dossierJeu} » — règle le dossier GTA V dans le setup.` };
  }
  const shvdn3 = path.join(dossierJeu, 'ScriptHookVDotNet3.dll');
  if (!fs.existsSync(shvdn3)) {
    return { ok: false, message: 'ScriptHookVDotNet3.dll absent du dossier du jeu : installe ScriptHookVDotNet v3 d\'abord (voir README).' };
  }
  // Compilé À CÔTÉ puis copié : si GTA V tourne, GTAMax.dll est verrouillée et
  // la copie échoue avec un message clair, sans laisser une DLL à moitié écrite.
  const tmp = path.join(os.tmpdir(), 'gtamax-' + process.pid + '-' + Date.now() + '.dll');
  const c = compiler({ sources: [SOURCE], sortie: tmp, references: [shvdn3] });
  if (!c.ok) return { ok: false, message: 'Compilation échouée :\n' + c.sortie };
  const scripts = path.join(dossierJeu, 'scripts');
  try {
    fs.mkdirSync(scripts, { recursive: true });
    fs.copyFileSync(tmp, path.join(scripts, 'GTAMax.dll'));
    fs.writeFileSync(path.join(scripts, 'GTAMax.version'), empreinte(SOURCE) + '\n', 'utf8');
  } catch (e) {
    return { ok: false, message: `Copie impossible dans ${scripts} : ${e.message}` + (e.code === 'EBUSY' || e.code === 'EPERM' ? ' — ferme GTA V puis relance.' : '') };
  } finally {
    try { fs.unlinkSync(tmp); } catch {}
  }
  let ini = 'GTAMax.ini conservé (tes réglages ne sont jamais écrasés).';
  if (!fs.existsSync(path.join(scripts, 'GTAMax.ini'))) {
    fs.copyFileSync(INI_MODELE, path.join(scripts, 'GTAMax.ini'));
    ini = 'GTAMax.ini déposé.';
  }
  return { ok: true, message: `Plugin installé : ${path.join(scripts, 'GTAMax.dll')}\n${ini}` + (c.sortie ? '\n' + c.sortie : '') };
}

module.exports = { trouverCsc, compiler, empreinte, etatPlugin, installer, SOURCE };

if (require.main === module) {
  const cmd = process.argv[2];
  if (cmd !== 'installer') { console.error('usage : node outils/plugin.js installer'); process.exit(2); }
  const dossier = String(require('./lire-config').lireConfig().gta.dossier_jeu || '');
  console.log('🎮 Dossier GTA V : ' + dossier);
  const r = installer(dossier);
  (r.ok ? console.log : console.error)((r.ok ? '✅ ' : '❌ ') + r.message);
  process.exit(r.ok ? 0 : 1);
}
