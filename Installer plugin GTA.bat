@echo off
title GTA Max - installation du plugin
cd /d "%~dp0app"
if not exist "node\node.exe" (
    echo.
    echo  [ERREUR] Dossier "app" incomplet ou deplace.
    echo  Gardez "Installer plugin GTA.bat" a cote du dossier "app".
    echo.
    pause
    exit /b 1
)
echo.
echo  Compilation de scripts\GTAMax.cs et installation dans le dossier de GTA V.
echo  GTA V doit etre FERME (sinon GTAMax.dll est verrouillee).
echo.
"node\node.exe" outils\plugin.js installer
echo.
pause
