include $(TOPDIR)/rules.mk
PKG_NAME:=luci-theme-studio
PKG_VERSION:=1.0.0~rc1
PKG_RELEASE:=1
LUCI_TITLE:=OpenWrt Studio theme
LUCI_DEPENDS:=+luci-base +luci-theme-bootstrap
PKG_LICENSE:=Apache-2.0
define Package/luci-theme-studio/postrm
#!/bin/sh
[ -n "$${IPKG_INSTROOT}" ] || {
 if [ "$$(uci -q get luci.main.mediaurlbase)" = /luci-static/studio ]; then
  uci set luci.main.mediaurlbase=/luci-static/bootstrap
 fi
 uci -q delete luci.themes.OpenWrtStudio
 uci commit luci
}
endef
include $(TOPDIR)/feeds/luci/luci.mk
