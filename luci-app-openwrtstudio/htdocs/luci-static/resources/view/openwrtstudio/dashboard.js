'use strict';
'require view';
'require rpc';
'require poll';
'require ui';

var board = rpc.declare({object:'system', method:'board', expect:{'':{}}});
var info = rpc.declare({object:'system', method:'info', expect:{'':{}}});
var leases = rpc.declare({object:'luci-rpc', method:'getDHCPLeases', expect:{'':{}}});
var services = rpc.declare({object:'service', method:'list', expect:{'':{}}});

var menuTree;
function exists(path) {
 var node=menuTree;
 return path.split('/').every(function(part) {node=node && node.children && node.children[part]; return !!node && node.satisfied !== false;});
}
function link(title, path) {
 if (!exists(path)) return E('div',{class:'studio-link studio-muted'},title+' · модуль LuCI не установлен');
 return E('a', {class:'studio-link', href:L.url.apply(L, path.split('/'))}, title + ' →');
}
function card(title, children) { return E('section', {class:'studio-card'}, [E('h3', {}, title)].concat(children)); }
function metric(title, value, subtitle) { return card(title,[E('div',{class:'studio-number'},value),E('div',{class:'studio-muted'},subtitle)]); }
function active(service) { return Object.values((service || {}).instances || {}).some(function(i) {return i.running === true;}); }

return view.extend({
 load: function() { return Promise.all([board(),info(),leases(),services(),ui.menu.load()]); },
 render: function(data) {
  var content = E('div',{class:'studio-dashboard'});
  if (!document.querySelector('link[data-studio-dashboard]'))
   document.head.appendChild(E('link',{'data-studio-dashboard':'1',rel:'stylesheet',href:L.resource('view/openwrtstudio/dashboard.css')}));
  menuTree=data[4];
  function update(d) {
   var b=d[0], s=d[1], clients=(d[2].dhcp_leases || []), svc=d[3], memory=s.memory || {};
   var used=memory.total ? Math.round((memory.total-(memory.available || memory.free || 0))*100/memory.total) : 0;
   var protocols=[['WireGuard','network'],['AmneziaWG','amneziawg'],['Sing-box','sing-box'],['Mihomo','mihomo'],['PassWall','passwall'],['OpenVPN','openvpn'],['Tailscale','tailscale'],['ZeroTier','zerotier'],['Xray','xray']];
   domContent(content, [
    E('section',{class:'studio-hero'},[
     E('div',{class:'studio-eyebrow'},'OPENWRT STUDIO • LuCI EDITION'),
     E('h2',{},'Ваш роутер. Единый центр управления.'),
     E('p',{class:'studio-muted'},(b.model || 'OpenWrt')+' · '+((b.release || {}).description || '')),
     E('span',{class:'studio-badge'},'Предварительная версия 0.1.0')
    ]),
    E('div',{class:'studio-grid'},[
     metric('Время работы', Math.floor((s.uptime||0)/3600)+' ч '+Math.floor(((s.uptime||0)%3600)/60)+' мин',b.hostname || ''),
     metric('Оперативная память',used+'%',Math.round((memory.total||0)/1048576)+' МБ всего'),
     metric('Нагрузка CPU',((s.load||[0])[0]/65536).toFixed(2),'Средняя нагрузка за минуту'),
     metric('DHCP-клиенты',String(clients.length),'Выданные IPv4-адреса')
    ]),
    E('div',{class:'studio-grid'},[
     card('Сеть и устройства',[link('Wi-Fi','admin/network/wireless'),link('LAN / WAN / WireGuard','admin/network/network'),link('DHCP и статические адреса','admin/network/dhcp'),link('Брандмауэр и проброс портов','admin/network/firewall')]),
     card('Система',[link('Hostname, время и оформление','admin/system/system'),link('Пакеты','admin/system/package-manager'),link('Резервная копия','admin/system/flash'),link('Планировщик Cron','admin/system/crontab'),link('Точки монтирования / USB','admin/system/mounts')]),
     card('Диагностика',[link('Сетевые тесты','admin/network/diagnostics'),link('Системный журнал','admin/status/logs/syslog'),link('Процессы','admin/status/processes'),link('Графики трафика','admin/status/realtime/bandwidth'),link('Перезагрузка','admin/system/reboot')])
    ]),
    card('Приложения на роутере',[
     E('p',{class:'studio-muted'},'Доступные конфигураторы LuCI. Настройка выполняется установленными приложениями.'),
     E('div',{class:'studio-grid'},[
      ['ForkOP','admin/services/forkop'],['PassWall','admin/services/passwall'],['PassWall 2','admin/services/passwall2'],['OpenVPN','admin/vpn/openvpn'],['Tailscale','admin/vpn/tailscale'],['ZeroTier','admin/vpn/zerotier'],['WireGuard: состояние','admin/status/wireguard']
     ].filter(function(p){return exists(p[1]);}).map(function(p){return link(p[0],p[1]);}))
    ]),
    card('Карта служб VPN',[
     E('p',{class:'studio-muted'},'Состояние зарегистрированных служб procd. WireGuard проверяйте в сетевых интерфейсах. Отсутствие службы не означает отсутствие пакета.'),
     E('div',{class:'studio-grid'},protocols.map(function(p){var service=svc[p[1]];return E('div',{},[E('strong',{},p[0]),E('div',{class:'studio-muted'},p[1]==='network'?'В сетевых интерфейсах':active(service)?'Работает':service?'Остановлена':'Служба не зарегистрирована')]);}))
    ]),
    card('Клиенты DHCP', [E('div',{class:'table'},[
     E('div',{class:'tr table-titles'},['Устройство','IPv4','MAC'].map(function(t){return E('div',{class:'th'},t);}))
    ].concat(clients.map(function(c){return E('div',{class:'tr'},[c.hostname || 'Без имени',c.ipaddr || '—',c.macaddr || '—'].map(function(t){return E('div',{class:'td'},t);}));})))]),
    E('p',{class:'studio-muted'},'Обновление каждые 10 секунд. Настройки открываются в штатных формах LuCI с проверкой и применением изменений. Конструкторы Mihomo, подписки и Sentinel из приложения Windows пока не перенесены.')
   ]);
  }
  function domContent(target, nodes) { while(target.firstChild) target.removeChild(target.firstChild); nodes.forEach(function(n){target.appendChild(n);}); }
  update(data);
  poll.add(function(){return Promise.all([board(),info(),leases(),services()]).then(update).catch(function(e){ui.addNotification(null,E('p',{},'Studio: '+e.message));});},10);
  return content;
 },
 handleSaveApply:null,handleSave:null,handleReset:null
});
