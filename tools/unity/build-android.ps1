# Собирает Android APK клиента без GUI.
#
# Unity Editor не может открыть проект, который уже открыт в редакторе, поэтому сборка идёт
# во временной копии (как agent-smoke.ps1). Плагины и JSON пересобираются заранее.
#
# Требуется установленный модуль Android Build Support для версии из ProjectVersion.txt:
#   Unity Hub -- --headless install-modules --version 6000.3.28f1 -m android --cm
#
# Пример:
#   pwsh -File tools/unity/build-android.ps1
#   pwsh -File tools/unity/build-android.ps1 -KeepProject
[CmdletBinding()]
param(
    [string]$UnityPath = "",
    [string]$ServerHost = "",
    [switch]$SkipPlugins,
    [switch]$KeepProject
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$client = Join-Path $root "unity-client"
$work = Join-Path $env:TEMP "albion-unity-agent"
$apkInWork = Join-Path $work "Builds/android/AlbionPrototype.apk"
$apkOut = Join-Path $client "Builds/android/AlbionPrototype.apk"

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
$androidPlayer = Join-Path (Split-Path $unity) "Data/PlaybackEngines/AndroidPlayer"
if (-not (Test-Path $androidPlayer)) {
    throw "Не установлен Android Build Support: нет $androidPlayer. Установите модуль android в Unity Hub."
}
Write-Host "Unity: $unity"
Write-Host "AndroidPlayer: $androidPlayer"

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

if (-not $ServerHost) {
    try {
        $route = Get-NetRoute -DestinationPrefix "0.0.0.0/0" -ErrorAction Stop |
            Sort-Object RouteMetric |
            Select-Object -First 1
        $ServerHost = (Get-NetIPAddress -InterfaceIndex $route.ifIndex -AddressFamily IPv4 -ErrorAction Stop |
            Where-Object { $_.IPAddress -notlike "169.254.*" } |
            Select-Object -First 1).IPAddress
    } catch {
        $ServerHost = "192.168.1.2"
    }
}

if ($ServerHost) {
    $resourceDir = Join-Path $work "Assets/Resources"
    New-Item -ItemType Directory -Force $resourceDir | Out-Null
    Set-Content -Path (Join-Path $resourceDir "MobileServerHost.txt") -Value $ServerHost -Encoding UTF8
    Write-Host "IP сервера по умолчанию в APK: $ServerHost"
}

$unityLog = Join-Path $work "android-build.log"
Remove-Item $unityLog, $apkInWork -ErrorAction SilentlyContinue

Write-Host "Сборка APK (IL2CPP ARM64, это может занять несколько минут)..."
$unityProcess = Start-Process -FilePath $unity `
    -ArgumentList @("-batchmode", "-projectPath", $work, "-executeMethod", "Albion.Editor.AlbionBuild.BuildAndroidBatch", "-logFile", $unityLog) `
    -PassThru -Wait -WindowStyle Hidden

Write-Host "Unity exit code: $($unityProcess.ExitCode)"
if ($unityProcess.ExitCode -ne 0 -or -not (Test-Path $apkInWork)) {
    if (Test-Path $unityLog) {
        Write-Host "Последние строки Unity-лога:"
        Get-Content $unityLog -Tail 60
    }
    throw "APK не собран."
}

New-Item -ItemType Directory -Force (Split-Path $apkOut) | Out-Null
Copy-Item $apkInWork $apkOut -Force
$hash = (Get-FileHash $apkOut -Algorithm SHA256).Hash
Write-Host ("APK: {0} ({1:N1} MB)" -f $apkOut, ((Get-Item $apkOut).Length / 1MB))
Write-Host "SHA256: $hash"

if (-not $KeepProject -and (Test-Path $work)) {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}

