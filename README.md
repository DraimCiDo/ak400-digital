# ak400-digital-linux

Display driver for the **DeepCool AK400 DIGITAL** CPU cooler (USB `3633:0001`),
written for units whose display **keeps disconnecting / never works** with the official
software and with existing open-source drivers.

- **Linux** · Follow the [Linux installation instructions](#install).
- **Windows 11 x64** · See the [Windows guide](windows/README.md) for WinUSB, the display probe and boot startup.

*Русская версия — [ниже](#по-русски).*

## The problem

On the affected unit the cooler re-enumerates on USB every few seconds as soon as any
software talks to it the usual way. The kernel log fills with:

```
usb 1-8: USB disconnect, device number N
usb 1-8: can't set config #1, error -71
```

Investigation showed the trigger is **not** the data written to the display. The firmware
crashes when its HID **IN endpoint is polled**. `usbhid` starts polling it whenever
`/dev/hidrawN` is open, which is exactly what the official software, `hidapi`-based tools
and [deepcool-digital-linux](https://github.com/Nortank12/deepcool-digital-linux) do.

| Method | Resets |
|---|---|
| hidraw held open (official app / hidapi) | 6 in 40 s |
| hidraw open → write → close each update | 1 in 90 s |
| usbfs, usbhid detached, OUT endpoint only, write on change | 2 in 15 min (auto-recovers in ~1 s) |

## The fix

- `ak400-display` detaches `usbhid` and writes reports straight to interrupt OUT endpoint
  `0x01` via usbfs ioctls. It never touches the IN endpoint.
- It only writes when the picture changes, because the display latches the last frame.
- A udev rule keeps `usbhid` off the cooler permanently, so no other program can open it
  and crash it.
- It recovers automatically within about a second if the cooler still resets.

The Linux implementation uses the Python 3 standard library (NVIDIA GPU stats via `libnvidia-ml` if present).

## Install

```sh
git clone https://github.com/DraimCiDo/ak400-digital-linux
cd ak400-digital-linux
sudo ./install.sh                 # default mode: all
sudo ./install.sh --mode temp     # temp | usage | auto | all
```

| Mode | Shows |
|---|---|
| `temp` | CPU temperature |
| `usage` | CPU load |
| `auto` | CPU temp ↔ CPU load every 4 s |
| `all` | CPU temp → CPU load → `---` → GPU temp → GPU load (NVIDIA) |

The load bar always shows the load of the device currently on screen. Digits blink at ≥ 90 °C.
A bar-fill animation plays at startup; add `--no-intro` to `ExecStart` to disable it.

Uninstall: `sudo ./uninstall.sh`.

## Display protocol (reverse-engineered)

64-byte output report: `[0x10, icon, bar, d1, d2, d3, alarm, 0 × 57]`

| Byte | Values |
|---|---|
| icon | `19` TEMP °C · `35` TEMP °F · `76` USAGE % · `170` bar-fill animation · `255` all segments · anything else = screen off (all 0–255 scanned) |
| bar | `0`–`10` segments (> 10 glitches) |
| d1–d3 | `0`–`9` digits · `10` blank · `12` `-` · higher codes are junk glyphs (no per-segment control) |
| alarm | `0` off · `1` blink TEMP + digits · `2` hide TEMP, blink digits |

Feature reports 2–5 return all zeros; the IN endpoint never sends data.

`tools/ak400-explore.py` is the interactive tool used to map this (`--scan`, `--digits`, `--glyphs`).

## RGB lighting

The AK400 DIGITAL's ARGB strips are **not** controlled over the cooler's USB. They plug
into the motherboard's 5 V ARGB header. Use [OpenRGB](https://openrgb.org).
On a Gigabyte B550 (IT5702 / RGB Fusion 2) the cooler shows up on `D_LED2` as **9 LEDs**
(1–4 bottom strip, 5–9 top strip).

Use the controller's **hardware modes** (Static, Breathing, Color Cycle…). Per-LED Direct
mode left stale LEDs on this board. `sudo ./install.sh --rgb` installs `ak400-rgb.service`,
which applies Color Cycle at boot. Edit the `-m` / `-d` arguments for your board.

## Notes

- Hardware still matters: the remaining occasional resets with `error -71` point to the
  USB link. Reseating the cable or trying another internal USB 2.0 header can help.
- Only tested on one AK400 DIGITAL (firmware bcdDevice 2.00) on Arch-based Linux (CachyOS).
  Other DeepCool AK DIGITAL models use the same packet format but may not share the bug.

---

## По-русски

Драйвер дисплея кулера **DeepCool AK400 DIGITAL**. Ниже описана Linux-версия. Для Windows 11 см. [сборку, проверку дисплея и автозапуск](windows/README.md).

**Проблема:** на некоторых экземплярах дисплей не работает ни с официальным софтом, ни с
открытыми драйверами. Кулер переподключается по USB каждые несколько секунд. Причина: прошивка
падает, когда драйвер `usbhid` опрашивает его входной (IN) эндпоинт, а это происходит, пока
устройство открыто.

**Решение:** драйвер отвязывает `usbhid` и пишет напрямую в выходной эндпоинт через usbfs,
только когда картинка меняется. Правило udev не даёт `usbhid` снова захватить кулер.

**Установка:** `sudo ./install.sh` (режимы: `--mode temp|usage|auto|all`). Удаление: `sudo ./uninstall.sh`.

Подсветка кулера управляется не через USB, а через ARGB-разъём материнской платы (OpenRGB,
на Gigabyte B550 — зона `D_LED2`, 9 светодиодов). Лучше использовать аппаратные режимы платы.

## License

MIT, see [LICENSE](LICENSE).
