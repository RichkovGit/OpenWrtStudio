"use strict";
"require view";
"require rpc";
"require ui";
var configure = rpc.declare({
  object: "studio",
  method: "mesh",
  params: ["kind", "key", "routes", "exitnode", "accept"],
  expect: {},
});
return view.extend({
  render: function () {
    var auth = E("input", {
        type: "password",
        autocomplete: "off",
        placeholder: "tskey-auth-…",
      }),
      routes = E("input", { placeholder: "192.168.1.0/24,10.0.0.0/24" }),
      exitnode = E("input", { type: "checkbox" }),
      accept = E("input", { type: "checkbox" }),
      network = E("input", { placeholder: "0123456789abcdef" }),
      output = E("pre");
    accept.checked = true;
    function apply(kind) {
      var key = (kind === "tailscale" ? auth : network).value.trim();
      if (
        kind === "tailscale"
          ? !/^tskey-auth-[A-Za-z0-9_-]+$/.test(key)
          : !/^[0-9a-f]{16}$/i.test(key)
      ) {
        output.textContent = "Проверьте ключ / ID сети";
        return;
      }
      ui.showModal("Подключить " + kind + "?", [
        E(
          "p",
          {},
          "Роутер подключится к указанной VPN-сети. Для Tailscale анонс маршрутов и Exit Node требуют одобрения в панели tailnet. Для ZeroTier подтвердите участника в контроллере сети.",
        ),
        E("button", { class: "cbi-button", click: ui.hideModal }, "Отмена"),
        E(
          "button",
          {
            class: "cbi-button cbi-button-positive",
            click: function () {
              ui.hideModal();
              output.textContent = "Подключение…";
              return configure(
                kind,
                key,
                routes.value.trim(),
                exitnode.checked,
                accept.checked,
              )
                .then(function (r) {
                  output.textContent =
                    (r.ok ? "Готово: " : "Ошибка: ") +
                    (r.error || r.output || "");
                  if (r.ok) auth.value = "";
                })
                .catch(function (e) {
                  output.textContent = e.message;
                });
            },
          },
          "Подключить",
        ),
      ]);
    }
    return E("div", {}, [
      E("h2", {}, "Mesh VPN"),
      E("div", { class: "studio-grid" }, [
        E("div", { class: "studio-card" }, [
          E("h3", {}, "Tailscale"),
          E("label", {}, "Auth key"),
          auth,
          E("label", {}, "Подсети для анонса"),
          routes,
          E("p", {}, [exitnode, " Анонсировать Exit Node"]),
          E("p", {}, [accept, " Принимать маршруты tailnet"]),
          E(
            "button",
            {
              class: "cbi-button cbi-button-positive",
              disabled: !L.hasViewPermission(),
              click: function () {
                apply("tailscale");
              },
            },
            "Подключить Tailscale",
          ),
        ]),
        E("div", { class: "studio-card" }, [
          E("h3", {}, "ZeroTier"),
          E("label", {}, "Network ID"),
          network,
          E(
            "button",
            {
              class: "cbi-button cbi-button-positive",
              disabled: !L.hasViewPermission(),
              click: function () {
                apply("zerotier");
              },
            },
            "Подключить ZeroTier",
          ),
        ]),
      ]),
      output,
    ]);
  },
  handleSaveApply: null,
  handleSave: null,
  handleReset: null,
});
