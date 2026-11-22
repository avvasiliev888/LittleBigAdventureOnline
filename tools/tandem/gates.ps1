# Гейты конвейера «Тандем».
#
# По порядку, стоп на первом провале:
#   1) dotnet build Prototype.sln -c Release
#   2) dotnet test src/Tests/Tests.csproj --no-build -c Release
#   3) bash tools/ci/check-architecture.sh + check-unity-boundaries.sh (WARN, если bash нет)
#   4) Unity compile (batchmode), таймаут 20 мин
#
# В конце печатает ОДИН JSON: { ok, failed_step, tail, steps[], log_dir }.
# Полные логи — docs/reports/logs/<task>-<step>.log, в JSON только хвост.
#
# Пример:
#   pwsh -File tools/tandem/gates.ps1 -Task T-012
#   pwsh -File tools/tandem/gates.ps1 -Task T-012 -SkipUnity      # быстрая проверка .NET
#   pwsh -File tools/tandem/gates.ps1 -Task T-012 -AsObject       # объект вместо JSON-строки
[CmdletBinding()]
param(
    [string]$Task = "gate",
    [switch]$SkipUnity,
    [switch]$SkipTests,
    [switch]$SkipArch,
    [switch]$AsObject,
    [int]$BuildTimeoutMin = 10,
    [int]$TestTimeoutMin = 10,
    [int]$UnityTimeoutMin = 20,
    [int]$TailLines = 40
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$logRoot = Join-Path $root "docs/reports/logs"
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null

$steps = New-Object System.Collections.ArrayList

function Write-Step {
    param([string]$Name, [string]$Status, [string]$Detail, [double]$Seconds)
    [void]$steps.Add([pscustomobject]@{
            name    = $Name
            status  = $Status      # pass | fail | warn | skip
            detail  = $Detail
            seconds = [math]::Round($Seconds, 1)
        })
}

# Запуск внешней команды с таймаутом. Вывод — в файл, потом склеиваем.
function Invoke-Logged {
    param(
        [string]$Exe,
        [string]$ArgLine,
        [string]$LogPath,
        [int]$TimeoutMin,
        [string]$WorkDir = $root
    )

    $outLog = "$LogPath.out"
    $errLog = "$LogPath.err"
    foreach ($f in @($outLog, $errLog, $LogPath)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $Exe -ArgumentList $ArgLine -WorkingDirectory $WorkDir `
        -NoNewWindow -PassThru -RedirectStandardOutput $outLog -RedirectStandardError $errLog
    $timedOut = $false
    try {
        Wait-Process -Id $p.Id -Timeout ($TimeoutMin * 60) -ErrorAction Stop
    }
    catch {
        $timedOut = $true
        try { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } catch { }
    }
    $sw.Stop()

    $p.Refresh()
    $code = if ($p.HasExited) { $p.ExitCode } else { -1 }

    $body = @()
    if (Test-Path $outLog) { $body += Get-Content $outLog -ErrorAction SilentlyContinue }
    if (Test-Path $errLog) { $body += Get-Content $errLog -ErrorAction SilentlyContinue }
    $body | Set-Content -Path $LogPath -Encoding UTF8 -ErrorAction SilentlyContinue
    Remove-Item $outLog, $errLog -Force -ErrorAction SilentlyContinue

    [pscustomobject]@{
        ExitCode = $code
        TimedOut = $timedOut
        Seconds  = $sw.Elapsed.TotalSeconds
        Log      = $LogPath
    }
}

function Get-Tail {
    param([string]$Path, [int]$Count = 40)
    if (-not (Test-Path $Path)) { return "" }
    return ((Get-Content $Path -Tail $Count -ErrorAction SilentlyContinue) -join "`n")
}

function Get-UnityError {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return "" }
    $hits = Select-String -Path $Path -Pattern "error CS|Compilation failed|Scripts have compiler errors|Exception:" -ErrorAction SilentlyContinue |
        Select-Object -First 15
    if (-not $hits) { return "" }
    return (($hits | ForEach-Object { $_.Line.Trim() }) -join "`n")
}

function Resolve-UnityExe {
    if ($env:UNITY_EDITOR_PATH -and (Test-Path $env:UNITY_EDITOR_PATH)) {
        return (Resolve-Path $env:UNITY_EDITOR_PATH).Path
    }

    $versionFile = Join-Path $root "unity-client/ProjectSettings/ProjectVersion.txt"
    $version = ""
    if (Test-Path $versionFile) {
        $line = Select-String -Path $versionFile -Pattern "^m_EditorVersion:" | Select-Object -First 1
        if ($line) { $version = ($line.Line -replace "^m_EditorVersion:\s*", "").Trim() }
    }
    if (-not $version) { $version = "6000.3.28f1" }

    $hubs = @()
    $hubFile = Join-Path $env:APPDATA "UnityHub/secondaryInstallPath.json"
    if (Test-Path $hubFile) {
        try { $hubs += (Get-Content $hubFile -Raw | ConvertFrom-Json).ToString().Trim('"') } catch { }
    }
    $hubs += "C:\Program Files\Unity\Hub\Editor"
    $hubs += "D:\Unity\Hub\Editor"
    $hubs += "E:\Unity\Hub\Editor"
    $hubs += "N:\Program Files\Unity Hub\Editor"

    foreach ($hub in $hubs) {
        if (-not $hub) { continue }
        $candidate = Join-Path $hub "$version\Editor\Unity.exe"
        if (Test-Path $candidate) { return $candidate }
        $any = Get-ChildItem $hub -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending | Select-Object -First 1
        if ($any) {
            $candidate = Join-Path $any.FullName "Editor\Unity.exe"
            if (Test-Path $candidate) { return $candidate }
        }
    }
    return $null
}

# ---------------------------------------------------------------- 1. build
$step = "build"
$log = Join-Path $logRoot "$Task-$step.log"
$r = Invoke-Logged -Exe "dotnet" `
    -ArgLine "build `"$root\Prototype.sln`" -c Release --nologo -v:q" `
    -LogPath $log -TimeoutMin $BuildTimeoutMin
if ($r.TimedOut) {
    Write-Step $step "fail" "таймаут $BuildTimeoutMin мин" $r.Seconds
    $failed = $step; $tail = Get-Tail $log $TailLines
}
elseif ($r.ExitCode -ne 0) {
    Write-Step $step "fail" "exit $($r.ExitCode)" $r.Seconds
    $failed = $step; $tail = Get-Tail $log $TailLines
}
else {
    Write-Step $step "pass" "exit 0" $r.Seconds; $failed = $null
}

# ---------------------------------------------------------------- 2. test
if (-not $failed -and -not $SkipTests) {
    $step = "test"
    $log = Join-Path $logRoot "$Task-$step.log"
    $r = Invoke-Logged -Exe "dotnet" `
        -ArgLine "test `"$root\src\Tests\Tests.csproj`" --no-build -c Release" `
        -LogPath $log -TimeoutMin $TestTimeoutMin
    if ($r.TimedOut) {
        Write-Step $step "fail" "таймаут $TestTimeoutMin мин" $r.Seconds
        $failed = $step; $tail = Get-Tail $log $TailLines
    }
    elseif ($r.ExitCode -ne 0) {
        Write-Step $step "fail" "exit $($r.ExitCode)" $r.Seconds
        $failed = $step; $tail = Get-Tail $log $TailLines
    }
    else {
        Write-Step $step "pass" "exit 0" $r.Seconds
    }
}
elseif (-not $failed) {
    Write-Step "test" "skip" "пропущено флагом -SkipTests" 0
}

# ---------------------------------------------------- 3. архитектурные скрипты
if (-not $failed -and -not $SkipArch) {
    # bash на Windows бывает трёх сортов: Git Bash (годится), WSL-заглушка
    # WindowsApps (не годится: пути /mnt/f, другой shell), MSYS. Проверяем фактом.
    $bashCandidates = @()
    if ($env:TANDEM_BASH -and (Test-Path $env:TANDEM_BASH)) { $bashCandidates += $env:TANDEM_BASH }
    $bashCandidates += "C:\Program Files\Git\bin\bash.exe"
    $bashCandidates += "C:\Program Files\Git\usr\bin\bash.exe"
    $bashCandidates += "C:\Program Files (x86)\Git\bin\bash.exe"

    $bash = $null
    $bashKind = ""
    foreach ($candidate in $bashCandidates) {
        if (-not (Test-Path $candidate)) { continue }
        try {
            $kind = & $candidate -c "uname -s" 2>$null
            if ("$kind" -match "MINGW|MSYS") { $bash = $candidate; $bashKind = "$kind"; break }
        }
        catch { }
    }
    if (-not $bash) {
        $onPath = Get-Command bash -ErrorAction SilentlyContinue
        if ($onPath) {
            try {
                $kind = & $onPath.Source -c "uname -s" 2>$null
                if ("$kind" -match "MINGW|MSYS") { $bash = $onPath.Source; $bashKind = "$kind" }
            }
            catch { }
        }
    }

    if (-not $bash) {
        Write-Step "arch" "warn" "bash нет (или только WSL-заглушка) — проверка архитектуры пропущена, не блокирует" 0
    }
    else {
        $scripts = @(
            @{ name = "check-architecture"; path = "tools/ci/check-architecture.sh" },
            @{ name = "check-unity-boundaries"; path = "tools/ci/check-unity-boundaries.sh" }
        )
        foreach ($s in $scripts) {
            $step = $s.name
            $log = Join-Path $logRoot "$Task-$step.log"
            if (-not (Test-Path (Join-Path $root $s.path))) {
                Write-Step $step "warn" "нет файла $($s.path)" 0
                continue
            }
            Push-Location $root
            try {
                $out = & $bash $s.path 2>&1
                $code = $LASTEXITCODE
            }
            finally { Pop-Location }
            ($out | Out-String) | Set-Content -Path $log -Encoding UTF8
            if ($code -ne 0) {
                Write-Step $step "fail" "exit $code ($bashKind)" 0
                $failed = $step; $tail = Get-Tail $log $TailLines
                break
            }
            Write-Step $step "pass" "exit 0 ($bashKind)" 0
        }
    }
}
elseif (-not $failed) {
    Write-Step "arch" "skip" "пропущено флагом -SkipArch" 0
}

# ---------------------------------------------------------------- 4. Unity
if (-not $failed -and -not $SkipUnity) {
    $step = "unity"
    $log = Join-Path $logRoot "$Task-$step.log"
    $editorProcs = @(Get-Process -Name "Unity" -ErrorAction SilentlyContinue)
    if ($editorProcs.Count -gt 0) {
        Write-Step $step "fail" "запущен Unity.exe (PID $($editorProcs[0].Id)) — закрой редактор" 0
        "Закрой Unity Editor (PID $($editorProcs[0].Id)) — batchmode не может открыть занятый проект." |
            Set-Content -Path $log -Encoding UTF8
        $failed = $step; $tail = Get-Tail $log $TailLines
    }
    else {
        $unity = Resolve-UnityExe
        if (-not $unity) {
            Write-Step $step "fail" "Unity.exe не найден: задай UNITY_EDITOR_PATH" 0
            "Unity.exe не найден. Задай переменную окружения UNITY_EDITOR_PATH." |
                Set-Content -Path $log -Encoding UTF8
            $failed = $step; $tail = Get-Tail $log $TailLines
        }
        else {
            $project = Join-Path $root "unity-client"
            $unityLog = Join-Path $env:TEMP "tandem-unity-compile.log"
            if (Test-Path $unityLog) { Remove-Item $unityLog -Force -ErrorAction SilentlyContinue }
            $argLine = "-batchmode -quit -projectPath `"$project`" -logFile `"$unityLog`""
            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            $p = Start-Process -FilePath $unity -ArgumentList $argLine -PassThru -NoNewWindow
            $timedOut = $false
            try { Wait-Process -Id $p.Id -Timeout ($UnityTimeoutMin * 60) -ErrorAction Stop }
            catch {
                $timedOut = $true
                try { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } catch { }
            }
            $sw.Stop()
            $p.Refresh()
            $code = if ($p.HasExited) { $p.ExitCode } else { -1 }
            if (Test-Path $unityLog) { Copy-Item $unityLog $log -Force }

            $unityErr = Get-UnityError $unityLog
            if ($timedOut) {
                Write-Step $step "fail" "таймаут $UnityTimeoutMin мин" $sw.Elapsed.TotalSeconds
                $failed = $step; $tail = Get-Tail $log $TailLines
            }
            elseif ($code -ne 0 -or $unityErr) {
                Write-Step $step "fail" "exit $code, ошибки компиляции: $(($unityErr -split "`n").Count)" $sw.Elapsed.TotalSeconds
                Add-Content -Path $log -Value "`n--- errors ---`n$unityErr" -Encoding UTF8
                $failed = $step
                $tail = (($unityErr + "`n" + (Get-Tail $log 20)) -split "`n" | Select-Object -Last $TailLines) -join "`n"
            }
            else {
                Write-Step $step "pass" "exit 0" $sw.Elapsed.TotalSeconds
            }
        }
    }
}
elseif (-not $failed) {
    Write-Step "unity" "skip" "пропущено флагом -SkipUnity" 0
}

if (-not $failed) { $tail = "" }

$result = [pscustomobject]@{
    ok          = (-not $failed)
    failed_step = $failed
    tail        = $tail
    steps       = @($steps)
    log_dir     = $logRoot
}

if ($AsObject) { return $result }
$result | ConvertTo-Json -Depth 5 -Compress
if ($failed) { exit 1 }
exit 0
