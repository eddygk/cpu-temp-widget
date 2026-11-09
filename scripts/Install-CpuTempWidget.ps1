<#
.SYNOPSIS
  Developer install: build from source, publish to %LOCALAPPDATA%\CpuTempWidget and start the widget at sign-in.
  (End users: use the installer from the Releases page instead.)

.DESCRIPTION
  Idempotent: safe to run repeatedly. Needs an elevated PowerShell (kernel driver + highest-privilege task).
    1. Builds native\RmTemp.dll with MSBuild (Visual Studio 2022 Build Tools, C++ workload; needs the AMD SDK headers).
    2. dotnet publish -> %LOCALAPPDATA%\CpuTempWidget\
    3. Registers the "CpuTempWidget" scheduled task via scripts\Register-StartupTask.ps1.

.PARAMETER Uninstall
  Remove the scheduled task and the published files (settings in %APPDATA%\CpuTempWidget are kept).

.PARAMETER NoStart
  Do not launch the widget at the end.
#>
[CmdletBinding()]
param(
    [switch]$Uninstall,
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'
$TaskName   = 'CpuTempWidget'
$Root       = Split-Path -Parent $PSScriptRoot
$InstallDir = Join-Path $env:LOCALAPPDATA 'CpuTempWidget'
$Exe        = Join-Path $InstallDir 'CpuTempWidget.exe'
$Register   = Join-Path $PSScriptRoot 'Register-StartupTask.ps1'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw 'Run this script from an elevated (Administrator) PowerShell.' }

function Stop-Widget {
    Get-Process -Name CpuTempWidget -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

function Find-MSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $path = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
                    -find 'MSBuild\Current\Bin\amd64\MSBuild.exe' | Select-Object -First 1
        if ($path) { return $path }
    }
    throw 'MSBuild with the C++ build tools was not found (install Visual Studio 2022 Build Tools, "Desktop development with C++").'
}

if ($Uninstall) {
    Stop-Widget
    & $Register -Remove
    if (Test-Path $InstallDir) {
        Remove-Item $InstallDir -Recurse -Force
        Write-Host "Removed $InstallDir"
    }
    return
}

# --- prerequisites ---------------------------------------------------------------------------------------------
if (-not (Test-Path 'HKLM:\SOFTWARE\AMD\RyzenMasterMonitoringSDK')) {
    throw 'AMD Ryzen Master Monitoring SDK 3.1.x is not installed (see README, "Install").'
}

# --- 1. native wrapper -----------------------------------------------------------------------------------------
Write-Host '== Building native wrapper'
& (Find-MSBuild) (Join-Path $Root 'native\RmTemp.vcxproj') /p:Configuration=Release /p:Platform=x64 /nologo /v:m /clp:'ErrorsOnly;Summary'
if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }

# --- 2. publish ------------------------------------------------------------------------------------------------
Write-Host '== Publishing app'
Stop-Widget
dotnet publish (Join-Path $Root 'app\CpuTempWidget.csproj') -c Release -p:Platform=x64 -o $InstallDir --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
Copy-Item (Join-Path $Root 'native\bin\x64\Release\RmTemp.dll') (Join-Path $InstallDir 'RmTemp.dll') -Force
if (-not (Test-Path $Exe)) { throw "Publish did not produce $Exe" }

# --- 3. start at sign-in ---------------------------------------------------------------------------------------
Write-Host '== Registering sign-in task'
& $Register -Exe $Exe

if (-not $NoStart) {
    Start-ScheduledTask -TaskName $TaskName
    Write-Host 'Widget started.'
}
