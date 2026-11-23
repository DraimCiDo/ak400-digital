# AK400 DIGITAL on Windows

Windows 11 x64, with DeepCool AK400 DIGITAL USB `3633:0001`.
The Windows code uses WinUSB to send the Linux driver's 64-byte reports directly to
interrupt OUT endpoint `0x01`. All display traffic uses the OUT endpoint.
Validated on Windows 11 build 26300, Ryzen 7 5700X, and GeForce RTX 4060: probe, CPU/GPU telemetry, GPU engine aggregation, and a background SYSTEM task. Other AK DIGITAL models are unverified.

## Build

Published self-contained output includes .NET; keep the complete folder together. The SDK is needed only for building.

From the repository root, using the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0):

```powershell
dotnet publish .\windows\Ak400Display.csproj -c Release -r win-x64 --self-contained true -o "$env:TEMP\ak400-win-x64"
```

## Bind WinUSB and check the display

1. Close the DeepCool display utility. In [Zadig](https://zadig.akeo.ie/), enable **Options → List All Devices**.
2. Verify USB ID **3633:0001**. If Zadig lists multiple interfaces for that ID, select **Interface 0**. Select **WinUSB** and install it for that entry.
3. Send a test frame. The display should show **42 °C**.

```powershell
& "$env:TEMP\ak400-win-x64\Ak400Display.exe" --probe
```

The probe sends a single frame and exits. Zadig is only needed once to bind WinUSB; it does not need to stay open. Close any running copy of this program before using the probe. To restore the original driver binding later, use **Roll Back Driver** in Device Manager if available. Otherwise, uninstall the device and remove the generated WinUSB driver package for this device, then rescan for hardware changes.

## Run in the foreground

For CPU package temperature, install [PawnIO](https://pawnio.eu/) and run from an elevated PowerShell. The app uses LibreHardwareMonitor for CPU temperature and NVIDIA GPU telemetry. CPU usage uses Windows system times and works without PawnIO. When CPU temperature is unavailable, the display shows dashes in its place.

```powershell
& "$env:TEMP\ak400-win-x64\Ak400Display.exe" --mode all
```

| Mode | Displays |
|---|---|
| `temp` | CPU temperature |
| `usage` | CPU usage |
| `auto` | CPU temperature and usage |
| `all` | CPU temperature and usage, then NVIDIA GPU temperature and usage when available |

Use `--no-intro` to skip the bar animation. Use `--interval` to change the sampling interval from the default one second. While connected, the app disables WinUSB idle autosuspend and resets the OUT pipe before one retry on a failed write. The program retries after USB disconnects and periodically refreshes unchanged frames so the display can recover from a reset.

GPU usage is the busiest Windows (WDDM/D3D) engine on the selected NVIDIA GPU,
matching Task Manager's aggregation rather than NVIDIA's `GPU Core` utilization.
This includes video encoding, decoding, and copy engines. Independently timed
samples can still differ. If Windows engine readings are unavailable, the app
falls back to `GPU Core` utilization.

Run the GPU aggregation regression checks from the repository root with
`dotnet run --project .\tests\windows\GpuLoadChecks.csproj -c Release`.

## Start at boot

After the foreground check, run these commands from an elevated PowerShell:

```powershell
.\windows\Install-Ak400Display.ps1 -PublishDirectory "$env:TEMP\ak400-win-x64" -Mode all
Start-ScheduledTask -TaskName 'AK400 DIGITAL Display'
```

The installer copies the app into Program Files and registers a startup task under the local SYSTEM account. Windows starts it before sign-in, including when nobody logs in. It runs with highest privileges in non-interactive session 0: no visible console and no recurring UAC prompt. The task has no runtime limit, with failure restart and missed-start recovery enabled. Its Hidden setting marks the task hidden; the SYSTEM session is what keeps its console off the user desktop. The task logs to `%ProgramFiles%\AK400 Display\display.log`.

To remove the task and installed app, run `.\windows\Uninstall-Ak400Display.ps1` from an elevated PowerShell. The WinUSB binding and PawnIO are managed separately.

## Troubleshooting

### WinUSB interface not found, Code 10, or USB write timeout

Zadig's success message confirms installation, not that the device started.
Inspect the actual PnP binding:

```powershell
$device = Get-PnpDevice -PresentOnly | Where-Object InstanceId -Like 'USB\VID_3633&PID_0001\*'
$device | Format-List Status,FriendlyName,InstanceId
$device | ForEach-Object {
    Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName DEVPKEY_Device_Service,DEVPKEY_Device_ProblemCode
}
```

Expect service `WinUSB`, status OK, and problem code 0. For `HidUsb`, bind WinUSB
to the correct device in Zadig. For Code 10 or a write timeout after changing
drivers, restart the AK400 only from elevated PowerShell:

```powershell
$device | ForEach-Object { pnputil /restart-device $_.InstanceId }
```

Retry the probe or task after restarting the device. Rebooting Windows is another
recovery step. For persistent timeouts, inspect the cable/internal USB header.

### CPU temperature is zero or missing

Install PawnIO and restart the foreground app **as administrator**. An instance
started before installation or elevation must be restarted. The background SYSTEM
task already has elevated access. An inaccessible sensor may return an invalid
zero instead of missing data; do not treat 0 °C as a real reading.

```powershell
Get-CimInstance Win32_SystemDriver -Filter "Name='PawnIO'" | Select-Object Name,State,Status
```

### GPU percentage differs from another tool

Compare the GPU phase marked **%**, not **°C**. CPU/GPU values alternate; the
cooler has no separate GPU label. This version selects the busiest Windows
engine, including video encoding/decoding, rather than only NVIDIA `GPU Core`.
Independent sampling periods can still cause small differences.

### Probe cannot open the device

Stop the scheduled task and any foreground instance before probing. WinUSB has
one write owner. Restart the task when manual checks are complete.

## Task status, updates, and removal

Use elevated PowerShell:

```powershell
Get-ScheduledTask -TaskName 'AK400 DIGITAL Display'
Get-ScheduledTaskInfo -TaskName 'AK400 DIGITAL Display'
Get-Content "$env:ProgramFiles\AK400 Display\display.log" -Tail 30
Stop-ScheduledTask -TaskName 'AK400 DIGITAL Display'
Start-ScheduledTask -TaskName 'AK400 DIGITAL Display'
```

`Running` is expected while the display is active. A running-task status
`0x41301` (267009) is not an application failure. Look for
`Connected to the AK400 DIGITAL over WinUSB.` and detected CPU/GPU readings in the log.

To update, publish the new version, rerun the installer, and start the task again.
The installer stops the scheduled instance before swapping the app files.
Remove with `.\windows\Uninstall-Ak400Display.ps1` from elevated PowerShell.
PawnIO and the WinUSB binding are managed separately. To restore HID, use Device
Manager's Roll Back Driver for the AK400 if available. Otherwise uninstall the
AK400 device and its generated WinUSB driver package, then rescan. Select the
AK400's package specifically.
