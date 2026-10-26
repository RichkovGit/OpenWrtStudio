"use strict";
"require view";
"require rpc";
"require ui";
var deploy = rpc.declare({
  object: "studio",
  method: "mihomo",
  params: ["content"],
  expect: {},
});
function config(url, mode) {
  if (!/^https:\/\/[^\s]+$/.test(url))
    throw new Error("Нужна HTTPS-ссылка на подписку в формате Mihomo.");
  return {
    "mixed-port": 7890,
    "allow-lan": false,
    mode: "rule",
    "log-level": "info",
    tun: {
      enable: true,
      stack: "mixed",
      "auto-route": true,
      "auto-detect-interface": true,
      "dns-hijack": ["any:53"],
    },
    dns: {
      enable: true,
      listen: "127.0.0.1:1053",
      "enhanced-mode": "fake-ip",
      nameserver: ["https://1.1.1.1/dns-query"],
      "fake-ip-filter": ["*.lan", "*.local"],
    },
    "proxy-providers": {
      subscription: {
        type: "http",
        url: url,
        interval: 86400,
        path: "./proxy_providers/studio.yaml",
        "health-check": {
          enable: true,
          url: "https://www.gstatic.com/generate_204",
          interval: 300,
        },
      },
    },
    "proxy-groups": [
      { name: "PROXY", type: "select", use: ["subscription"] },
      {
        name: "AUTO",
        type: "url-test",
        use: ["subscription"],
        url: "https://www.gstatic.com/generate_204",
        interval: 300,
      },
    ],
    rules: [
      "IP-CIDR,127.0.0.0/8,DIRECT",
      "IP-CIDR,192.168.0.0/16,DIRECT",
      "IP-CIDR,10.0.0.0/8,DIRECT",
      "IP-CIDR,172.16.0.0/12,DIRECT",
    ].concat(
      mode === "smart"
        ? [
            "GEOIP,RU,DIRECT",
            "GEOSITE,youtube,PROXY",
            "GEOSITE,discord,PROXY",
            "MATCH,PROXY",
          ]
        : ["MATCH,PROXY"],
    ),
  };
}
return view.extend({
  render: function () {
    var subscription = E("input", {
      type: "password",
      class: "cbi-input-text",
      placeholder: "https://…",
      autocomplete: "off",
    });
    var preset = E("select", { class: "cbi-input-select" }, [
      E(
        "option",
        { value: "smart" },
        "RU напрямую, внешний трафик через прокси",
      ),
      E("option", { value: "full" }, "Полный туннель"),
    ]);
    var editor = E("textarea", {
      class: "studio-editor",
      spellcheck: "false",
      placeholder: "Конфигурация JSON (допустимое подмножество YAML)",
    });
    var result = E("pre");
    return E("div", {}, [
      E("h2", {}, "Конструктор Mihomo"),
      E(
        "p",
        {},
        "Подписка должна возвращать YAML с узлами Mihomo. Генератор создаёт конфигурацию с TUN и обновлением подписки раз в сутки. Правила GEOSITE/GEOIP требуют геобаз Mihomo. Парольная ссылка остаётся только в форме и конфигурации роутера.",
      ),
      E("div", { class: "studio-card" }, [
        E("label", {}, "Подписка HTTPS"),
        subscription,
        E("label", {}, "Маршрутизация"),
        preset,
        E("div", { class: "studio-actions" }, [
          E(
            "button",
            {
              class: "cbi-button",
              click: function () {
                try {
                  editor.value = JSON.stringify(
                    config(subscription.value.trim(), preset.value),
                    null,
                    2,
                  );
                } catch (e) {
                  ui.addNotification(null, E("p", {}, e.message), "error");
                }
              },
            },
            "Сформировать",
          ),
        ]),
        editor,
        E("div", { class: "studio-actions" }, [
          E(
            "button",
            {
              class: "cbi-button cbi-button-positive",
              disabled: !L.hasViewPermission(),
              click: function () {
                var content = editor.value;
                try {
                  var parsed = JSON.parse(content);
                  if (!parsed["proxy-groups"] || !parsed.rules)
                    throw new Error("Нужны proxy-groups и rules");
                } catch (e) {
                  result.textContent = "Ошибка JSON: " + e.message;
                  return;
                }
                ui.showModal("Применить Mihomo?", [
                  E(
                    "p",
                    {},
                    "Будет заменён /etc/mihomo/config.yaml и перезапущена служба mihomo. Предыдущая конфигурация сохранится в config.yaml.studio-backup. TUN меняет маршрутизацию и может прервать доступ. Убедитесь, что служба использует этот путь.",
                  ),
                  E("div", { class: "right" }, [
                    E(
                      "button",
                      { class: "cbi-button", click: ui.hideModal },
                      "Отмена",
                    ),
                    E(
                      "button",
                      {
                        class: "cbi-button cbi-button-positive",
                        click: function () {
                          ui.hideModal();
                          result.textContent = "Проверка и применение…";
                          return deploy(content)
                            .then(function (r) {
                              result.textContent =
                                (r.ok ? "Готово: " : "Ошибка: ") +
                                (r.error || r.output || "");
                            })
                            .catch(function (e) {
                              result.textContent = e.message;
                            });
                        },
                      },
                      "Проверить и применить",
                    ),
                  ]),
                ]);
              },
            },
            "Проверить ядром Mihomo и применить",
          ),
        ]),
        result,
      ]),
    ]);
  },
  handleSaveApply: null,
  handleSave: null,
  handleReset: null,
});
