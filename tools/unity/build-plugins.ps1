# Собирает netstandard2.1-сборки и раскладывает их в Unity как плагины.
# Запуск из корня репозитория: pwsh -File tools/unity/build-plugins.ps1
# Плагины генерируемые: не коммитятся (см. unity-client/.gitignore), их пересобирает этот скрипт.
[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$clientProject = Join-Path $root "src/Client.Network/Client.Network.csproj"
$target = Join-Path $root "unity-client/Assets/Plugins/Albion"

Write-Host "Сборка netstandard2.1: $clientProject"
& dotnet build $clientProject -c $Configuration -f netstandard2.1
if ($LASTEXITCODE -ne 0) {
    throw "Сборка не удалась (exit $LASTEXITCODE)."
}

$output = Join-Path $root "src/Client.Network/bin/$Configuration/netstandard2.1"
if (-not (Test-Path $output)) {
    throw "Каталог сборки не найден: $output"
}

New-Item -ItemType Directory -Force -Path $target | Out-Null

# Стабильные имена: Unity импортирует DLL как плагины и сам создаёт для них .meta.
$assemblies = @(
    "Simulation.Core.dll",
    "Shared.dll",
    "Client.Network.dll",
    "LiteNetLib.dll"
)

foreach ($assembly in $assemblies) {
    $source = Join-Path $output $assembly
    if (-not (Test-Path $source)) {
        throw "Не найден $assembly в $output — проверьте PackageReference LiteNetLib и ProjectReference."
    }

    Copy-Item -Path $source -Destination (Join-Path $target $assembly) -Force

    Write-Host ("  " + $assembly + " -> " + $target)
}

Write-Host "Плагины для Unity обновлены: $target"
