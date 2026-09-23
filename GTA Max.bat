@echo off
title GTA Max
cd /d "%~dp0app"
if not exist "node\node.exe" (
    echo.
    echo  [ERREUR] Dossier "app" incomplet ou deplace.
    echo  Gardez "GTA Max.bat" a cote du dossier "app".
    echo.
    pause
    exit /b 1
)
rem Port lu comme le serveur le lit (.env PORT, sinon config.json, sinon 3200) :
rem un port change dans le setup ouvre toujours la bonne page.
set "PORT_GTAMAX=3200"
for /f "usebackq delims=" %%p in (`node\node.exe outils\port.js`) do set "PORT_GTAMAX=%%p"
echo.
echo  Demarrage de GTA Max... les reglages vont s'ouvrir dans le navigateur.
echo  NE FERMEZ PAS cette fenetre noire pendant le live.
echo  (la fermer = arreter GTA Max)
echo.
start "" /min cmd /c "timeout /t 2 /nobreak >nul & start "" http://localhost:%PORT_GTAMAX%/setup"
"node\node.exe" server.js
echo.
echo  GTA Max est arrete. Vous pouvez fermer cette fenetre.
pause
