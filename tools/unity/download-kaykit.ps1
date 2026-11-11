# Скачивает CC0-набор KayKit для Albion: герой, скелеты-мобы, оружие и окружение.
# Источники — официальные GitHub-репозитории KayKit-Game-Assets (CC0 1.0).
# После запуска: Unity -> Albion -> Art -> Prepare All Imported Art,
# затем Albion -> Art -> Configure KayKit All.
[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$charDir = Join-Path $root "unity-client/Assets/Art/Characters/KayKit"
$weaponDir = Join-Path $root "unity-client/Assets/Art/Weapons/KayKit"
$envDir = Join-Path $root "unity-client/Assets/Art/Environment/KayKit"
$licenseDir = Join-Path $root "unity-client/Assets/Art/KayKit"
$temp = Join-Path $env:TEMP ("albion-kaykit-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Force $temp | Out-Null

$adventurers = "https://raw.githubusercontent.com/KayKit-Game-Assets/KayKit-Character-Pack-Adventures-1.0/main/addons/kaykit_character_pack_adventures"
$skeletons = "https://raw.githubusercontent.com/KayKit-Game-Assets/KayKit-Character-Pack-Skeletons-1.0/main/addons/kaykit_character_pack_skeletons"

function Get-Raw {
    param([string]$Url, [string]$Path)
    if ((Test-Path $Path) -and -not $Force) {
        Write-Host "  exists: $Path"
        return
    }
    New-Item -ItemType Directory -Force (Split-Path -Parent $Path) | Out-Null
    Write-Host "  GET $Url"
    Invoke-WebRequest -Uri $Url -OutFile $Path -Headers @{ "User-Agent" = "AlbionAssetBot" } -TimeoutSec 600
}

New-Item -ItemType Directory -Force $charDir, $weaponDir, $envDir, $licenseDir | Out-Null

Write-Host "KayKit Adventurers: Knight + weapons"
Get-Raw "$adventurers/Characters/fbx/Knight.fbx" (Join-Path $charDir "Knight.fbx")
foreach ($texture in @("knight_texture", "rogue_texture", "mage_texture", "barbarian_texture")) {
    Get-Raw "$adventurers/Characters/fbx/$texture.png" (Join-Path $charDir "$texture.png")
}
foreach ($weapon in @("sword_1handed", "axe_1handed", "staff", "crossbow_1handed", "dagger")) {
    Get-Raw "$adventurers/Assets/fbx/$weapon.fbx" (Join-Path $weaponDir "$weapon.fbx")
}

Write-Host "KayKit Skeletons: three monsters"
foreach ($mob in @("Skeleton_Minion", "Skeleton_Rogue", "Skeleton_Warrior")) {
    Get-Raw "$skeletons/Characters/fbx/$mob.fbx" (Join-Path $charDir "$mob.fbx")
}
Get-Raw "$skeletons/Characters/fbx/skeleton_texture.png" (Join-Path $charDir "skeleton_texture.png")

Write-Host "KayKit Dungeon: selected environment OBJ props"
$dungeonZip = Join-Path $temp "dungeon.zip"
Invoke-WebRequest -Uri "https://codeload.github.com/KayKit-Game-Assets/KayKit-Dungeon-Remastered-1.0/zip/refs/heads/main" `
    -OutFile $dungeonZip -Headers @{ "User-Agent" = "AlbionAssetBot" } -TimeoutSec 600
Expand-Archive -LiteralPath $dungeonZip -DestinationPath $temp -Force
$dungeonAssets = Get-ChildItem $temp -Recurse -Directory |
    Where-Object { $_.Name -eq "Assets" -and $_.Parent.Name -eq "kaykit_dungeon_remastered" } |
    Select-Object -First 1
if ($null -eq $dungeonAssets) { throw "Dungeon archive layout changed: Assets/obj not found." }
$props = @("barrel_large", "crates_stacked", "chest", "torch_mounted", "table_medium", "chair", "column", "banner_red")
foreach ($prop in $props) {
    foreach ($ext in @(".obj", ".mtl")) {
        $source = Join-Path $dungeonAssets.FullName "obj/$prop$ext"
        if (Test-Path $source) { Copy-Item $source (Join-Path $envDir "$prop$ext") -Force }
    }
}
Copy-Item (Join-Path $dungeonAssets.FullName "texture/dungeon_texture.png") `
    (Join-Path $envDir "dungeon_texture.png") -Force

Write-Host "CC0 licenses"
Invoke-WebRequest -Uri "https://raw.githubusercontent.com/KayKit-Game-Assets/KayKit-Character-Pack-Adventures-1.0/main/LICENSE.txt" `
    -OutFile (Join-Path $licenseDir "LICENSE-Adventurers.txt") -Headers @{ "User-Agent" = "AlbionAssetBot" } -TimeoutSec 60
Invoke-WebRequest -Uri "https://raw.githubusercontent.com/KayKit-Game-Assets/KayKit-Character-Pack-Skeletons-1.0/main/LICENSE.txt" `
    -OutFile (Join-Path $licenseDir "LICENSE-Skeletons.txt") -Headers @{ "User-Agent" = "AlbionAssetBot" } -TimeoutSec 60
Invoke-WebRequest -Uri "https://raw.githubusercontent.com/KayKit-Game-Assets/KayKit-Dungeon-Remastered-1.0/main/LICENSE.txt" `
    -OutFile (Join-Path $licenseDir "LICENSE-Dungeon.txt") -Headers @{ "User-Agent" = "AlbionAssetBot" } -TimeoutSec 60

Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ""
Write-Host "Готово. В Unity выполните:"
Write-Host "  1) Albion -> Art -> Prepare All Imported Art"
Write-Host "  2) Albion -> Art -> Configure KayKit All"
Write-Host "  3) Albion -> Art -> Validate Imported Art"
