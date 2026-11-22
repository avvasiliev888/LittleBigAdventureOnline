# Telegram-пинг конвейера «Тандем».
#
# Отправляет текст в чат из .agent/telegram.json. По умолчанию молчит, пока нет
# .agent/away (пользователь за компьютером — писать ему в телефон незачем).
#
# Таймаут 10 с, 2 попытки: Invoke-RestMethod -> curl.exe -> node.
# Полный провал пишется строкой в docs/reports/tandem.log, скрипт НЕ падает.
# Токен никогда не печатается.
#
# Пример:
#   pwsh -File tools/tandem/notify.ps1 -Text "T-012: гейты зелёные"
#   pwsh -File tools/tandem/notify.ps1 -Text "тест" -Force    # проверка без .agent/away
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Text,
    [switch]$Force,
    [switch]$Quiet
)

$ErrorActionPreference = "Continue"

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$agentDir = Join-Path $root ".agent"
$awayFile = Join-Path $agentDir "away"
$secretFile = Join-Path $agentDir "telegram.json"
$logDir = Join-Path $root "docs/reports"
$logFile = Join-Path $logDir "tandem.log"

New-Item -ItemType Directory -Path $logDir -Force | Out-Null

function Write-Log {
    param([string]$Line)
    $stamp = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    Add-Content -Path $logFile -Value "$stamp  $Line" -Encoding UTF8
}

if (-not $Force -and -not (Test-Path $awayFile)) {
    Write-Log "skip  нет .agent/away — сообщение не отправлено: $($Text -replace "`n", " ")"
    if (-not $Quiet) { "skip: нет .agent/away" }
    exit 0
}

if (-not (Test-Path $secretFile)) {
    Write-Log "error нет .agent/telegram.json — отправка невозможна"
    if (-not $Quiet) { "error: нет .agent/telegram.json" }
    exit 0
}

try {
    $cfg = Get-Content $secretFile -Raw | ConvertFrom-Json
}
catch {
    Write-Log "error .agent/telegram.json не читается как JSON"
    if (-not $Quiet) { "error: telegram.json битый" }
    exit 0
}

$token = "$($cfg.token)"
$chatId = "$($cfg.chat_id)"
if (-not $token -or -not $chatId) {
    Write-Log "error в telegram.json нет token или chat_id"
    if (-not $Quiet) { "error: нет token/chat_id" }
    exit 0
}

$sent = $false
$how = ""

# --- 1. Invoke-RestMethod
for ($attempt = 1; $attempt -le 2 -and -not $sent; $attempt++) {
    try {
        $body = @{ chat_id = $chatId; text = $Text; disable_web_page_preview = $true }
        $resp = Invoke-RestMethod -Uri "https://api.telegram.org/bot$token/sendMessage" `
            -Method Post -Body $body -TimeoutSec 10 -ErrorAction Stop
        if ($resp.ok) { $sent = $true; $how = "Invoke-RestMethod" }
    }
    catch {
        if ($attempt -lt 2) { Start-Sleep -Seconds 2 }
    }
}

# --- 2. curl.exe
if (-not $sent) {
    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($curl) {
        try {
            $out = & curl.exe -s -m 10 -X POST "https://api.telegram.org/bot$token/sendMessage" `
                --data-urlencode "chat_id=$chatId" --data-urlencode "text=$Text" 2>&1
            if ($out -match '"ok":\s*true') { $sent = $true; $how = "curl" }
        }
        catch { }
    }
}

# --- 3. node
if (-not $sent) {
    $node = Get-Command node -ErrorAction SilentlyContinue
    if ($node) {
        $scriptPath = Join-Path $env:TEMP "tandem-notify.js"
        $js = @'
const https = require("https");
const token = process.env.TG_TOKEN, chat = process.env.TG_CHAT, text = process.env.TG_TEXT;
const data = JSON.stringify({chat_id: chat, text: text, disable_web_page_preview: true});
const req = https.request({hostname:"api.telegram.org", path:"/bot"+token+"/sendMessage", method:"POST",
  headers:{"Content-Type":"application/json","Content-Length":Buffer.byteLength(data)}, timeout:10000},
  res => { let b=""; res.on("data",d=>b+=d); res.on("end",()=>{ process.exit(b.includes('"ok":true')?0:1); }); });
req.on("error", () => process.exit(1));
req.on("timeout", () => { req.destroy(); process.exit(1); });
req.write(data); req.end();
'@
        try {
            Set-Content -Path $scriptPath -Value $js -Encoding UTF8
            $env:TG_TOKEN = $token
            $env:TG_CHAT = $chatId
            $env:TG_TEXT = $Text
            & node $scriptPath
            if ($LASTEXITCODE -eq 0) { $sent = $true; $how = "node" }
            Remove-Item Env:TG_TOKEN, Env:TG_CHAT, Env:TG_TEXT -ErrorAction SilentlyContinue
            Remove-Item $scriptPath -Force -ErrorAction SilentlyContinue
        }
        catch { }
    }
}

if ($sent) {
    Write-Log "sent  ($how) $($Text -replace "`n", " ")"
    if (-not $Quiet) { "sent via $how" }
}
else {
    Write-Log "fail  все три способа не сработали: $($Text -replace "`n", " ")"
    if (-not $Quiet) { "fail: см. docs/reports/tandem.log" }
}
exit 0
