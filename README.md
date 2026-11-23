# AK400 DIGITAL — Linux and Windows

Display controller for the **DeepCool AK400 DIGITAL** CPU cooler, USB **`3633:0001`**.
Shows CPU temperature and usage, plus NVIDIA GPU temperature and usage. Both
platform implementations are available in `main` and support background startup.

[Русская инструкция](#по-русски) · [Windows troubleshooting](windows/README.md)

## Platforms and requirements

| Platform | Implementation | Background startup | Requirements |
|---|---|---|---|
| Linux | Python 3 standard library, usbfs | systemd service | systemd/udev; root for installation; NVIDIA driver/library for GPU telemetry |
| Windows 11 x64 | .NET 10, WinUSB, LibreHardwareMonitor | Non-interactive SYSTEM scheduled task | WinUSB binding; PawnIO and elevated access for CPU temperature; NVIDIA driver for GPU telemetry |

A self-contained Windows build includes its .NET runtime; the SDK is only needed
to build from source. Linux NVIDIA readings use `libnvidia-ml` when available.

Hardware validation is limited to AK400 DIGITAL. Linux was tested on CachyOS
(Arch-based). Windows was tested on Windows 11 x64 build 26300, Ryzen 7 5700X, and
GeForce RTX 4060: display probe, CPU/GPU telemetry, GPU engine aggregation, and a
non-interactive SYSTEM task. Other AK DIGITAL models are unverified.

## Display modes

| Mode | Display |
|---|---|
| `temp` | CPU temperature |
| `usage` | CPU usage |
| `auto` | CPU temperature and usage, alternating every 4 seconds |
| `all` | CPU temperature → CPU usage → GPU temperature → GPU usage, when NVIDIA data is available |

`all` is the default. A brief `---` separates the CPU and GPU phases. The bar shows
usage of the current device. Temperature digits blink at 90 °C and above. A short
bar animation plays at startup; `--no-intro` skips it.

Compare **% with %** and **°C with °C**. The cooler has no separate GPU label.
Windows GPU usage is the **busiest WDDM/D3D engine** on the selected NVIDIA adapter,
including video encode, decode, and copy engines. This follows Task Manager's
aggregation. If Windows engine data is absent, the app falls back to NVIDIA
`GPU Core` load. Sampling times and rounding can cause differences. Linux uses
NVML utilization, which is a different measurement.

## Linux installation

```sh
git clone https://github.com/DraimCiDo/ak400-digital-linux.git
cd ak400-digital-linux
sudo ./install.sh                 # all mode; starts now and at boot
# Or choose a mode:
sudo ./install.sh --mode temp     # temp | usage | auto | all
```

Check status and logs:

```sh
systemctl status ak400-display
journalctl -u ak400-display -f
```

The installer places the program in `/usr/local/bin/ak400-display`, installs its
udev rule, and enables `ak400-display.service`. CPU temperature comes from hwmon
(`k10temp`, `coretemp`, `zenpower`); NVIDIA requires a driver exposing `libnvidia-ml`.

Stop the service before a manual run so only one program owns the display:

```sh
sudo systemctl stop ak400-display
sudo /usr/local/bin/ak400-display all 1
# After stopping the manual run with Ctrl+C:
sudo systemctl start ak400-display
```

Use `sudo systemctl edit --full ak400-display` to change the mode, interval, or add
`--no-intro` to `ExecStart`. Then run `sudo systemctl daemon-reload` and
`sudo systemctl restart ak400-display`.

Remove with `sudo ./uninstall.sh`. Replug the cooler or reboot to let `usbhid` bind again.

## Windows installation

1. Build from the repository root with the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0):

   ```powershell
   dotnet publish .\windows\Ak400Display.csproj -c Release -r win-x64 --self-contained true -o .\publish\win-x64
   ```

2. Close the official DeepCool display utility. Run [Zadig](https://zadig.akeo.ie/)
   as administrator, enable **Options → List All Devices**, and select USB ID
   **3633:0001** (Interface 0 if several interfaces are listed). Replace its driver
   with **WinUSB**. Zadig is a one-time setup tool; it need not remain open.

3. Stop any running app instance, then test:

   ```powershell
   .\publish\win-x64\Ak400Display.exe --probe
   ```

   The display should show **42 °C**. If Windows reports Code 10 after changing
   drivers, restart the AK400 device or reboot, then repeat the probe.

4. Install [PawnIO](https://pawnio.eu/). Open **PowerShell as administrator** and run:

   ```powershell
   .\publish\win-x64\Ak400Display.exe --mode all
   ```

   Restart the app after installing PawnIO. CPU temperature needs elevated access;
   inaccessible sensors may return missing or invalid readings. CPU usage does
   not require PawnIO. NVIDIA's driver supplies GPU telemetry.

5. Stop the foreground run with **Ctrl+C**. In elevated PowerShell, install and start:

   ```powershell
   .\windows\Install-Ak400Display.ps1 -PublishDirectory .\publish\win-x64 -Mode all
   Start-ScheduledTask -TaskName 'AK400 DIGITAL Display'
   ```

The app is copied to `%ProgramFiles%\AK400 Display\app`. The task runs as **SYSTEM
at Windows startup**, including before sign-in, in a non-interactive session:
**no visible console and no recurring UAC prompt**. It has no runtime limit;
failure restart and missed-start recovery are enabled. The app retries USB and
rechecks NVIDIA availability if its driver was not ready at boot.

Inspect from elevated PowerShell:

```powershell
Get-ScheduledTask -TaskName 'AK400 DIGITAL Display'
Get-Content "$env:ProgramFiles\AK400 Display\display.log" -Tail 30
```

To update, publish a new build, rerun the installer, and start the task again.
Remove with `.\windows\Uninstall-Ak400Display.ps1` from elevated PowerShell.
WinUSB and PawnIO are managed separately. See the [Windows guide](windows/README.md)
for USB recovery, driver rollback, options, and troubleshooting.

## Why direct USB output is used

On the affected Linux unit, polling HID **IN** triggers firmware resets. Opening
`/dev/hidrawN` lets `usbhid` poll it. The kernel reports USB disconnects and
`can't set config #1, error -71`.

| Measured method on the affected Linux unit | Resets |
|---|---|
| hidraw held open | 6 in 40 seconds |
| hidraw open → write → close each update | 1 in 90 seconds |
| usbfs, usbhid detached, OUT-only updates | 2 in 15 minutes |

Linux detaches `usbhid` and uses usbfs; a udev rule prevents HID from reclaiming
the cooler. Windows binds WinUSB. Both send 64-byte frames directly to interrupt
**OUT endpoint `0x01`**, without polling IN. Frames are sent on changes and refreshed
periodically. Reconnects are retried; a faulty USB link can still reset. Reseating
the cable or trying another internal USB 2.0 header can help persistent link errors.

## Display protocol

Reverse-engineered 64-byte report: `[0x10, icon, bar, d1, d2, d3, alarm, 0 × 57]`.

| Field | Values |
|---|---|
| `icon` | `19` TEMP °C; `35` TEMP °F; `76` USAGE %; `170` startup animation; `255` all segments; other values blank the tested firmware's display |
| `bar` | `0`–`10` segments; larger values glitch |
| `d1`–`d3` | `0`–`9` digits; `10` blank; `12` dash; higher codes are not a per-segment interface |
| `alarm` | `0` off; `1` blink TEMP and digits; `2` hide TEMP and blink digits |

Feature reports 2–5 returned zeros on the investigated unit; IN produced no useful
data. [`tools/ak400-explore.py`](tools/ak400-explore.py) contains the investigation
commands (`--scan`, `--digits`, `--glyphs`).

## RGB lighting

The ARGB strips use the motherboard's **5 V ARGB header**, not the display's USB.
[OpenRGB](https://openrgb.org/) is an optional, separate lighting tool. On the tested
Gigabyte B550 / IT5702 setup, `D_LED2` has 9 LEDs: 1–4 on the bottom strip and 5–9
on the top strip. Hardware effects worked better than per-LED Direct mode there.

On Linux, `sudo ./install.sh --rgb` also installs `ak400-rgb.service` to apply
Color Cycle at boot when OpenRGB is installed. Adjust its device/mode arguments
for your motherboard. The Windows display installer does not configure RGB.

## Development checks

```powershell
dotnet run --project .\tests\windows\GpuLoadChecks.csproj -c Release
```

Checks cover GPU engine selection, unrelated sensor exclusion, idle/invalid values,
saturation, absent GPU data, and fallback. Physical behavior needs hardware validation.

## По-русски

Проект управляет дисплеем **DeepCool AK400 DIGITAL** (`3633:0001`) в **Linux и
Windows 11 x64**. Обе версии находятся в `main`. Доступны температура и загрузка
CPU, а также температура и загрузка NVIDIA GPU.

### Linux

```sh
git clone https://github.com/DraimCiDo/ak400-digital-linux.git
cd ak400-digital-linux
sudo ./install.sh --mode all
```

Сервис запускается сразу и при включении ПК. Проверка: `systemctl status ak400-display`;
журнал: `journalctl -u ak400-display -f`. Удаление: `sudo ./uninstall.sh`.
Нужны Python 3, systemd/udev и права root; для NVIDIA — драйвер с `libnvidia-ml`.

### Windows

1. Собери проект через .NET 10 SDK командой из раздела Windows выше.
2. В Zadig выбери **3633:0001 → WinUSB → Replace Driver**. Это разовая настройка;
   держать Zadig запущенным не нужно. Проверь `--probe`: на дисплее должно появиться
   **42 °C**. При коде 10 перезапусти устройство или Windows.
3. Установи PawnIO и перезапусти приложение **от администратора**, иначе температура
   CPU может отсутствовать или быть неверной. Загрузка CPU PawnIO не требует.
4. Проверь `--mode all`, останови через Ctrl+C и настрой автозапуск из PowerShell
   **от администратора**:

   ```powershell
   .\windows\Install-Ak400Display.ps1 -PublishDirectory .\publish\win-x64 -Mode all
   Start-ScheduledTask -TaskName 'AK400 DIGITAL Display'
   ```

Приложение копируется в `%ProgramFiles%\AK400 Display\app` и запускается при старте
Windows от SYSTEM, до входа пользователя, **без открытой консоли** и повторных
запросов UAC. Журнал: `%ProgramFiles%\AK400 Display\display.log`.
Удаление: `.\windows\Uninstall-Ak400Display.ps1` от администратора; это не удаляет
PawnIO и не возвращает исходный USB-драйвер.

Режимы: `temp` — температура CPU; `usage` — загрузка CPU; `auto` — чередование CPU;
`all` — CPU и NVIDIA GPU. Фазы сменяются каждые 4 секунды. Сравнивай **°C с °C**, а
**% с %**. В Windows процент GPU берётся по наиболее загруженному движку Windows,
как в Диспетчере задач, включая видеокодировщик; при недоступности этих данных
используется `GPU Core`. Небольшая разница возможна из-за времени обновления.

[Подробная настройка Windows и устранение ошибок](windows/README.md).
Подсветка ARGB управляется отдельно через материнскую плату, а не через USB дисплея.

## License

MIT. See [LICENSE](LICENSE). Bundled Windows dependencies retain their own licenses.
