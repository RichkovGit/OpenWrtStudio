#!/bin/sh
set -eu
# Run from the extracted source archive. Theme selection is explicit, never automatic.
cd "$(dirname "$0")/.."
test -f /usr/share/ucode/luci/template/themes/bootstrap/header.ut || {
 echo 'Requires modern ucode LuCI and luci-theme-bootstrap.' >&2; exit 1;
}
test ! -e /etc/openwrtstudio-luci-original || {
 echo 'Studio is already installed. Restore/uninstall before installing again.' >&2; exit 1;
}
umask 077
uci -q get luci.main.mediaurlbase > /etc/openwrtstudio-luci-original
cp /etc/config/luci /etc/openwrtstudio-luci-config.backup
umask 022
cp -R luci-theme-openwrtstudio/htdocs/. /www/
cp -R luci-app-openwrtstudio/htdocs/. /www/
cp -R luci-app-openwrtstudio/root/. /
sh luci-theme-openwrtstudio/root/etc/uci-defaults/90-openwrtstudio-theme
# Reuse upstream icon assets without shipping a modified copy.
cp /www/luci-static/bootstrap/logo.svg /www/luci-static/openwrtstudio/logo.svg
cp /www/luci-static/bootstrap/logo_48.png /www/luci-static/openwrtstudio/logo_48.png
find /www/luci-static/openwrtstudio /www/luci-static/resources/view/openwrtstudio -type d -exec chmod 755 {} \;
find /www/luci-static/openwrtstudio /www/luci-static/resources/view/openwrtstudio -type f -exec chmod 644 {} \;
rm -f /tmp/luci-indexcache.*
/etc/init.d/rpcd reload
echo 'Installed. Select OpenWrtStudio under System > System > Language and Style.'
echo 'Restore the original theme: sh scripts/restore-luci-theme.sh'
