#!/bin/sh
# Install the DeepCool AK400 DIGITAL display driver.
#   sudo ./install.sh                     # display driver only
#   sudo ./install.sh --mode temp         # display mode: temp | usage | auto | all (default: all)
#   sudo ./install.sh --rgb               # also install the OpenRGB boot effect unit
set -e
cd "$(dirname "$0")"
[ "$(id -u)" -eq 0 ] || { echo "Run as root: sudo $0 $*"; exit 1; }

MODE=all
RGB=0
while [ $# -gt 0 ]; do
    case "$1" in
        --mode) MODE="$2"; shift ;;
        --rgb)  RGB=1 ;;
        *) echo "Unknown option: $1"; exit 1 ;;
    esac
    shift
done
case "$MODE" in temp|usage|auto|all) ;; *) echo "Bad mode: $MODE"; exit 1 ;; esac

install -m755 ak400-display /usr/local/bin/ak400-display
install -m644 udev/99-ak400-nohid.rules /etc/udev/rules.d/
sed "s#ak400-display all 1#ak400-display $MODE 1#" systemd/ak400-display.service \
    > /etc/systemd/system/ak400-display.service
udevadm control --reload
# Detach usbhid from an already-connected cooler.
for i in /sys/bus/usb/drivers/usbhid/*:*; do
    [ -e "$i" ] || continue
    dev=$(readlink -f "$i/..")
    if [ "$(cat "$dev/idVendor" 2>/dev/null)" = 3633 ] && [ "$(cat "$dev/idProduct" 2>/dev/null)" = 0001 ]; then
        echo -n "$(basename "$i")" > /sys/bus/usb/drivers/usbhid/unbind || true
    fi
done

if [ "$RGB" -eq 1 ]; then
    command -v openrgb >/dev/null || { echo "openrgb not found, skipping RGB unit"; RGB=0; }
fi
[ "$RGB" -eq 1 ] && install -m644 systemd/ak400-rgb.service /etc/systemd/system/

systemctl daemon-reload
systemctl enable --now ak400-display.service
[ "$RGB" -eq 1 ] && systemctl enable --now ak400-rgb.service
echo "Installed. Status: systemctl status ak400-display"
