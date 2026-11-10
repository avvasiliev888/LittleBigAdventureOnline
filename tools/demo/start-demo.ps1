<#
.SYNOPSIS
  Запускает демо M12: один UDP-сервер и два desktop-клиента.

.DESCRIPTION
  Собирает сервер (если нужно), проверяет плагины Unity и готовый desktop-билд, затем запускает
  сервер и два окна AlbionPrototype.exe. С -Seconds N работает как smoke-тест: ждёт N секунд,
  закрывает клиентов/сервер и проверяет, что сервер принял два Hello.

  Для сборки клиента: Unity -> Albion -> Build -> Desktop, либо
    Unity.exe -batchmode -quit -projectPath unity-client `
      -executeMethod Albion.Editor.AlbionBuild.BuildDesktopBatch -logFile unity-client/Builds/m12-build-desktop.log

.EXAMPLE
  pwsh -File tools/demo/start-demo.ps1

.EXAMPLE
  pwsh -File tools/demo/start-demo.ps1 -DelayMs 100 -LossPercent 30
#>
[CmdletBinding()]
param(
    [int]$Port = 9050,
    [int]$DelayMs = 0,
    [int]$LossPercent = 0,
    [ValidateSet('desktop', 'mobile')]
    [string]$Quality = 'desktop',
    [int]$Seconds = 0,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Push-Location $root
$serverProcess = $null
$clients = @()
try {
    $player = Join-Path $root 'unity-client/Builds/desktop/AlbionPrototype.exe'
    if (-not (Test-Path $player)) {
        throw "Нет desktop-билда: $player. Соберите Unity -> Albion -> Build -> Desktop."
    }

    $plugins = Join-Path $root 'unity-client/Assets/Plugins/Albion/Client.Network.dll'
    if (-not (Test-Path $plugins)) {
        Write-Host '[demo] плагины Unity отсутствуют, собираю...'
        & pwsh -File tools/unity/build-plugins.ps1
        if ($LASTEXITCODE -ne 0) { throw 'build-plugins.ps1 failed' }
    }

    if (-not $NoBuild) {
        Write-Host '[demo] сборка сервера...'
        dotnet build src/Server/Server.csproj -c Release --nologo | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'server build failed' }
    }

    $artifactDir = Join-Path $root 'artifacts/demo'
    New-Item -ItemType Directory -Force $artifactDir | Out-Null
    $serverLog = Join-Path $artifactDir 'server.log'
    $serverErr = Join-Path $artifactDir 'server.err.log'
    Remove-Item $serverLog, $serverErr -ErrorAction SilentlyContinue

    $serverArgs = @(
        'src/Server/bin/Release/net10.0/Server.dll',
        '--udp', "$Port"
    )
    if ($DelayMs -gt 0) { $serverArgs += @('--delay', "$DelayMs") }
    if ($LossPercent -gt 0) { $serverArgs += @('--loss', "$LossPercent") }
    if ($Seconds -gt 0) { $serverArgs += @('--seconds', "$($Seconds + 5)") }

    Write-Host "[demo] server UDP $Port, delay=$DelayMs ms, loss=$LossPercent%"
    $serverProcess = Start-Process -FilePath dotnet -ArgumentList $serverArgs `
        -RedirectStandardOutput $serverLog -RedirectStandardError $serverErr -PassThru
    Start-Sleep -Seconds 2
    if ($serverProcess.HasExited) {
        throw "сервер завершился сразу; смотрите $serverLog"
    }

    Write-Host '[demo] запускаю два клиента...'
    $beforeClients = @(Get-Process AlbionPrototype -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
    foreach ($index in 1..2) {
        # Start-Process на этом хосте зависает на GUI-плеере (оболочка ждёт закрытия окна),
        # поэтому запускаем через `cmd /c start` и находим появившийся процесс по PID.
        & cmd /c start "" "$player" -serverPort "$Port" -quality "$Quality"
        Start-Sleep -Milliseconds 800
        $newClient = Get-Process AlbionPrototype -ErrorAction SilentlyContinue |
            Where-Object { $beforeClients -notcontains $_.Id } |
            Select-Object -First 1
        if ($null -eq $newClient) {
            throw "клиент $index не запустился (см. Player.log)"
        }

        $beforeClients += $newClient.Id
        $clients += $newClient
        Write-Host ("[demo] client {0}: PID {1}" -f $index, $newClient.Id)
    }

    if ($Seconds -le 0) {
        Write-Host "[demo] запущено. Сервер: PID $($serverProcess.Id), клиенты: $($clients.Id -join ', ')"
        Write-Host "[demo] логи сервера: $serverLog"
        Write-Host '[demo] закройте окна клиентов и остановите сервер вручную (Ctrl+C).'
        return
    }

    Write-Host "[demo] smoke: жду $Seconds с..."
    Start-Sleep -Seconds $Seconds
    foreach ($client in $clients) {
        if ($null -ne $client -and -not $client.HasExited) {
            Stop-Process -Id $client.Id -Force -ErrorAction SilentlyContinue
        }
    }

    if (-not $serverProcess.WaitForExit(($Seconds + 20) * 1000)) {
        throw 'сервер не остановился вовремя'
    }

    $hellos = @(Select-String -Path $serverLog -Pattern 'Hello принят' -ErrorAction SilentlyContinue).Count
    $snapshots = @(Select-String -Path $serverLog -Pattern 'tick metrics:' -ErrorAction SilentlyContinue).Count
    Write-Host "[demo] сервер принял Hello: $hellos/2, строк метрик: $snapshots"
    if ($hellos -lt 2) {
        throw "ожидались 2 подключения, получено $hellos (см. $serverLog)"
    }

    Write-Host '[demo] PASS: два клиента подключились'
}
finally {
    if ($Seconds -gt 0) {
        foreach ($client in $clients) {
            if ($null -ne $client -and -not $client.HasExited) {
                Stop-Process -Id $client.Id -Force -ErrorAction SilentlyContinue
            }
        }

        if ($null -ne $serverProcess -and -not $serverProcess.HasExited) {
            Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue
        }
    }

    Pop-Location
}
