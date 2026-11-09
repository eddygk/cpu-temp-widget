<#
.SYNOPSIS
  Create (or remove) the "CpuTempWidget" scheduled task that starts the widget when the user signs in.

.DESCRIPTION
  The widget needs administrator rights (AMD's SDK loads a kernel driver). A plain Startup-folder shortcut would
  show a UAC prompt at every sign-in; a scheduled task with "run with highest privileges" does not.
  Used by the installer (installer\CpuTempWidget.iss) and by scripts\Install-CpuTempWidget.ps1. Idempotent.
  Run from an elevated PowerShell.

.PARAMETER Exe
  Full path of CpuTempWidget.exe.
.PARAMETER User
  The user who signs in (DOMAIN\name or just name). Defaults to the current user.
.PARAMETER Remove
  Delete the task instead of creating it.
#>
[CmdletBinding()]
param(
    [string]$Exe,
    [string]$User = "$env:USERDOMAIN\$env:USERNAME",
    [switch]$Remove
)

$ErrorActionPreference = 'Stop'
$TaskName = 'CpuTempWidget'

if ($Remove) {
    if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
        Write-Host "Removed scheduled task '$TaskName'."
    }
    return
}

if (-not $Exe -or -not (Test-Path -LiteralPath $Exe)) { throw "CpuTempWidget.exe not found: '$Exe'" }
if ($User -notmatch '\\') { $User = "$env:USERDOMAIN\$User" }     # setup passes the bare account name

$action    = New-ScheduledTaskAction -Execute $Exe -WorkingDirectory (Split-Path -Parent $Exe)
$trigger   = New-ScheduledTaskTrigger -AtLogOn -User $User
$principal = New-ScheduledTaskPrincipal -UserId $User -LogonType Interactive -RunLevel Highest
$settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
              -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -StartWhenAvailable
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal `
    -Settings $settings -Description 'CPU Temperature Widget: start at sign-in' -Force | Out-Null

$t = Get-ScheduledTask -TaskName $TaskName
Write-Host ("Task '{0}': state {1}, run level {2}, user {3}" -f $t.TaskName, $t.State, $t.Principal.RunLevel, $t.Principal.UserId)
