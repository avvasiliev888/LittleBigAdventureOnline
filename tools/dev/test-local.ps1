# Запуск dotnet test локально в обход ограничения окружения.
#
# Почему не просто `dotnet test` в рабочей папке: на этом хосте VSTest-testhost,
# запущенный с рабочим каталогом внутри F:\Projects\Albion_prototype, не может
# открыть handle родительского процесса (System.ComponentModel.Win32Exception (5)
# в Process.set_EnableRaisingEvents) и прогон прерывается до выполнения тестов.
# Тот же проект, собранный и запущенный вне workspace (например, в %TEMP%),
# проходит полностью. Скрипт копирует исходники во временный каталог, прогоняет
# тесты там и удаляет копию. CI (ubuntu-latest) этого обходного пути не требует.
#
# Использование: pwsh -File tools/dev/test-local.ps1 [-Configuration Release]

param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("albion-test-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))

try {
    New-Item -ItemType Directory -Path $tempRoot | Out-Null

    foreach ($item in @('src', 'tools', 'config')) {
        Copy-Item -Path (Join-Path $repoRoot $item) -Destination $tempRoot -Recurse
    }

    foreach ($file in @('Prototype.sln', 'global.json')) {
        Copy-Item -Path (Join-Path $repoRoot $file) -Destination $tempRoot
    }

    Push-Location $tempRoot
    try {
        & dotnet test 'src/Tests/Tests.csproj' -c $Configuration `
            /p:CollectCoverage=true `
            /p:CoverletOutputFormat=json `
            /p:Include="[Simulation.Core]Simulation.Core.Simulation" `
            /p:Threshold=100 `
            /p:ThresholdType=line
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($exitCode -ne 0) {
        Write-Error "Тесты провалены (exit $exitCode). Временная копия сохранена: $tempRoot"
        exit $exitCode
    }

    Write-Host "Тесты пройдены. Временная копия: $tempRoot"
    exit 0
}
finally {
    if ($exitCode -eq 0 -and (Test-Path $tempRoot)) {
        Remove-Item $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
