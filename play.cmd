@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo .NET SDK not found.
    pause
    exit /b 1
)

echo Building server...
dotnet build src\Server\Server.csproj -c Release --nologo -v:q
if errorlevel 1 (
    echo Server build failed.
    pause
    exit /b 1
)

if not exist "src\Server\bin\Release\net10.0\Server.exe" (
    echo Server build not found.
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
ping -n 3 127.0.0.1 >nul
start "" "unity-client\Builds\desktop\AlbionPrototype.exe"
