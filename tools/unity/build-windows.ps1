# Собирает Windows-плеер без GUI.
#
# Как и smoke/build-android, работает во временной копии: открытый Unity Editor
# блокирует исходный проект. Готовый билд копируется в unity-client/Builds/desktop.
#
# Пример:
#   pwsh -File tools/unity/build-windows.ps1
#   pwsh -File tools/unity/build-windows.ps1 -KeepProject
[CmdletBinding()]
param(
    [string]$UnityPath = "",
    [switch]$SkipPlugins,
    [switch]$KeepProject
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$client = Join-Path $root "unity-client"
$work = Join-Path $env:TEMP "albion-unity-agent"
$buildInWork = Join-Path $work "Builds/desktop"
$buildOut = Join-Path $client "Builds/desktop"

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

Write-Host "Копирование проекта в $work..."
New-Item -ItemType Directory -Force $work | Out-Null
& robocopy $client $work /E /XD Library Temp Logs Builds UserSettings obj /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
if ($LASTEXITCODE -gt 7) { throw "robocopy завершился с кодом $LASTEXITCODE" }
$LASTEXITCODE = 0

$unityLog = Join-Path $work "windows-build.log"
Remove-Item $unityLog -ErrorAction SilentlyContinue
if (Test-Path $buildInWork) { Remove-Item $buildInWork -Recurse -Force }

Write-Host "Сборка Windows-плеера..."
$unityProcess = Start-Process -FilePath $unity `
    -ArgumentList @("-batchmode", "-projectPath", $work, "-executeMethod", "Albion.Editor.AlbionBuild.BuildDesktopBatch", "-logFile", $unityLog) `
    -PassThru -Wait -WindowStyle Hidden

Write-Host "Unity exit code: $($unityProcess.ExitCode)"
$exe = Join-Path $buildInWork "AlbionPrototype.exe"
if ($unityProcess.ExitCode -ne 0 -or -not (Test-Path $exe)) {
    if (Test-Path $unityLog) {
        Write-Host "Последние строки Unity-лога:"
        Get-Content $unityLog -Tail 80
    }
    throw "Windows-билд не собран."
}

if (Test-Path $buildOut) { Remove-Item $buildOut -Recurse -Force }
New-Item -ItemType Directory -Force $buildOut | Out-Null
Copy-Item (Join-Path $buildInWork "*") $buildOut -Recurse -Force
$size = (Get-ChildItem $buildOut -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ("Билд: {0} ({1:N1} MB)" -f $buildOut, ($size / 1MB))

if (-not $KeepProject -and (Test-Path $work)) {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
