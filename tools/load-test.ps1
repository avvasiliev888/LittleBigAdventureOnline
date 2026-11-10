<#
.SYNOPSIS
  M11 load test: starts a UDP server and N headless bots, then checks tick metrics.

.DESCRIPTION
  Runs for --Seconds, parses the final `tick final metrics` line and fails if:
    - the bots did not all connect or receive snapshots;
    - the server or bot process returned non-zero;
    - average tick > 25 ms or p99 > 50 ms.
  Artifacts (server/bot logs and summary JSON) go to artifacts/m11-load/.

.EXAMPLE
  pwsh -File tools/load-test.ps1
  pwsh -File tools/load-test.ps1 -Bots 20 -Seconds 60
#>
[CmdletBinding()]
param(
    [int]$Bots = 20,
    [int]$Seconds = 60,
    [int]$Port = 9061
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
$serverProcess = $null
try {
    $artifactDir = Join-Path $root 'artifacts/m11-load'
    New-Item -ItemType Directory -Force $artifactDir | Out-Null
    $serverLog = Join-Path $artifactDir 'server.log'
    $serverErr = Join-Path $artifactDir 'server.err.log'
    $botLog = Join-Path $artifactDir 'bots.log'
    Remove-Item $serverLog, $serverErr, $botLog -ErrorAction SilentlyContinue

    Write-Host "[load] building server and bot-client..."
    dotnet build src/Server/Server.csproj -c Release --nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "server build failed" }
    dotnet build tools/bot-client/BotClient.csproj -c Release --nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "bot-client build failed" }

    $serverSeconds = $Seconds + 5
    Write-Host "[load] starting server on UDP $Port for $serverSeconds s (metrics every 100 ticks)"
    $serverProcess = Start-Process -FilePath dotnet `
        -ArgumentList @(
            'src/Server/bin/Release/net10.0/Server.dll',
            '--udp', "$Port",
            '--seconds', "$serverSeconds",
            '--metrics-every', '100'
        ) `
        -RedirectStandardOutput $serverLog `
        -RedirectStandardError $serverErr `
        -PassThru
    Start-Sleep -Seconds 2

    Write-Host "[load] starting $Bots bots for $Seconds s..."
    dotnet tools/bot-client/bin/Release/net10.0/BotClient.dll `
        --host 127.0.0.1 --port $Port --bots $Bots --seconds $Seconds *> $botLog
    $botExit = $LASTEXITCODE
    Get-Content $botLog -Tail 4 | ForEach-Object { Write-Host $_ }

    if (-not $serverProcess.WaitForExit(($Seconds + 20) * 1000)) {
        throw "server did not stop within $($Seconds + 20)s"
    }

    $serverExit = $serverProcess.ExitCode
    $botDone = Select-String -Path $botLog -Pattern '^\[bots\] done: ' | Select-Object -Last 1
    $metricsLine = Select-String -Path $serverLog -Pattern 'tick final metrics:' | Select-Object -Last 1
    if ($null -eq $metricsLine) {
        $metricsLine = Select-String -Path $serverLog -Pattern 'tick metrics:' | Select-Object -Last 1
    }

    if ($botExit -ne 0) { throw "bot-client exited with $botExit" }
    if ($serverExit -ne 0) { throw "server exited with $serverExit" }
    if ($null -eq $botDone) { throw "bot summary missing" }
    if ($null -eq $metricsLine) { throw "server tick metrics missing" }

    $text = $metricsLine.Line
    if ($text -notmatch 'n=(\d+)\s+avg=([\d.]+)ms\s+p50=([\d.]+)ms\s+p99=([\d.]+)ms\s+max=([\d.]+)ms') {
        throw "cannot parse metrics: $text"
    }

    $sampleCount = [int]$Matches[1]
    $average = [double]::Parse($Matches[2], [Globalization.CultureInfo]::InvariantCulture)
    $p50 = [double]::Parse($Matches[3], [Globalization.CultureInfo]::InvariantCulture)
    $p99 = [double]::Parse($Matches[4], [Globalization.CultureInfo]::InvariantCulture)
    $max = [double]::Parse($Matches[5], [Globalization.CultureInfo]::InvariantCulture)

    $summary = [ordered]@{
        bots = $Bots
        seconds = $Seconds
        samples = $sampleCount
        averageMs = $average
        p50Ms = $p50
        p99Ms = $p99
        maxMs = $max
        averageLimitMs = 25
        p99LimitMs = 50
        botSummary = $botDone.Line.Trim()
        serverLine = $text.Trim()
    }
    $summary | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $artifactDir 'summary.json') -Encoding utf8
    $summary | Format-List | Out-Host

    if ($sampleCount -lt ($Seconds * 15)) {
        throw "too few tick samples: $sampleCount"
    }
    if ($average -gt 25) { throw "average tick $average ms > 25 ms" }
    if ($p99 -gt 50) { throw "p99 tick $p99 ms > 50 ms" }

    Write-Host "[load] PASS: $Bots bots, $Seconds s, avg=$average ms, p99=$p99 ms, max=$max ms"
    exit 0
}
finally {
    if ($null -ne $serverProcess -and -not $serverProcess.HasExited) {
        Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue
    }
    Pop-Location
}
