<#
.SYNOPSIS
    Lists every Wi-Fi radio in range, grouped by network, with its band and channel.

.DESCRIPTION
    A no-install companion to BandPilot. It parses "netsh wlan show networks
    mode=bssid" so you can see which bands a network is offering and which
    access point you are currently on.

    This script only reports. Pinning the connection to a specific access point
    needs the Native Wifi API, which netsh does not expose - that is what the
    BandPilot application is for.

.EXAMPLE
    .\Show-WifiBands.ps1

.EXAMPLE
    .\Show-WifiBands.ps1 -Ssid "Red Roof"
    Shows only the radios belonging to that network.
#>

[CmdletBinding()]
param(
    [string] $Ssid,
    [switch] $NoColour
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-BandFromChannel {
    param([int] $Channel)
    if ($Channel -ge 1   -and $Channel -le 14)  { return '2.4 GHz' }
    if ($Channel -ge 32  -and $Channel -le 177) { return '5 GHz' }
    if ($Channel -ge 181)                       { return '6 GHz' }
    return 'unknown'
}

# netsh reports 6 GHz channels in a range that overlaps 5 GHz numbering on some
# driver versions. When the band column is present in the output we trust it.
function Get-Band {
    param([int] $Channel, [string] $BandText)
    if ($BandText) {
        if ($BandText -match '6')   { return '6 GHz' }
        if ($BandText -match '5')   { return '5 GHz' }
        if ($BandText -match '2\.4'){ return '2.4 GHz' }
    }
    return Get-BandFromChannel -Channel $Channel
}

Write-Host ''
Write-Host 'Scanning...' -ForegroundColor DarkGray

$raw = netsh wlan show networks mode=bssid 2>&1 | Out-String
if ($raw -match 'not running|no wireless interface|not have') {
    Write-Host 'No wireless interface available, or the WLAN AutoConfig service is stopped.' -ForegroundColor Red
    return
}

# Which AP are we on right now?
$currentBssid = $null
$currentSsid  = $null
$iface = netsh wlan show interfaces 2>&1 | Out-String
if ($iface -match '(?m)^\s*BSSID\s*:\s*(.+)$')  { $currentBssid = $Matches[1].Trim() }
if ($iface -match '(?m)^\s*SSID\s*:\s*(.+)$')   { $currentSsid  = $Matches[1].Trim() }

$networks = @()
$current  = $null
$radio    = $null

foreach ($line in ($raw -split "`r?`n")) {

    if ($line -match '^\s*SSID\s+\d+\s*:\s*(.*)$') {
        if ($current) { $networks += $current }
        $name = $Matches[1].Trim()
        if ([string]::IsNullOrWhiteSpace($name)) { $name = '(hidden)' }
        $current = [pscustomobject]@{ Ssid = $name; Radios = @() }
        $radio = $null
        continue
    }

    if (-not $current) { continue }

    if ($line -match '^\s*BSSID\s+\d+\s*:\s*(.+)$') {
        $radio = [pscustomobject]@{
            Bssid   = $Matches[1].Trim()
            Signal  = 0
            Channel = 0
            BandRaw = ''
            Radio   = ''
        }
        $current.Radios += $radio
        continue
    }

    if (-not $radio) { continue }

    if ($line -match '^\s*Signal\s*:\s*(\d+)%')        { $radio.Signal  = [int]$Matches[1]; continue }
    if ($line -match '^\s*Channel\s*:\s*(\d+)')        { $radio.Channel = [int]$Matches[1]; continue }
    if ($line -match '^\s*Band\s*:\s*(.+)$')           { $radio.BandRaw = $Matches[1].Trim(); continue }
    if ($line -match '^\s*Radio type\s*:\s*(.+)$')     { $radio.Radio   = $Matches[1].Trim(); continue }
}
if ($current) { $networks += $current }

if ($Ssid) {
    $networks = $networks | Where-Object { $_.Ssid -like "*$Ssid*" }
}

if (-not $networks -or $networks.Count -eq 0) {
    Write-Host 'No networks found.' -ForegroundColor Yellow
    return
}

# Networks offering the most radios first - those are the ones where choosing
# actually matters.
$networks = $networks | Sort-Object -Property @{ Expression = { $_.Radios.Count } } -Descending

Write-Host ''
foreach ($n in $networks) {

    $isCurrent = ($currentSsid -and $n.Ssid -eq $currentSsid)
    $suffix = if ($isCurrent) { '  <- connected' } else { '' }

    $header = '{0}   ({1} radio{2}){3}' -f $n.Ssid, $n.Radios.Count,
              $(if ($n.Radios.Count -eq 1) { '' } else { 's' }), $suffix

    if ($NoColour) { Write-Host $header }
    else { Write-Host $header -ForegroundColor $(if ($isCurrent) { 'Green' } else { 'White' }) }

    $rows = foreach ($r in ($n.Radios | Sort-Object Signal -Descending)) {
        $band = Get-Band -Channel $r.Channel -BandText $r.BandRaw
        $here = if ($currentBssid -and $r.Bssid -ieq $currentBssid) { '*' } else { ' ' }
        [pscustomobject]@{
            ' '       = $here
            'Band'    = $band
            'Ch'      = $r.Channel
            'Signal'  = '{0,3}%' -f $r.Signal
            'Type'    = $r.Radio
            'BSSID'   = $r.Bssid
        }
    }

    $rows | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
}

Write-Host 'A "*" marks the access point you are currently connected to.' -ForegroundColor DarkGray
Write-Host 'To switch to a different band or AP, use the BandPilot application.' -ForegroundColor DarkGray
Write-Host ''
