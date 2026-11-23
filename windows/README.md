# AK400 DIGITAL on Windows

Target: Windows 11 26H2 x64, with DeepCool AK400 DIGITAL USB `3633:0001`.
The Windows code uses WinUSB to send the Linux driver's 64-byte reports directly to
interrupt OUT endpoint `0x01`. All display traffic uses the OUT endpoint.
Hardware validation on Windows is pending.

## Build

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

The probe sends a single frame and exits. Close any running copy of this program before using the probe. To restore the original driver binding later, use **Roll Back Driver** in Device Manager if available. Otherwise, uninstall the device and remove the generated WinUSB driver package for this device, then rescan for hardware changes.

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

Use `--no-intro` to skip the bar animation. Use `--interval` to change the sampling interval from the default one second. The program retries after USB disconnects and periodically refreshes unchanged frames so the display can recover from a reset.

## Start at boot

After the foreground check, run these commands from an elevated PowerShell:

```powershell
.\windows\Install-Ak400Display.ps1 -PublishDirectory "$env:TEMP\ak400-win-x64" -Mode all
Start-ScheduledTask -TaskName 'AK400 DIGITAL Display'
```

The installer copies the app into Program Files and registers a startup task under the local SYSTEM account. Windows starts it before sign-in, including when nobody logs in. The task logs to `%ProgramFiles%\AK400 Display\display.log`.

To remove the task and installed app, run `.\windows\Uninstall-Ak400Display.ps1` from an elevated PowerShell. The WinUSB binding and PawnIO are managed separately.
