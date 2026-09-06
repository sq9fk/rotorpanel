@echo off
setlocal
title RotorPanel - autostart
set "CEL=%LOCALAPPDATA%\RotorPanel\RotorPanel.exe"
set "LNK=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\RotorPanel.lnk"

if not exist "%CEL%" (
  echo Najpierw uruchom install.bat - nie znalazlem %CEL%
  pause & exit /b 1
)

if exist "%LNK%" (
  del "%LNK%"
  echo Autostart WYLACZONY.
) else (
  powershell -NoProfile -Command ^
    "$s=(New-Object -ComObject WScript.Shell).CreateShortcut('%LNK%');" ^
    "$s.TargetPath='%CEL%'; $s.WorkingDirectory='%LOCALAPPDATA%\RotorPanel'; $s.Save()"
  echo Autostart WLACZONY - program bedzie startowal do zasobnika przy logowaniu.
)
echo.
pause
