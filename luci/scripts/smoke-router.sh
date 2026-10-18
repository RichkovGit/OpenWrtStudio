#!/bin/sh
# Run on a test router as root, with locally copied candidate packages.
# Installs theme/app and leaves them installed; always restores original selected theme.
# Does not configure VPN, firewall, WAN, or wireless.
set -eu
[ "$(id -u)" = 0 ] || { echo 'Root is required' >&2; exit 1; }
[ "$#" = 2 ] || { echo 'Usage: smoke-router.sh /tmp/theme.ipk /tmp/app.ipk' >&2; exit 1; }
command -v opkg >/dev/null || { echo 'This candidate requires opkg/OpenWrt 24.10' >&2; exit 1; }
. /etc/openwrt_release
case "$DISTRIB_RELEASE" in 24.10*) ;; *) echo 'Test package targets OpenWrt 24.10; firmware differs' >&2; exit 1;; esac
[ -z "$(uci changes luci)" ] || { echo 'Pending LuCI settings exist; test has not changed anything' >&2; exit 1; }
original="$(uci -q get luci.main.mediaurlbase || true)"
original_present=0
uci -q get luci.main.mediaurlbase >/dev/null && original_present=1
restore() {
 if [ "$original_present" = 1 ]; then
  uci set "luci.main.mediaurlbase=$original"
 else
  uci -q delete luci.main.mediaurlbase || true
 fi
 if uci commit luci; then
  current="$(uci -q get luci.main.mediaurlbase || true)"
  if [ "$current" = "$original" ]; then echo 'Original theme restored and verified'; else echo 'ERROR: Theme restore mismatch' >&2; return 1; fi
 else
  echo 'ERROR: Failed to commit theme restoration' >&2
  return 1
 fi
}
trap restore EXIT
trap 'exit 1' HUP INT TERM
opkg install "$1" "$2"
/etc/init.d/rpcd restart
ubus -t 15 call studio status >/tmp/studio-status-smoke.json
jsonfilter -i /tmp/studio-status-smoke.json -e '@.ok' | grep -qx true
uci set luci.main.mediaurlbase=/luci-static/studio
uci commit luci
if command -v uclient-fetch >/dev/null; then
 uclient-fetch -q -O /tmp/studio-login-smoke.html http://127.0.0.1/cgi-bin/luci/
else
 wget -q -O /tmp/studio-login-smoke.html http://127.0.0.1/cgi-bin/luci/
fi
grep -q '/luci-static/studio/studio.css' /tmp/studio-login-smoke.html
[ "$(uci -q get studio.sentinel.enabled)" = 0 ] || { echo 'Sentinel was previously enabled; check existing configuration' >&2; exit 1; }
echo 'RPC registration and login template smoke checks passed'
