#Requires -Version 5.1
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PublishDirectory,

    [ValidateSet('temp', 'usage', 'auto', 'all')]
    [string]$Mode = 'all',

    [ValidateRange(0.25, 60)]
    [double]$Interval = 1,

    [switch]$NoIntro
)

$ErrorActionPreference = 'Stop'
$taskName = 'AK400 DIGITAL Display'
$programFiles = [Environment]::GetFolderPath('ProgramFiles')
$installDirectory = Join-Path $programFiles 'AK400 Display'
$source = (Get-Item -LiteralPath $PublishDirectory).FullName
$appDirectory = Join-Path $installDirectory 'app'
$executable = Join-Path $appDirectory 'Ak400Display.exe'
$log = Join-Path $installDirectory 'display.log'

if ($source -eq $appDirectory -or
    -not (Test-Path -LiteralPath (Join-Path $source 'Ak400Display.exe') -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $source 'coreclr.dll') -PathType Leaf)) {
    throw 'Point -PublishDirectory to the self-contained win-x64 publish output.'
}

$intervalText = $Interval.ToString([Globalization.CultureInfo]::InvariantCulture)
$arguments = '--mode {0} --interval {1} --log-file "{2}"' -f $Mode.ToLowerInvariant(), $intervalText, $log
if ($NoIntro) {
    $arguments += ' --no-intro'
}

$action = New-ScheduledTaskAction -Execute $executable -Argument $arguments -WorkingDirectory $appDirectory
$trigger = New-ScheduledTaskTrigger -AtStartup
$principal = New-ScheduledTaskPrincipal -UserId 'S-1-5-18' -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew `
    -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries

# Copy the complete publish output before touching the running installation.
New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
$deploymentId = [Guid]::NewGuid().ToString('N')
$staging = Join-Path $installDirectory "staging-$deploymentId"
$backup = Join-Path $installDirectory "backup-$deploymentId"
$activated = $false

try {
    New-Item -ItemType Directory -Path $staging | Out-Null
    Get-ChildItem -LiteralPath $source -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $staging -Recurse -Force
    }

    if ((Get-ScheduledTask -TaskName $taskName -TaskPath '\' -ErrorAction SilentlyContinue).State -eq 'Running') {
        Stop-ScheduledTask -TaskName $taskName -TaskPath '\'
        for ($attempt = 0; $attempt -lt 20; $attempt++) {
            if ((Get-ScheduledTask -TaskName $taskName -TaskPath '\').State -ne 'Running') {
                break
            }
            Start-Sleep -Milliseconds 250
        }
        if ((Get-ScheduledTask -TaskName $taskName -TaskPath '\').State -eq 'Running') {
            throw "Could not stop the existing task '$taskName'."
        }
    }

    if (Test-Path -LiteralPath $appDirectory) {
        Move-Item -LiteralPath $appDirectory -Destination $backup
    }
    Move-Item -LiteralPath $staging -Destination $appDirectory
    $activated = $true
    Register-ScheduledTask -TaskName $taskName -TaskPath '\' -Action $action -Trigger $trigger `
        -Principal $principal -Settings $settings -Description 'Updates the DeepCool AK400 DIGITAL display.' -Force | Out-Null
} catch {
    if ($activated) {
        Remove-Item -LiteralPath $appDirectory -Recurse -Force
    }
    if (Test-Path -LiteralPath $backup) {
        Move-Item -LiteralPath $backup -Destination $appDirectory
    }
    throw
} finally {
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
}

if (Test-Path -LiteralPath $backup) {
    Remove-Item -LiteralPath $backup -Recurse -Force
}

Write-Output "Registered '$taskName' for the next Windows startup."
Write-Output "To start it now, run: Start-ScheduledTask -TaskName '$taskName'"
Write-Output "Log: $log"
