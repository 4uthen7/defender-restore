#requires -Version 5.1
<#
.SYNOPSIS
  Read-only Microsoft Defender health check for DefenderRestore.

.DESCRIPTION
  Prints engine status, preferences, service state, sc.exe configuration,
  Defender policies, and the Windows Security app registration.
  It does not change anything. Run it in PowerShell (admin recommended).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\tools\health-check.ps1
#>

[CmdletBinding()]
param(
    [switch]$NoColor
)

$ErrorActionPreference = 'SilentlyContinue'

function Write-Header([string]$Text) {
    Write-Host ''
    Write-Host ('== ' + $Text + ' ==') -ForegroundColor Cyan
}

function Write-Result([bool]$Ok, [string]$Text) {
    if ($NoColor) {
        if ($Ok) { Write-Host ('[OK] ' + $Text) } else { Write-Host ('[!!] ' + $Text) }
    }
    else {
        if ($Ok) { Write-Host ('[OK] ' + $Text) -ForegroundColor Green }
        else { Write-Host ('[!!] ' + $Text) -ForegroundColor Yellow }
    }
}

function Get-AdminState {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $p = New-Object Security.Principal.WindowsPrincipal($id)
    return $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

Write-Host 'DefenderRestore - health check (read-only)' -ForegroundColor White

$os = Get-CimInstance Win32_OperatingSystem
if ($os) {
    Write-Host ('OS:    ' + $os.Caption + '  Build ' + $os.BuildNumber)
}
Write-Host ('Admin: ' + (Get-AdminState))

# ---------------------------------------------------------------------------
Write-Header 'Engine (MSFT_MpComputerStatus)'
$mp = Get-MpComputerStatus
if ($mp) {
    $mp | Select-Object AntivirusEnabled, RealTimeProtectionEnabled, IsTamperProtected,
        AMRunningMode, AntivirusSignatureAge, AntivirusSignatureVersion | Format-List
    $engineOk = [bool]$mp.AntivirusEnabled -and [bool]$mp.RealTimeProtectionEnabled
    Write-Result $engineOk ('AntivirusEnabled=' + $mp.AntivirusEnabled + ', RealTimeProtectionEnabled=' + $mp.RealTimeProtectionEnabled)
}
else {
    Write-Result $false 'MSFT_MpComputerStatus is unavailable. WinDefend may be stopped or disabled.'
}

# ---------------------------------------------------------------------------
Write-Header 'Preferences (Get-MpPreference)'
$pref = Get-MpPreference
if ($pref) {
    $pref | Select-Object DisableRealtimeMonitoring, DisableIOAVProtection,
        DisableBehaviorMonitoring, DisableScriptScanning | Format-List
    $prefOk = -not $pref.DisableRealtimeMonitoring -and -not $pref.DisableIOAVProtection
    Write-Result $prefOk 'DisableRealtimeMonitoring / DisableIOAVProtection should both be False.'
}
else {
    Write-Result $false 'Get-MpPreference failed (service may be stopped).'
}

# ---------------------------------------------------------------------------
Write-Header 'Services'
$names = @('WinDefend', 'WdNisSvc', 'SecurityHealthService', 'wscsvc', 'mpssvc', 'Sense')
$services = Get-Service -Name $names -ErrorAction SilentlyContinue
if ($services) {
    $services | Select-Object Name, Status, StartType | Format-Table -AutoSize
}

$serviceProblems = @()
foreach ($name in @('WinDefend', 'wscsvc', 'SecurityHealthService')) {
    $svc = $services | Where-Object { $_.Name -eq $name }
    if (-not $svc) {
        $serviceProblems += ($name + ': missing')
    }
    elseif ($svc.StartType -eq 'Disabled') {
        $serviceProblems += ($name + ': DISABLED')
    }
    elseif ($svc.Status -ne 'Running') {
        $serviceProblems += ($name + ': ' + $svc.Status + ' / ' + $svc.StartType)
    }
}

if ($serviceProblems.Count -eq 0) {
    Write-Result $true 'WinDefend, wscsvc and SecurityHealthService are running.'
}
else {
    Write-Result $false ('Service problem: ' + ($serviceProblems -join '; '))
}

# ---------------------------------------------------------------------------
Write-Header 'Service configuration (sc.exe qc)'
foreach ($name in @('WinDefend', 'SecurityHealthService', 'wscsvc')) {
    Write-Host ('-- ' + $name + ' --')
    $qc = sc.exe qc $name 2>$null
    if ($qc) {
        $qc | Select-String -Pattern 'SERVICE_NAME|START_TYPE|DEPENDENCIES|BINARY_PATH_NAME' |
            ForEach-Object { Write-Host ('  ' + $_.Line.Trim()) }
    }
    else {
        Write-Host '  (not found)'
    }
}

# ---------------------------------------------------------------------------
Write-Header 'Defender policies'
$policyRoot = 'HKLM\SOFTWARE\Policies\Microsoft\Windows Defender'
$policy = reg query $policyRoot /s 2>$null
if (-not $policy) {
    Write-Result $true 'No Defender policy key / no disable values.'
}
else {
    $disableLines = $policy | Select-String -Pattern 'DisableAntiSpyware|DisableAntiVirus|DisableRealtimeMonitoring|DisableBehaviorMonitoring|DisableIOAVProtection|DisableScriptScanning'
    if ($disableLines) {
        $disableLines | ForEach-Object { Write-Host ('  ' + $_.Line) }
        Write-Result $false 'Defender-disabling policy values were found.'
    }
    else {
        Write-Result $true 'Policy key exists, but no known disable values were found.'
    }
}

# ---------------------------------------------------------------------------
Write-Header 'Windows Security app'
$appx = Get-AppxPackage -Name '*SecHealthUI*'
if ($appx) {
    $appx | Select-Object Name, Version, Status | Format-Table -AutoSize
    $appxOk = $true
    foreach ($p in $appx) {
        if ($p.Status -ne 'Ok') { $appxOk = $false }
    }
    Write-Result $appxOk 'Microsoft.SecHealthUI is installed.'
}
else {
    Write-Result $false 'Microsoft.SecHealthUI was not found. The Settings page may show only an "Open Windows Security" button.'
}

# ---------------------------------------------------------------------------
Write-Header 'Firewall'
$fw = Get-NetFirewallProfile
if ($fw) {
    $fw | Select-Object Name, Enabled | Format-Table -AutoSize
    $fwOk = $true
    foreach ($p in $fw) { if (-not $p.Enabled) { $fwOk = $false } }
    Write-Result $fwOk 'All firewall profiles should be enabled.'
}
else {
    Write-Result $false 'Get-NetFirewallProfile failed.'
}

# ---------------------------------------------------------------------------
Write-Header 'Next steps'
Write-Host 'If everything above is [OK], no action is needed.'
Write-Host 'If something is [!!]:'
Write-Host '  1. Run DefenderRestore.exe as administrator and click Repair.'
Write-Host '  2. If a service is DISABLED, set it with sc.exe config and reboot.'
Write-Host '  3. If SecurityHealthService returns 1058 after the start type is 2/3,'
Write-Host '     reboot first, then run the tool again (SCM caches the old state).'
Write-Host ''
Write-Host 'Full guide: README.md (English) / README.ja.md (Japanese)'
