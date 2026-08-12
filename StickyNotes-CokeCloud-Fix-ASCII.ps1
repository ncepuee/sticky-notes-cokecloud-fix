param([switch]$CheckOnly)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Reg = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'
$LogDir = Join-Path $env:LOCALAPPDATA 'Packages\Microsoft.MicrosoftStickyNotes_8wekyb3d8bbwe\LocalState\DiagOutputDir'
$PkgName = 'Microsoft.MicrosoftStickyNotes'
$AppId = 'Microsoft.MicrosoftStickyNotes_8wekyb3d8bbwe!App'
$CokeState = Join-Path $env:APPDATA 'cokecloud\vortex.json'
$DataDir = Join-Path $env:LOCALAPPDATA 'StickyNotes-CokeCloud-Fix'
$Rollback = Join-Path $DataDir 'rollback-state.json'

function Get-ProxyState {
    $p = Get-ItemProperty -LiteralPath $Reg
    [pscustomobject]@{
        Enable = [int]$p.ProxyEnable
        Server = [string]$p.ProxyServer
        Override = [string]$p.ProxyOverride
    }
}

function Get-SafeJsonField([string]$Text, [string]$Name) {
    $m = [regex]::Match($Text, ('"' + [regex]::Escape($Name) + '"\s*:\s*(?:"([^"]*)"|(true|false|-?\d+(?:\.\d+)?))'))
    if (!$m.Success) { return '' }
    if ($m.Groups[1].Success) { return $m.Groups[1].Value }
    return $m.Groups[2].Value
}

function Get-CokeState {
    $o = [ordered]@{
        Installed = $false
        Mode = ''
        Port = ''
        Connected = ''
        Tun = ''
        Service = ''
        Processes = @(Get-Process -Name CokeCloud -ErrorAction SilentlyContinue).Count
    }
    if (Test-Path -LiteralPath $CokeState) {
        $t = [IO.File]::ReadAllText($CokeState)
        $o.Mode = Get-SafeJsonField $t 'proxy_mode'
        $o.Port = Get-SafeJsonField $t 'proxy_port'
        $o.Connected = Get-SafeJsonField $t 'connected'
        $o.Tun = Get-SafeJsonField $t 'tun'
        $o.Service = Get-SafeJsonField $t 'service_mode'
    }
    [pscustomobject]$o
}

function Get-NotesState {
    $o = [ordered]@{
        Installed = $false
        Version = ''
        Log = ''
        LogTime = ''
        Session = ''
        Opened = ''
        Updated = ''
        Failure = ''
        Code = ''
        Events = @()
    }
    try {
        $p = Get-AppxPackage -Name $PkgName -ErrorAction Stop | Select-Object -First 1
        if ($p) { $o.Installed = $true; $o.Version = [string]$p.Version }
    } catch {}
    if (!(Test-Path -LiteralPath $LogDir)) { return [pscustomobject]$o }
    $f = Get-ChildItem -LiteralPath $LogDir -Filter '*.txt' -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (!$f) { return [pscustomobject]$o }

    $o.Log = $f.FullName
    $o.LogTime = $f.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')
    $events = @()
    foreach ($line in Get-Content -LiteralPath $f.FullName -ErrorAction SilentlyContinue) {
        try {
            $j = $line.Trim().TrimEnd(',') | ConvertFrom-Json
            $li = $j.LogItem
            if ($li) {
                $events += [pscustomobject]@{
                    Time = [string]$j.Time
                    Namespace = [string]$li.Namespace
                    Event = [string]$li.EventName
                    Code = [string]$li.ErrorData.Code
                }
            }
        } catch {}
    }
    $o.Events = @($events | Select-Object -Last 12)
    $x = $events | Where-Object { $_.Event -eq 'SyncSessionStarted' } | Select-Object -Last 1
    if ($x) { $o.Session = $x.Time }
    $x = $events | Where-Object { $_.Event -eq 'RealTimeConnectionOpened' } | Select-Object -Last 1
    if ($x) { $o.Opened = $x.Time }
    $x = $events | Where-Object { $_.Event -eq 'NoteContentUpdated' } | Select-Object -Last 1
    if ($x) { $o.Updated = $x.Time }
    $x = $events | Where-Object { $_.Event -match 'Failed|Error' -or $_.Event -eq 'SyncRequestFailed' } | Select-Object -Last 1
    if ($x) { $o.Failure = $x.Time + ' ' + $x.Event; $o.Code = $x.Code }
    [pscustomobject]$o
}

function Save-Rollback {
    if (!(Test-Path -LiteralPath $DataDir)) { New-Item -ItemType Directory -Path $DataDir -Force | Out-Null }
    $p = Get-ProxyState
    [ordered]@{
        SavedAt = (Get-Date).ToString('o')
        ProxyEnable = $p.Enable
        ProxyServer = $p.Server
        ProxyOverride = $p.Override
    } | ConvertTo-Json | Set-Content -LiteralPath $Rollback -Encoding UTF8
}

function Restart-Notes {
    Get-Process -Name Microsoft.Notes -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    Start-Process -FilePath 'explorer.exe' -ArgumentList ('shell:AppsFolder\' + $AppId)
}

function Get-Summary($Proxy, $Coke, $Notes) {
    $proxyText = if ($Proxy.Enable -eq 0) { 'OFF (direct for Sticky Notes)' } else { 'ON -> ' + $Proxy.Server }
    $modeText = if ($Coke.Mode) { $Coke.Mode } else { 'unknown' }
    $connectedText = if ($Coke.Connected) { $Coke.Connected } else { 'unknown' }
    $versionText = if ($Notes.Version) { $Notes.Version } else { 'not detected' }
    $syncText = if ($Notes.Opened) { 'opened: ' + $Notes.Opened } elseif ($Notes.Failure) { 'failed: ' + $Notes.Failure + ' ' + $Notes.Code } else { 'no clear channel evidence' }
    $updatedText = if ($Notes.Updated) { $Notes.Updated } else { 'none' }
    [pscustomobject]@{
        Proxy = $proxyText
        CokeMode = $modeText
        CokeConnected = $connectedText
        CokeProcesses = $Coke.Processes
        NotesVersion = $versionText
        Sync = $syncText
        Updated = $updatedText
    }
}

if ($CheckOnly) {
    Get-Summary (Get-ProxyState) (Get-CokeState) (Get-NotesState) | Format-List
    exit 0
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$form = New-Object System.Windows.Forms.Form
$form.Text = 'Sticky Notes Sync Fix - CokeCloud'
$form.StartPosition = 'CenterScreen'
$form.Size = New-Object System.Drawing.Size(900, 650)
$form.MinimumSize = New-Object System.Drawing.Size(820, 580)
$form.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 9)
$form.BackColor = [System.Drawing.Color]::White

$header = New-Object System.Windows.Forms.Label
$header.Text = 'Windows Sticky Notes Sync Fix'
$header.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 16, [System.Drawing.FontStyle]::Bold)
$header.AutoSize = $true
$header.Location = New-Object System.Drawing.Point(20, 15)
$form.Controls.Add($header)

$subtitle = New-Object System.Windows.Forms.Label
$subtitle.Text = 'Recommended: CokeCloud can stay running; Windows system proxy stays OFF for Sticky Notes.'
$subtitle.AutoSize = $true
$subtitle.ForeColor = [System.Drawing.Color]::DimGray
$subtitle.Location = New-Object System.Drawing.Point(22, 50)
$form.Controls.Add($subtitle)

$group = New-Object System.Windows.Forms.GroupBox
$group.Text = 'Current status'
$group.Location = New-Object System.Drawing.Point(20, 78)
$group.Size = New-Object System.Drawing.Size(845, 185)
$form.Controls.Add($group)

$table = New-Object System.Windows.Forms.TableLayoutPanel
$table.Dock = [System.Windows.Forms.DockStyle]::Fill
$table.Padding = New-Object System.Windows.Forms.Padding(10)
$table.ColumnCount = 2
$table.RowCount = 7
$table.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::Absolute, 180)))
$table.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::Percent, 100)))
$group.Controls.Add($table)

$labels = @{}
$rows = @(
    @('Windows system proxy', 'Proxy'),
    @('CokeCloud mode', 'CokeMode'),
    @('CokeCloud connected', 'CokeConnected'),
    @('CokeCloud processes', 'CokeProcesses'),
    @('Sticky Notes version', 'NotesVersion'),
    @('Sync channel', 'Sync'),
    @('Last content update', 'Updated')
)
for ($i = 0; $i -lt $rows.Count; $i++) {
    $name = New-Object System.Windows.Forms.Label
    $name.Text = $rows[$i][0]
    $name.Dock = [System.Windows.Forms.DockStyle]::Fill
    $name.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 9, [System.Drawing.FontStyle]::Bold)
    $name.TextAlign = [System.Drawing.ContentAlignment]::MiddleLeft

    $value = New-Object System.Windows.Forms.Label
    $value.Text = '-'
    $value.Dock = [System.Windows.Forms.DockStyle]::Fill
    $value.TextAlign = [System.Drawing.ContentAlignment]::MiddleLeft
    $labels[$rows[$i][1]] = $value

    $table.Controls.Add($name, 0, $i)
    $table.Controls.Add($value, 1, $i)
}

$buttonPanel = New-Object System.Windows.Forms.FlowLayoutPanel
$buttonPanel.Location = New-Object System.Drawing.Point(20, 275)
$buttonPanel.Size = New-Object System.Drawing.Size(845, 90)
$form.Controls.Add($buttonPanel)

function New-Button([string]$Text, [int]$Width) {
    $b = New-Object System.Windows.Forms.Button
    $b.Text = $Text
    $b.Width = $Width
    $b.Height = 34
    $b.Margin = New-Object System.Windows.Forms.Padding(4)
    return $b
}

$checkButton = New-Button 'Refresh status' 110
$fixButton = New-Button 'One-click fix (recommended)' 190
$restartButton = New-Button 'Restart Sticky Notes' 145
$saveButton = New-Button 'Save rollback point' 140
$restoreButton = New-Button 'Restore proxy state' 140
$logsButton = New-Button 'Open log folder' 120
$buttonPanel.Controls.AddRange(@($checkButton, $fixButton, $restartButton, $saveButton, $restoreButton, $logsButton))

$logGroup = New-Object System.Windows.Forms.GroupBox
$logGroup.Text = 'Operation log / diagnostic summary'
$logGroup.Location = New-Object System.Drawing.Point(20, 375)
$logGroup.Size = New-Object System.Drawing.Size(845, 220)
$form.Controls.Add($logGroup)

$logBox = New-Object System.Windows.Forms.RichTextBox
$logBox.Dock = [System.Windows.Forms.DockStyle]::Fill
$logBox.ReadOnly = $true
$logBox.BackColor = [System.Drawing.Color]::White
$logBox.BorderStyle = [System.Windows.Forms.BorderStyle]::None
$logBox.Font = New-Object System.Drawing.Font('Consolas', 9)
$logGroup.Controls.Add($logBox)

function Write-Log([string]$Message) {
    $logBox.AppendText(('[' + (Get-Date -Format 'HH:mm:ss') + '] ' + $Message + [Environment]::NewLine))
    $logBox.ScrollToCaret()
}

function Refresh-Ui {
    try {
        $p = Get-ProxyState
        $c = Get-CokeState
        $n = Get-NotesState
        $s = Get-Summary $p $c $n
        foreach ($key in $labels.Keys) {
            $labels[$key].Text = [string]$s.$key
            $labels[$key].ForeColor = [System.Drawing.Color]::Black
        }
        if ($p.Enable -eq 0) { $labels['Proxy'].ForeColor = [System.Drawing.Color]::ForestGreen } else { $labels['Proxy'].ForeColor = [System.Drawing.Color]::DarkOrange }
        if ($n.Opened) { $labels['Sync'].ForeColor = [System.Drawing.Color]::ForestGreen } elseif ($n.Failure) { $labels['Sync'].ForeColor = [System.Drawing.Color]::Crimson } else { $labels['Sync'].ForeColor = [System.Drawing.Color]::DimGray }
        $logBox.Clear()
        Write-Log ('ProxyEnable=' + $p.Enable + '; ProxyServer=' + $p.Server)
        Write-Log ('CokeCloud mode=' + $c.Mode + '; connected=' + $c.Connected + '; processes=' + $c.Processes)
        if ($n.Log) { Write-Log ('Latest log=' + $n.Log) }
        foreach ($e in $n.Events) {
            $codeText = if ($e.Code) { ' code=' + $e.Code } else { '' }
            Write-Log ($e.Time + ' ' + $e.Namespace + '/' + $e.Event + $codeText)
        }
    } catch { Write-Log ('Refresh failed: ' + $_.Exception.Message) }
}

$checkButton.Add_Click({ Refresh-Ui })
$saveButton.Add_Click({
    try { Save-Rollback; Write-Log 'Rollback point saved.'; [System.Windows.Forms.MessageBox]::Show('Current proxy state was saved.', 'Done', 'OK', 'Information') | Out-Null }
    catch { [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Save failed', 'OK', 'Error') | Out-Null }
    Refresh-Ui
})
$fixButton.Add_Click({
    if ([System.Windows.Forms.MessageBox]::Show('This saves a rollback point, turns OFF the Windows system proxy, and restarts Sticky Notes. CokeCloud core will not be closed. Continue?', 'Confirm one-click fix', 'YesNo', 'Question') -ne 'Yes') { return }
    try { if (!(Test-Path -LiteralPath $Rollback)) { Save-Rollback }; Set-ItemProperty -LiteralPath $Reg -Name ProxyEnable -Value 0; Restart-Notes; Write-Log 'System proxy disabled and Sticky Notes restarted.' }
    catch { [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Fix failed', 'OK', 'Error') | Out-Null }
    Refresh-Ui
})
$restartButton.Add_Click({
    try { Restart-Notes; Write-Log 'Sticky Notes restarted.' } catch { Write-Log ('Restart failed: ' + $_.Exception.Message) }
    Refresh-Ui
})
$restoreButton.Add_Click({
    if (!(Test-Path -LiteralPath $Rollback)) { [System.Windows.Forms.MessageBox]::Show('No rollback point. Save one first.', 'Notice', 'OK', 'Information') | Out-Null; return }
    $b = Get-Content -LiteralPath $Rollback -Raw | ConvertFrom-Json
    if ([System.Windows.Forms.MessageBox]::Show('Restore the saved proxy state? This may turn the Windows system proxy back ON.', 'Confirm restore', 'YesNo', 'Warning') -ne 'Yes') { return }
    Set-ItemProperty -LiteralPath $Reg -Name ProxyEnable -Value ([int]$b.ProxyEnable)
    Set-ItemProperty -LiteralPath $Reg -Name ProxyServer -Value ([string]$b.ProxyServer)
    Set-ItemProperty -LiteralPath $Reg -Name ProxyOverride -Value ([string]$b.ProxyOverride)
    Write-Log ('Proxy state restored. ProxyEnable=' + $b.ProxyEnable)
    Refresh-Ui
})
$logsButton.Add_Click({ if (Test-Path $LogDir) { Start-Process -FilePath explorer.exe -ArgumentList $LogDir } })
$form.Add_Shown({ Refresh-Ui })
[System.Windows.Forms.Application]::Run($form)
