#Requires -Version 5.1
#Requires -RunAsAdministrator
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$taskName = 'AK400 DIGITAL Display'
$task = Get-ScheduledTask -TaskName $taskName -TaskPath '\' -ErrorAction SilentlyContinue
if ($task) {
    if ($task.State -eq 'Running') {
        Stop-ScheduledTask -TaskName $taskName -TaskPath '\'
    }
    Unregister-ScheduledTask -TaskName $taskName -TaskPath '\' -Confirm:$false
}

$installDirectory = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'AK400 Display'
if (Test-Path -LiteralPath (Join-Path $installDirectory 'app\Ak400Display.exe') -PathType Leaf) {
    Remove-Item -LiteralPath $installDirectory -Recurse -Force
}
