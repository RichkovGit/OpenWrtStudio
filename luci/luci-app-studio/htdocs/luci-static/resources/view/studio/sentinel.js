"use strict";
"require view";
"require form";
"require uci";
return view.extend({
  load: function () {
    return uci.load("studio");
  },
  render: function () {
    var m = new form.Map(
      "studio",
      "Sentinel · фоновый мониторинг",
      "Проверяет внешнюю связность и локальный DNS непосредственно на роутере. Работает после закрытия браузера. Включается по желанию. Повторные сбои WAN и внешней сети записываются в журнал; восстановление ограничено DNS и выбранной службой прокси.",
    );
    var s = m.section(form.NamedSection, "sentinel", "sentinel");
    s.addremove = false;
    var o = s.option(form.Flag, "enabled", "Включить Sentinel");
    o.rmempty = false;
    o = s.option(form.Value, "interval", "Период проверки, секунд");
    o.datatype = "range(30,3600)";
    o.default = "60";
    o.rmempty = false;
    o = s.option(form.Value, "failures", "Сбоев подряд до восстановления");
    o.datatype = "range(2,20)";
    o.default = "3";
    o.rmempty = false;
    o = s.option(
      form.Value,
      "cooldown",
      "Пауза между восстановлениями, секунд",
    );
    o.datatype = "range(300,86400)";
    o.default = "300";
    o.rmempty = false;
    o = s.option(
      form.Flag,
      "dns",
      "Перезапускать dnsmasq при локальном сбое DNS",
    );
    o.rmempty = false;
    o = s.option(
      form.ListValue,
      "proxy",
      "Перезапускать остановленное ядро при потере связи",
    );
    o.value("none", "Не перезапускать");
    o.value("mihomo", "Mihomo");
    o.value("sing-box", "Sing-box");
    o.default = "none";
    return m.render();
  },
});
