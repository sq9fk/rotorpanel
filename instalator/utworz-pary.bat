@echo off
title Tworzenie par portow com0com
cd /d "%~dp0"

net session >nul 2>&1
if errorlevel 1 (
  echo Wymagane uprawnienia administratora - podnosze...
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

set "COM0COM=%ProgramFiles(x86)%\com0com"
if not exist "%COM0COM%\setupc.exe" set "COM0COM=%ProgramFiles%\com0com"
if not exist "%COM0COM%\setupc.exe" (
  echo.
  echo   BLAD: nie znaleziono com0com.
  echo   Pobierz wersje PODPISANA - paczka z koncowka -signed w nazwie:
  echo         https://sourceforge.net/projects/com0com/files/com0com/
  echo.
  pause & exit /b 1
)

REM setupc szuka com0com.inf w katalogu biezacym - dlatego cd
cd /d "%COM0COM%"

echo Tworze pary portow...
echo.
setupc.exe install PortName=COM10 PortName=CNCB10
setupc.exe install PortName=COM11 PortName=CNCB11
setupc.exe install PortName=COM12 PortName=CNCB12

echo.
echo === stan par ===
setupc.exe list

echo.
echo Lewa strona kazdej pary (COM10-COM12) jest dla PstRotatora,
echo prawa (CNCB10-CNCB12) dla mostka w RotorPanelu.
echo.
pause
