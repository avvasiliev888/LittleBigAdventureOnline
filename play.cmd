@echo off
setlocal
cd /d "%~dp0"

if not exist "src\Server\bin\Release\net10.0\Server.exe" (
    echo Server build not found.
    echo Run: dotnet build Prototype.sln -c Release
    pause
    exit /b 1
)

if not exist "unity-client\Builds\desktop\AlbionPrototype.exe" (
    echo Client build not found.
    echo Run: pwsh -File tools/unity/build-windows.ps1
    pause
    exit /b 1
)

start "Albion Server" "src\Server\bin\Release\net10.0\Server.exe" --udp 9050
timeout /t 2 /nobreak >nul
start "" "unity-client\Builds\desktop\AlbionPrototype.exe"
