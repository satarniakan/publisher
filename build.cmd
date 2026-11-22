@echo off
setlocal
cd /d "%~dp0"

echo Building linux-x64...
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o dist\linux-x64 || exit /b 1

echo Building win-x64...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o dist\win-x64 || exit /b 1

echo.
echo Done:
dir dist\linux-x64\publisher dist\win-x64\publisher.exe
