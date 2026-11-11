# Автономный PlayMode smoke-тест Unity-клиента без GUI.
#
# Запускает сервер, копирует unity-client во временный каталог (открытый Unity Editor
# блокирует исходный проект), запускает Unity в batchmode, открывает Arena, входит в Play
# и проверяет, что клиент получил снапшоты, создал HUD и нарисовал мобов.
#
# Требуется установленный Unity Editor той же версии, что в ProjectVersion.txt, и лицензия.
# Путь ищется в UNITY_EDITOR_PATH, затем в Unity Hub secondaryInstallPath и стандартных местах.
#
# Пример:
#   pwsh -File tools/unity/agent-smoke.ps1
#   pwsh -File tools/unity/agent-smoke.ps1 -Port 9300 -KeepProject
[CmdletBinding()]
param(
    [int]$Port = 0,
    [int]$TimeoutSeconds = 180,
    [string]$UnityPath = "",
    [switch]$SkipPlugins,
    [switch]$KeepProject
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$client = Join-Path $root "unity-client"
$work = Join-Path $env:TEMP "albion-unity-agent"
$resultFile = Join-Path $work "agent-smoke-result.txt"

function Resolve-Unity {
    param([string]$Explicit)

    if ($Explicit) {
        if (-not (Test-Path $Explicit)) { throw "Unity.exe не найден: $Explicit" }
        return (Resolve-Path $Explicit).Path
    }

    if ($env:UNITY_EDITOR_PATH -and (Test-Path $env:UNITY_EDITOR_PATH)) {
        return (Resolve-Path $env:UNITY_EDITOR_PATH).Path
    }

    $versionLine = Select-String -Path (Join-Path $client "ProjectSettings/ProjectVersion.txt") -Pattern "^m_EditorVersion:" | Select-Object -First 1
    if (-not $versionLine) { throw "Не удалось прочитать версию Unity из ProjectVersion.txt" }
    $version = ($versionLine.Line -replace "^m_EditorVersion:\s*", "").Trim()

    $hubPathFile = Join-Path $env:APPDATA "UnityHub/secondaryInstallPath.json"
    $hubRoot = $null
    if (Test-Path $hubPathFile) {
        $hubRoot = (Get-Content $hubPathFile -Raw | ConvertFrom-Json)
    }

    $candidates = @(
        (Join-Path "C:\Program Files\Unity\Hub\Editor" "$version\Editor\Unity.exe"),
        (Join-Path "C:\Program Files\Unity Hub\Editor" "$version\Editor\Unity.exe")
    )
    if ($hubRoot) {
        $candidates = @((Join-Path $hubRoot "$version\Editor\Unity.exe")) + $candidates
    }

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return (Resolve-Path $candidate).Path }
    }

    throw "Unity $version не найден. Укажите -UnityPath или переменную UNITY_EDITOR_PATH."
}

$unity = Resolve-Unity -Explicit $UnityPath
Write-Host "Unity: $unity"

if (-not $SkipPlugins) {
    Write-Host "Пересборка плагинов и конфигов для Unity..."
    & pwsh -NoProfile -File (Join-Path $root "tools/unity/build-plugins.ps1")
    if ($LASTEXITCODE -ne 0) { throw "build-plugins.ps1 завершился с кодом $LASTEXITCODE" }
}

# Копируем только исходники: Library остаётся от предыдущего прогона и сильно ускоряет импорт.
Write-Host "Копирование проекта в $work..."
New-Item -ItemType Directory -Force $work | Out-Null
& robocopy $client $work /E /XD Library Temp Logs Builds UserSettings obj /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
if ($LASTEXITCODE -gt 7) { throw "robocopy завершился с кодом $LASTEXITCODE" }
$LASTEXITCODE = 0

if ($Port -eq 0) { $Port = Get-Random -Minimum 9200 -Maximum 9800 }
Write-Host "Порт сервера: $Port"

$serverLog = Join-Path $work "server-smoke.log"
$unityLog = Join-Path $work "unity-smoke.log"
Remove-Item $serverLog, $unityLog, $resultFile -ErrorAction SilentlyContinue

$server = $null
$oldPort = $env:ALBION_SERVER_PORT
try {
    $server = Start-Process -FilePath "dotnet" `
        -ArgumentList @("run", "--project", (Join-Path $root "src/Server"), "-c", "Release", "--no-build", "--", "--udp", "$Port", "--seconds", "$TimeoutSeconds", "--metrics-every", "0") `
        -RedirectStandardOutput $serverLog -RedirectStandardError "$serverLog.err" -PassThru -WindowStyle Hidden

    $ready = $false
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 250
        if ((Test-Path $serverLog) -and (Select-String -Path $serverLog -Pattern "UDP-сервер слушает" -Quiet)) {
            $ready = $true
            break
        }
        if ($server.HasExited) { break }
    }
    if (-not $ready) {
        $tail = if (Test-Path $serverLog) { Get-Content $serverLog -Tail 20 } else { @() }
        throw "Сервер не запустился на порту $Port.`n$($tail -join "`n")"
    }

    $env:ALBION_SERVER_PORT = "$Port"
    $unityProcess = Start-Process -FilePath $unity `
        -ArgumentList @("-batchmode", "-projectPath", $work, "-executeMethod", "Albion.Editor.AgentSmokeTest.Run", "-logFile", $unityLog) `
        -PassThru -Wait -WindowStyle Hidden

    Write-Host "Unity exit code: $($unityProcess.ExitCode)"
    if (Test-Path $resultFile) {
        $result = Get-Content $resultFile -Raw
        Write-Host $result.Trim()
    } else {
        Write-Host "Результат smoke-теста не записан."
    }

    if ($unityProcess.ExitCode -ne 0) {
        if (Test-Path $unityLog) {
            Write-Host "Последние строки Unity-лога:"
            Get-Content $unityLog -Tail 30
        }
        exit $unityProcess.ExitCode
    }
} finally {
    if ($server -and -not $server.HasExited) {
        Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
    }
    $env:ALBION_SERVER_PORT = $oldPort
    if (-not $KeepProject -and (Test-Path $work)) {
        Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}
