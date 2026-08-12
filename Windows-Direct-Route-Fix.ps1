param(
    [switch]$CheckOnly,
    [switch]$ApplyDomains,
    [switch]$BroadFallback,
    [switch]$RestartTarget,
    [string]$Domains = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$reg = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'
$profileFile = Join-Path $env:LOCALAPPDATA 'WindowsDirectRouteFix\profile.json'
$rollbackFile = Join-Path $env:LOCALAPPDATA 'WindowsDirectRouteFix\rollback-state.json'

function JsonField([string]$Text, [string]$Name) {
    $m = [regex]::Match($Text, ('"' + [regex]::Escape($Name) + '"\s*:\s*(?:"([^"]*)"|(true|false|-?\d+(?:\.\d+)?))'))
    if (!$m.Success) { return '' }
    if ($m.Groups[1].Success) { return $m.Groups[1].Value }
    return $m.Groups[2].Value
}

function Read-Proxy {
    $p = Get-ItemProperty -LiteralPath $reg
    [pscustomobject]@{ Enable = [int]$p.ProxyEnable; Server = [string]$p.ProxyServer; Override = [string]$p.ProxyOverride }
}

function Save-Rollback {
    $p = Read-Proxy
    $dir = Split-Path -Parent $rollbackFile
    if (!(Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    [ordered]@{ SavedAt = (Get-Date).ToString('o'); ProxyEnable = $p.Enable; ProxyServer = $p.Server; ProxyOverride = $p.Override } |
        ConvertTo-Json | Set-Content -LiteralPath $rollbackFile -Encoding UTF8
}

function Refresh-WinInet { }

function Restart-Target {
    $appId = 'Microsoft.MicrosoftStickyNotes_8wekyb3d8bbwe!App'
    Get-Process -Name Microsoft.Notes -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    Start-Process explorer.exe ('shell:AppsFolder' + $appId)
}

$p = Read-Proxy
if ($CheckOnly) {
    [pscustomobject]@{ Proxy = if ($p.Enable -eq 0) { 'OFF' } else { 'ON -> ' + $p.Server }; ProxyOverride = $p.Override; Rollback = $rollbackFile } | Format-List
    exit 0
}

if ($ApplyDomains) {
    Save-Rollback
    $items = @($p.Override -split ';' | Where-Object { $_.Trim() })
    foreach ($item in ($Domains -split '[,;\s]+' | Where-Object { $_.Trim() })) {
        if ($items -notcontains $item) { $items += $item }
    }
    Set-ItemProperty -LiteralPath $reg -Name ProxyOverride -Value ($items -join ';')
    Refresh-WinInet
}
if ($BroadFallback) {
    Save-Rollback
    Set-ItemProperty -LiteralPath $reg -Name ProxyEnable -Value 0
    Refresh-WinInet
}
if ($RestartTarget) { Restart-Target }
Read-Proxy | Format-List
