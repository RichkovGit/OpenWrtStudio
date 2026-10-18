"use strict";
"require view";
"require rpc";
"require ui";
var probe = rpc.declare({ object: "studio", method: "probe", expect: {} });
var action = rpc.declare({
  object: "studio",
  method: "action",
  params: ["name", "service"],
  expect: {},
});
return view.extend({
  render: function () {
    var output = E(
      "pre",
      {},
      "Нажмите «Проверить». Проверка обращается к openwrt.org и 1.1.1.1 с роутера. Неудачный ping сам по себе не доказывает сбой провайдера.",
    );
    var button = E(
      "button",
      {
        class: "cbi-button cbi-button-positive",
        click: function () {
          button.disabled = true;
          output.textContent = "Проверка…";
          return probe()
            .then(function (r) {
              output.textContent = r.output || r.error || "Нет данных";
            })
            .catch(function (e) {
              output.textContent = e.message;
            })
            .finally(function () {
              button.disabled = false;
            });
        },
      },
      "Проверить сеть",
    );
    function recover(service) {
      ui.showModal("Sentinel · восстановление", [
        E(
          "p",
          {},
          "Перезапустить " +
            service +
            "? Возможен кратковременный обрыв доступа.",
        ),
        E("div", { class: "right" }, [
          E("button", { class: "cbi-button", click: ui.hideModal }, "Отмена"),
          E(
            "button",
            {
              class: "cbi-button cbi-button-positive",
              click: function () {
                ui.hideModal();
                return action("restart", service)
                  .then(function (r) {
                    output.textContent =
                      (r.ok
                        ? "Команда выполнена. Повторите диагностику.\n"
                        : "Ошибка: ") + (r.error || r.output || "");
                  })
                  .catch(function (e) {
                    output.textContent = e.message;
                  });
              },
            },
            "Перезапустить",
          ),
        ]),
      ]);
    }
    return E("div", {}, [
      E("h2", {}, "Диагностика · Sentinel"),
      E(
        "p",
        {},
        "Проверка WAN, DNS, маршрутов и интерфейсов. Восстановление запускается вручную; автоматические изменения сети отключены.",
      ),
      E(
        "div",
        { class: "studio-actions" },
        [button].concat(
          ["dnsmasq", "mihomo", "sing-box"].map(function (s) {
            return E(
              "button",
              {
                class: "cbi-button",
                disabled: !L.hasViewPermission(),
                click: function () {
                  recover(s);
                },
              },
              "Перезапустить " + s,
            );
          }),
        ),
      ),
      E("div", { class: "studio-card" }, [output]),
      E("div", { class: "studio-links" }, [
        E(
          "a",
          { href: L.url("admin/network/diagnostics") },
          "Ping / traceroute / nslookup",
        ),
        E("a", { href: L.url("admin/status/nftables") }, "Правила nftables"),
        E("a", { href: L.url("admin/status/syslog") }, "Системный журнал"),
      ]),
    ]);
  },
  handleSaveApply: null,
  handleSave: null,
  handleReset: null,
});
