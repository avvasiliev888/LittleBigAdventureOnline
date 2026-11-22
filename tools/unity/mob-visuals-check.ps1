<#
Что: проверяет, какие префабы реально привязаны к ключам мобов в MobVisuals.asset.
Зачем: «моб выглядит не так» — обычно ответ в том, что ключу назначен чужой префаб или
префаб ссылается на битый GUID. Ошибка Unity при этом молчит: битая ссылка читается как null.

Как: разбирает asset как текст, находит у каждого ключа GUID префаба, разрешает GUID в путь
через .meta, затем достаёт из префаба имя меша и аватара и их пути.

Запуск: pwsh -File tools/unity/mob-visuals-check.ps1
#>
param(
    [string]$Asset = "unity-client/Assets/Resources/Characters/MobVisuals.asset",
    [string]$AssetsRoot = "unity-client/Assets"
)

$ErrorActionPreference = "Stop"
$root = (Get-Location).Path

function Resolve-Guid([string]$guid) {
    if (-not $guid -or $guid -eq "00000000000000000000000000000000") { return $null }
    $hit = Get-ChildItem (Join-Path $root $AssetsRoot) -Recurse -Filter '*.meta' -ErrorAction SilentlyContinue |
        Where-Object { (Get-Content $_.FullName -TotalCount 3) -match "guid: $guid" } |
        Select-Object -First 1
    if (-not $hit) { return "<not found: $guid>" }
    return $hit.FullName.Substring($root.Length + 1).Replace('\meta', '').Replace('.meta', '')
}

$lines = Get-Content (Join-Path $root $Asset)
$entries = @()
$current = $null
foreach ($line in $lines) {
    if ($line -match '^\s*-\s*MobKey:\s*(\S+)') {
        if ($current) { $entries += $current }
        $current = [ordered]@{ Key = $matches[1]; Guid = $null; Height = $null; Flat = $null }
        continue
    }
    if (-not $current) { continue }
    if ($line -match 'Prefab:\s*\{fileID:\s*\d+,\s*guid:\s*([0-9a-f]{32})') { $current.Guid = $matches[1] }
    if ($line -match '^\s*Height:\s*(\S+)') { $current.Height = $matches[1] }
    if ($line -match '^\s*AllowFlatColour:\s*(\S+)') { $current.Flat = $matches[1] }
}
if ($current) { $entries += $current }

$rows = foreach ($e in $entries) {
    $prefab = Resolve-Guid $e.Guid
    $mesh = "<-"; $avatar = "<-"
    $prefabFull = if ($prefab -and $prefab -notlike '<*') { Join-Path $root $prefab } else { $null }
    if ($prefabFull -and (Test-Path $prefabFull)) {
        $text = Get-Content $prefabFull
        $meshLine = $text | Select-String -Pattern 'm_Mesh:\s*\{fileID:\s*\d+,\s*guid:\s*([0-9a-f]{32})' | Select-Object -First 1
        if ($meshLine) { $mesh = Resolve-Guid $meshLine.Matches[0].Groups[1].Value }
        $avatarLine = $text | Select-String -Pattern 'm_Avatar:\s*\{fileID:\s*\d+,\s*guid:\s*([0-9a-f]{32})' | Select-Object -First 1
        if ($avatarLine) { $avatar = Resolve-Guid $avatarLine.Matches[0].Groups[1].Value }
    }
    [pscustomobject]@{
        Key = $e.Key
        Prefab = $prefab
        Height = $e.Height
        FlatColour = $e.Flat
        Mesh = $mesh
        Avatar = $avatar
    }
}

$rows | Format-List
"--- ITOGO: $($rows.Count) мобов;" 
