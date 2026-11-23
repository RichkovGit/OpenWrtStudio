include $(TOPDIR)/rules.mk
PKG_NAME:=luci-app-studio
PKG_VERSION:=1.0.0~rc1
PKG_RELEASE:=1
LUCI_TITLE:=OpenWrt Studio router management
LUCI_DEPENDS:=+luci-base +rpcd +rpcd-mod-file +rpcd-mod-iwinfo +luci-mod-admin-full +jshn +luci-app-firewall +luci-app-opkg
PKG_LICENSE:=MIT
define Package/luci-app-studio/conffiles
/etc/config/studio
endef
include $(TOPDIR)/feeds/luci/luci.mk
