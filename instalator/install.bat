@echo off
setlocal
title Instalacja RotorPanel
cd /d "%~dp0"

echo ============================================
echo   RotorPanel - instalacja
echo ============================================
echo.

set "CEL=%LOCALAPPDATA%\RotorPanel"
set "SETUPC=%ProgramFiles(x86)%\com0com\setupc.exe"
if not exist "%SETUPC%" set "SETUPC=%ProgramFiles%\com0com\setupc.exe"

REM ---------- 1. com0com ----------
echo [1/3] Sprawdzam com0com...
if exist "%SETUPC%" (
  echo       jest: %SETUPC%
) else (
  echo.
  echo   UWAGA: nie znaleziono com0com.
  echo   Pobierz wersje PODPISANA - paczka z koncowka -signed w nazwie:
  echo         https://sourceforge.net/projects/com0com/files/com0com/
  echo.
  echo   Instalacja programu bedzie kontynuowana, ale pary portow
  echo   trzeba bedzie utworzyc pozniej w samym programie.
  echo.
  pause
)

REM ---------- 2. kopiowanie ----------
echo.
echo [2/3] Kopiuje program do %CEL%
if not exist "%CEL%" mkdir "%CEL%"
xcopy /E /I /Y /Q "app\*" "%CEL%\" >nul
if errorlevel 1 (
  echo   BLAD kopiowania.
  pause & exit /b 1
)
echo       gotowe.

REM ---------- 3. skroty ----------
echo.
echo [3/3] Tworze skroty...
powershell -NoProfile -Command ^
  "$s=(New-Object -ComObject WScript.Shell).CreateShortcut([Environment]::GetFolderPath('Desktop')+'\RotorPanel.lnk');" ^
  "$s.TargetPath='%CEL%\RotorPanel.exe'; $s.WorkingDirectory='%CEL%'; $s.Description='Mostki rotorow do ser2net'; $s.Save();" ^
  "$m=[Environment]::GetFolderPath('Programs'); $s2=(New-Object -ComObject WScript.Shell).CreateShortcut($m+'\RotorPanel.lnk');" ^
  "$s2.TargetPath='%CEL%\RotorPanel.exe'; $s2.WorkingDirectory='%CEL%'; $s2.Save()"
echo       skrot na pulpicie i w menu Start.

echo.
echo ============================================
echo   Zainstalowane.
echo ============================================
echo.
echo   Program nie wymaga zadnego srodowiska uruchomieniowego -
echo   dziala na .NET Framework 4.8 wbudowanym w Windows 10 i 11.
echo.
echo   Startuje do zasobnika przy zegarze. Krzyzyk go tam chowa,
echo   wyjscie jest w menu pod prawym przyciskiem na ikonie.
echo   autostart.bat wlacza uruchamianie przy logowaniu.
echo.
echo   Nastepne kroki:
echo     1. Uruchom RotorPanel i otworz "Pary COM..." - przycisk "Nowa para"
echo        zaklada pare portow. Program sam poprosi o uprawnienia.
echo     2. W Ustawieniach podaj adres ser2net i sterownika anten,
echo        zdefiniuj rotory i przypisz je do anten.
echo.
pause
