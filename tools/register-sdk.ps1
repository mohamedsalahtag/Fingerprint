<#
.SYNOPSIS
    Installs & registers the ZKTeco zkemkeeper COM SDK required by ZK Device Manager.

.DESCRIPTION
    The device operations use the zkemkeeper COM component. For Connect_Net to work, ALL nine
    SDK DLLs (zkemkeeper + the native siblings zkemsdk, commpro, comms, tcpcomm, usbcomm, rscomm,
    rscagent) must be present in the Windows SYSTEM folders — not just next to the app. Registering
    zkemkeeper alone, with siblings only in the app folder, makes the COM object create but
    Connect_Net fail (looks like an auth/network error but is really a missing-dependency load).

    This script copies the DLLs into System32 (x64) and SysWOW64 (x86) and registers zkemkeeper.dll
    with both regsvr32 builds. Re-run with -Unregister to remove.

.NOTES
    Run from an ELEVATED (Administrator) PowerShell:
        powershell -ExecutionPolicy Bypass -File tools\register-sdk.ps1
#>
[CmdletBinding()]
param(
    [string]$SdkPath = (Join-Path $PSScriptRoot '..\sdk'),
    [switch]$Unregister
)

$ErrorActionPreference = 'Stop'

$identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "Please run this script from an elevated (Administrator) PowerShell."
    exit 1
}

$SdkPath  = (Resolve-Path $SdkPath).Path
$dllNames = 'zkemkeeper.dll','zkemsdk.dll','commpro.dll','comms.dll','tcpcomm.dll','usbcomm.dll','rscomm.dll','rscagent.dll'
$sys32    = Join-Path $env:WINDIR 'System32'
$syswow   = Join-Path $env:WINDIR 'SysWOW64'
$reg64    = Join-Path $sys32  'regsvr32.exe'
$reg32    = Join-Path $syswow 'regsvr32.exe'

if ($Unregister) {
    if (Test-Path (Join-Path $sys32  'zkemkeeper.dll')) { & $reg64 /s /u (Join-Path $sys32  'zkemkeeper.dll') }
    if (Test-Path (Join-Path $syswow 'zkemkeeper.dll')) { & $reg32 /s /u (Join-Path $syswow 'zkemkeeper.dll') }
    Write-Host "Unregistered zkemkeeper.dll." -ForegroundColor Green
    exit 0
}

Write-Host "SDK source: $SdkPath"
foreach ($name in $dllNames) {
    $src = Join-Path $SdkPath $name
    if (-not (Test-Path $src)) { Write-Warning "missing $name in SDK folder"; continue }
    Copy-Item $src -Destination $sys32  -Force
    if (Test-Path $syswow) { Copy-Item $src -Destination $syswow -Force }
}
Write-Host "Copied SDK DLLs into System32$(if (Test-Path $syswow) {' and SysWOW64'})."

# Register in both apartments so either x64 or x86 hosts can bind it.
& $reg64 /s (Join-Path $sys32 'zkemkeeper.dll')
$ok64 = ($LASTEXITCODE -eq 0)
if (Test-Path $reg32) { & $reg32 /s (Join-Path $syswow 'zkemkeeper.dll'); $ok32 = ($LASTEXITCODE -eq 0) } else { $ok32 = $false }

if ($ok64 -or $ok32) {
    Write-Host ("zkemkeeper.dll registered (x64={0}, x86={1})." -f $ok64, $ok32) -ForegroundColor Green
    Write-Host "The app can now connect to devices."
} else {
    Write-Error "regsvr32 failed for both builds. Check that the DLLs copied and match the OS bitness."
}
