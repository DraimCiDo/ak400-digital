#!/bin/sh
# Remove the DeepCool AK400 DIGITAL display driver.
set -e
[ "$(id -u)" -eq 0 ] || { echo "Run as root: sudo $0"; exit 1; }
systemctl disable --now ak400-display.service ak400-rgb.service 2>/dev/null || true
rm -f /usr/local/bin/ak400-display \
      /etc/systemd/system/ak400-display.service \
      /etc/systemd/system/ak400-rgb.service \
      /etc/udev/rules.d/99-ak400-nohid.rules
systemctl daemon-reload
udevadm control --reload
echo "Removed. Replug the cooler (or reboot) to give it back to usbhid."
