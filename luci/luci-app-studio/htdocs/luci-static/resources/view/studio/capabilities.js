"use strict";
"require view";
"require rpc";
"require ui";
var status = rpc.declare({ object: "studio", method: "status", expect: {} });
var action = rpc.declare({
  object: "studio",
  method: "action",
  params: ["name", "service"],
  expect: {},
});
var routes = {
  "studio-sentinel": "admin/studio/sentinel",
  mihomo: "admin/studio/mihomo",
  "sing-box": "admin/services/sing-box",
  xray: "admin/services/passwall2",
  tailscale: "admin/services/tailscale",
  zerotier: "admin/services/zerotier",
  openvpn: "admin/services/openvpn",
  smartdns: "admin/services/smartdns",
  mosdns: "admin/services/mosdns",
  passwall2: "admin/services/passwall2",
  dnsmasq: "admin/network/dhcp",
};
return view.extend({
  load: function () {
    return status();
  },
  render: function (data) {
    function run(service, name, button) {
      ui.showModal("Подтверждение", [
        E(
          "p",
          {},
          name +
            " · " +
            service +
            ". Действие может прервать соединение с роутером.",
        ),
        E("div", { class: "right" }, [
          E("button", { class: "cbi-button", click: ui.hideModal }, "Отмена"),
          E(
            "button",
            {
              class: "cbi-button cbi-button-positive",
              click: function () {
                ui.hideModal();
                button.disabled = true;
                action(name, service)
                  .then(function (r) {
                    if (!r.ok) throw new Error(r.error || r.output);
                    ui.addNotification(
                      null,
                      E("p", {}, "Команда выполнена: " + service),
                      "info",
                    );
                  })
                  .catch(function (e) {
                    ui.addNotification(null, E("p", {}, e.message), "error");
                  })
                  .finally(function () {
                    button.disabled = false;
                  });
              },
            },
            "Выполнить",
          ),
        ]),
      ]);
    }
    return E("div", {}, [
      E("h2", {}, "Карта возможностей"),
      E(
        "p",
        {},
        "Состояние служб определяется на роутере. Для настройки протокола используется его LuCI-модуль; доступность зависит от установленных пакетов.",
      ),
      E("div", { class: "studio-links" }, [
        E(
          "a",
          { href: L.url("admin/network/network") },
          "WireGuard / AmneziaWG · интерфейсы",
        ),
        E("a", { href: L.url("admin/system/opkg") }, "Установить пакеты"),
      ]),
      E(
        "div",
        { class: "studio-grid" },
        (data.services || []).map(function (s) {
          return E("div", { class: "studio-card" }, [
            E("h3", {}, s.name),
            E(
              "span",
              { class: "studio-status" + (s.running ? "" : " studio-warn") },
              s.running
                ? "Работает"
                : s.installed
                  ? "Остановлен"
                  : "Не установлен",
            ),
            E(
              "div",
              { class: "studio-actions" },
              s.installed
                ? ["start", "stop", "restart"].map(function (n) {
                    var button = E(
                      "button",
                      {
                        class: "cbi-button",
                        disabled: !L.hasViewPermission(),
                        click: function () {
                          run(s.name, n, button);
                        },
                      },
                      n,
                    );
                    return button;
                  })
                : [
                    E(
                      "a",
                      {
                        href:
                          L.url("admin/system/opkg") +
                          "?query=" +
                          encodeURIComponent(s.name),
                      },
                      "Каталог пакетов",
                    ),
                  ],
            ),
            s.installed
              ? E(
                  "a",
                  { href: L.url(routes[s.name]) },
                  "Открыть конфигуратор (если установлен)",
                )
              : "",
          ]);
        }),
      ),
    ]);
  },
  handleSaveApply: null,
  handleSave: null,
  handleReset: null,
});
