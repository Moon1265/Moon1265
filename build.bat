@echo off
rem Builds Moon1265.dll and copies the mod into KSP.
rem Double-click to run, or pass a different KSP folder: build.bat "D:\Games\Kerbal Space Program"
setlocal
set "KSPDIR=C:\Steam\steamapps\common\Kerbal Space Program"
if not "%~1"=="" set "KSPDIR=%~1"
cd /d "%~dp0"
echo Building Moon1265 against "%KSPDIR%" ...
echo.
dotnet build "Source\Moon1265\Moon1265.csproj" -c Release -nologo -p:KSPDIR="%KSPDIR%" "-flp:logfile=build-log.txt;verbosity=normal"
echo.
if errorlevel 1 (
  echo BUILD FAILED. Send Claude the errors above, or the file build-log.txt in this folder.
) else (
  echo BUILD SUCCEEDED. The mod has been copied into "%KSPDIR%\GameData\Moon1265".
)
echo.
pause
