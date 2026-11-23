"use strict";
"require view";
"require rpc";
"require poll";
"require ui";
var board = rpc.declare({ object: "system", method: "board", expect: {} });
var info = rpc.declare({ object: "system", method: "info", expect: {} });
var status = rpc.declare({ object: "studio", method: "status", expect: {} });
var leases = rpc.declare({
  object: "luci-rpc",
  method: "getDHCPLeases",
  expect: {},
});
function card(title, value, detail, percent) {
  return E("div", { class: "studio-card" }, [
    E("h3", {}, title),
    E("div", { class: "studio-value" }, String(value)),
    E("p", { class: "studio-muted" }, detail || ""),
    percent == null
      ? ""
      : E("div", { class: "studio-meter" }, [
          E("span", {
            style: "width:" + Math.max(0, Math.min(100, percent)) + "%",
          }),
        ]),
  ]);
}
return view.extend({
  load: function () {
    return Promise.all([board(), info(), status(), leases()]);
  },
  render: function (data) {
    var root = E("div"),
      latest = data;
    function draw(d) {
      latest = d;
      var b = d[0],
        i = d[1],
        s = d[2],
        m = i.memory || {},
        ram = m.total
          ? (100 *
              (m.total -
                (m.available != null
                  ? m.available
                  : (m.free || 0) + (m.buffered || 0) + (m.cached || 0)))) /
            m.total
          : null;
      var clients = d[3].dhcp_leases || [],
        load = (i.load || [])
          .map(function (n) {
            return (n / 65536).toFixed(2);
          })
          .join(" / ");
      root.replaceChildren(
        E("div", { class: "studio-hero" }, [
          E("div", {}, [
            E("h2", {}, "Ваша сеть под контролем"),
            E(
              "p",
              {},
              (b.model || "OpenWrt") +
                " · " +
                ((b.release || {}).description || "") +
                " · " +
                new Date().toLocaleTimeString(),
            ),
          ]),
          E("span", { class: "studio-status" }, "Подключено · LuCI RPC"),
        ]),
        E("div", { class: "studio-grid" }, [
          card("Загрузка системы", load, "Load average · 1 / 5 / 15 минут"),
          card(
            "Оперативная память",
            ram == null ? "Нет данных" : ram.toFixed(1) + "%",
            "С учётом MemAvailable",
            ram,
          ),
          card(
            "Хранилище overlay",
            s.overlay || "Нет данных",
            "Занято места",
            parseFloat(s.overlay) || 0,
          ),
          card(
            "Время работы",
            Math.floor((i.uptime || 0) / 3600) +
              " ч " +
              Math.floor(((i.uptime || 0) % 3600) / 60) +
              " мин",
            s.architecture,
          ),
        ]),
        E("div", { class: "studio-actions" }, [
          E(
            "button",
            { class: "cbi-button", click: report },
            "Сохранить отчёт",
          ),
          E(
            "a",
            { class: "cbi-button", href: L.url("admin/studio/diagnostics") },
            "Проверить сеть",
          ),
        ]),
        E("h3", { id: "studio-clients" }, "Клиенты DHCP"),
        E(
          "p",
          { class: "studio-muted" },
          "Активные IPv4-аренды. Статические адреса и ограничения доступа настраиваются в разделе «Настройки и инструменты».",
        ),
        E(
          "table",
          { class: "table" },
          [
            E(
              "tr",
              { class: "tr table-titles" },
              ["Имя", "IP-адрес", "MAC-адрес"].map(function (x) {
                return E("th", { class: "th" }, x);
              }),
            ),
          ].concat(
            clients.map(function (c) {
              return E(
                "tr",
                { class: "tr" },
                [c.hostname || "—", c.ipaddr || "—", c.macaddr || "—"].map(
                  function (x) {
                    return E("td", { class: "td" }, x);
                  },
                ),
              );
            }),
          ),
        ),
      );
      if (ram > 92 || parseFloat(s.overlay) > 92)
        root.appendChild(
          E(
            "div",
            { class: "alert-message warning" },
            "Ресурсы роутера близки к исчерпанию. Проверьте память и свободное место.",
          ),
        );
    }
    function report() {
      var b = latest[0],
        i = latest[1],
        s = latest[2];
      var text = JSON.stringify(
        {
          application: "OpenWrt Studio LuCI 1.0.0-rc1",
          model: b.model,
          release: (b.release || {}).description,
          uptime: i.uptime,
          load: i.load,
          memory: i.memory,
          overlay: s.overlay,
          architecture: s.architecture,
        },
        null,
        2,
      );
      var url = URL.createObjectURL(
        new Blob([text], { type: "text/plain;charset=utf-8" }),
      );
      var a = E("a", { href: url, download: "openwrt-studio-report.txt" });
      a.click();
      setTimeout(function () {
        URL.revokeObjectURL(url);
      }, 1000);
    }
    draw(data);
    poll.add(function () {
      return Promise.all([board(), info(), status(), leases()])
        .then(draw)
        .catch(function (e) {
          root.replaceChildren(
            E(
              "div",
              { class: "alert-message warning" },
              "Данные недоступны: " + e.message,
            ),
          );
        });
    }, 25);
    return root;
  },
  handleSaveApply: null,
  handleSave: null,
  handleReset: null,
});
