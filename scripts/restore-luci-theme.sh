#!/bin/sh
set -eu
test -s /etc/openwrtstudio-luci-original || { echo 'Original theme record not found' >&2; exit 1; }
original=$(cat /etc/openwrtstudio-luci-original)
case "$original" in /luci-static/*) ;; *) echo 'Invalid theme record' >&2; exit 1;; esac
uci set "luci.main.mediaurlbase=$original"
uci commit luci
rm -f /tmp/luci-indexcache.*
echo "Restored: $original"
