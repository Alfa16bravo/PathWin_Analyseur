@echo off
setlocal
cd /d "%~dp0PathAnalyzer"

echo === Compilation et publication de PathWin Analyzer ===
dotnet publish -c Release -r win-x64 -p:SelfContained=false ^
  -p:PublishSingleFile=true -o "%~dp0dist" --nologo
if errorlevel 1 (
  echo.
  echo ECHEC de la compilation.
  pause
  exit /b 1
)

echo.
echo Executable genere : "%~dp0dist\PathWinAnalyzer.exe"
echo (necessite le runtime .NET Desktop 8 : https://dotnet.microsoft.com/download/dotnet/8.0)
echo.
echo Pour un executable totalement autonome (sans runtime a installer, ~70 Mo) :
echo   dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist-standalone
pause
