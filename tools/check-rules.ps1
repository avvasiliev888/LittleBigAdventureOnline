<#
.SYNOPSIS
    Механическая проверка жёстких запретов проекта.

.DESCRIPTION
    Проверяет то, что раньше держалось на памяти агента в AGENT.md — файле,
    который DSH не загружает. Пока правило живёт только текстом, его нарушают
    молча; здесь каждое нарушение — падение с кодом 1.

    Проверки:
      1. src/Simulation.Core не ссылается на UnityEngine (ADR-001).
      2. src/Simulation.Core не содержит MonoBehaviour (ADR-001).
      3. src/Shared и src/Simulation.Core не используют System.Text.Json (ADR-011).
      4. В src/Tests нет отключённых тестов: [Ignore] и Skip=.
      5. В корне нет AGENT.md — DSH его не читает, правила из него не работают.

    Комментарии (// и /* */) из кода вырезаются перед поиском: в проекте есть
    комментарии вида «System.Text.Json не используется», и проверка по сырому
    тексту давала бы ложное срабатывание каждый раз.

.EXAMPLE
    pwsh tools/check-rules.ps1
    pwsh tools/check-rules.ps1 -Fix     # напечатать, что именно нарушено, и выйти с 1
#>
[CmdletBinding()]
param(
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$violations = New-Object System.Collections.Generic.List[string]

# Каталоги, которые не индексируются и не проверяются: они дают мегабайты
# мусора и к правилам отношения не имеют.
$skipDirs = @('obj', 'bin', 'Library', 'Temp', 'Builds', 'node_modules', '.git')

function Get-CsFiles {
    param([string[]]$Paths)
    $found = @()
    foreach ($p in $Paths) {
        if (Test-Path $p) {
            $found += Get-ChildItem -Path $p -Recurse -Filter *.cs -File |
                Where-Object { $d = $_.FullName; -not ($skipDirs | Where-Object { $d -match "\\$_\\" }) }
        }
    }
    return $found
}

# Вырезает комментарии, сохраняя номера строк, чтобы отчёт указывал на верное место.
function Get-CodeText {
    param([string]$Path)
    $lines = Get-Content -LiteralPath $Path -Encoding UTF8
    $inBlock = $false
    $out = New-Object System.Collections.Generic.List[string]
    $i = 0
    foreach ($line in $lines) {
        $i++
        $s = $line
        if ($inBlock) {
            $end = $s.IndexOf('*/')
            if ($end -lt 0) { $out.Add(''); continue }
            $s = $s.Substring($end + 2)
            $inBlock = $false
        }
        # блочный комментарий целиком в строке
        $s = [regex]::Replace($s, '/\*.*?\*/', '')
        # незакрытый блочный комментарий
        $open = $s.IndexOf('/*')
        if ($open -ge 0) { $s = $s.Substring(0, $open); $inBlock = $true }
        # строчный комментарий
        $sl = $s.IndexOf('//')
        if ($sl -ge 0) { $s = $s.Substring(0, $sl) }
        if ($s.Trim()) { $out.Add("$($i):$s") } else { $out.Add('') }
    }
    return $out
}

function Find-InCode {
    param(
        [string[]]$Paths,
        [string]$Pattern,
        [string]$Rule,
        [string]$Why
    )
    $files = Get-CsFiles -Paths $Paths
    foreach ($f in $files) {
        $code = Get-CodeText -Path $f.FullName
        foreach ($row in $code) {
            if ($row -and $row -match $Pattern) {
                $rel = $f.FullName.Replace("$root\", '').Replace('\', '/')
                $violations.Add("$Rule — ${rel}:$row   ($Why)")
            }
        }
    }
}

# --- 1, 2. Изоляция Simulation.Core (ADR-001) -------------------------------
Find-InCode -Paths @('src/Simulation.Core') -Pattern 'UnityEngine' `
    -Rule 'ADR-001' -Why 'Simulation.Core не должен ссылаться на UnityEngine'

Find-InCode -Paths @('src/Simulation.Core') -Pattern '\bMonoBehaviour\b' `
    -Rule 'ADR-001' -Why 'авторитетная логика не живёт в MonoBehaviour'

# --- 3. Единственный JSON-парсер — MiniJson (ADR-011) ----------------------
Find-InCode -Paths @('src/Shared', 'src/Simulation.Core') -Pattern 'System\.Text\.Json' `
    -Rule 'ADR-011' -Why 'для конфигов только MiniJson'

# --- 4. Отключённые тесты ----------------------------------------------
Find-InCode -Paths @('src/Tests') -Pattern '\[Ignore|Skip\s*=' `
    -Rule 'TEST' -Why 'флапящий тест — дефект; чинить причину, а не отключать'

# --- 5. Неактивный файл правил -----------------------------------------
if (Test-Path 'AGENT.md') {
    $violations.Add('AGENT.md — DSH загружает только AGENTS.md; правила в AGENT.md не работают. Содержание переносить в docs/rules.md.')
}

# --- 6. Наборы анимаций ссылаются на существующие клипы ----------------
# Опечатка в имени клипа не даёт ошибки нигде: Unity оставит состояние без
# анимации, моб замрёт в T-позе, и виноватой сочтут «кривую модель». Проверка
# идёт по инвентарю, снятому из Unity, а не по догадкам о именах.
if (Test-Path 'tools/unity/check-animation-sets.py') {
    $setsOutput = & python tools/unity/check-animation-sets.py 2>&1
    if ($LASTEXITCODE -ne 0) {
        $violations.Add('Наборы анимаций: ' + (($setsOutput | Select-Object -First 3) -join ' | '))
    }
}

# --- Отчёт -------------------------------------------------------------
if ($violations.Count -eq 0) {
    if (-not $Quiet) {
        Write-Host '  OK  Запреты не нарушены.' -ForegroundColor Green
        Write-Host '      Simulation.Core без Unity, JSON через MiniJson, тесты активны.' -ForegroundColor DarkGray
    }
    exit 0
}

Write-Host "  НАРУШЕНО ПРАВИЛ: $($violations.Count)" -ForegroundColor Red
foreach ($v in $violations) { Write-Host "    $v" -ForegroundColor Red }
exit 1
