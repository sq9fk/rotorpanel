@echo off
REM Buduje wersje wynikowa i sklada paczke instalacyjna w dist\paczka.
cd /d "%~dp0.."

echo === publikowanie ===
dotnet publish -c Release -p:DebugType=none -o dist\portable48
if errorlevel 1 (echo BLAD kompilacji & pause & exit /b 1)

echo.
echo === skladanie paczki ===
if exist dist\paczka rmdir /s /q dist\paczka
mkdir dist\paczka\app

copy /y dist\portable48\RotorPanel.exe dist\paczka\app\ >nul
copy /y rotory.przyklad.json dist\paczka\app\rotory.json >nul
copy /y instalator\install.bat dist\paczka\ >nul
copy /y instalator\utworz-pary.bat dist\paczka\ >nul
copy /y instalator\autostart.bat dist\paczka\ >nul
copy /y instalator\CZYTAJ-TO.txt dist\paczka\ >nul

powershell -NoProfile -Command "Compress-Archive -Path 'dist\paczka\*' -DestinationPath 'dist\RotorPanel-paczka.zip' -Force"

echo.
echo Gotowe: %cd%\dist\RotorPanel-paczka.zip
pause
