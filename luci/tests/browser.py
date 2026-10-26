"""Browser contract tests against LuCI-shaped RPC fixtures, no router mutations."""
from pathlib import Path
import json,os,shutil
from playwright.sync_api import sync_playwright
root=Path(__file__).resolve().parents[1]
views=root/'luci-app-studio/htdocs/luci-static/resources/view/studio'
fixtures={'board':{'model':'OpenWrt test router','release':{'description':'OpenWrt 24.10'}},'info':{'uptime':7260,'load':[65536,32768,0],'memory':{'total':1000,'available':350}},'status':{'services':[{'name':'mihomo','installed':True,'running':True},{'name':'sing-box','installed':False,'running':False}],'overlay':'30%','architecture':'aarch64_cortex-a53'},'getDHCPLeases':{'dhcp_leases':[{'hostname':'<img src=x onerror=alert(1)>','ipaddr':'192.0.2.2','macaddr':'00:11:22:33:44:55'}]},'probe':{'ok':True,'output':'WAN: up\nDNS: ok'},'action':{'ok':True,'output':''},'mihomo':{'ok':False,'error':'Invalid test config'}}
setup=r'''
window.calls=[];window.polls=[];window.failRpc=false;
window.E=function(tag,attrs,children){const e=document.createElement(tag);for(const [k,v] of Object.entries(attrs||{})){if(k==='click')e.addEventListener('click',v);else if(k==='disabled')e.disabled=v;else e.setAttribute(k,v);}function add(c){if(Array.isArray(c))c.forEach(add);else if(c!=null)e.append(c instanceof Node?c:document.createTextNode(String(c)));}add(children);return e;};
window.uci={load:()=>Promise.resolve(),get:()=>null,add:()=>'',set:()=>{},save:()=>Promise.resolve()};window.validation={parseIPv4:x=>/^([0-9]{1,3}\.){3}[0-9]{1,3}$/.test(x),parseIPv6:x=>x.includes(':')};
window.L={url:x=>'/cgi-bin/luci/'+x,hasViewPermission:()=>true};
window.view={extend:o=>o};window.poll={add:(f)=>polls.push(f)};
window.rpc={declare:d=>(...args)=>{calls.push({method:d.method,args});if(window.failRpc)return Promise.reject(new Error('Router unavailable'));return Promise.resolve(fixtures[d.method]);}};
window.ui={showModal:(title,children)=>{ui.hideModal();const modal=E('div',{id:'modal'},[E('h3',{},title),children]);document.body.append(modal);},hideModal:()=>document.querySelector('#modal')?.remove(),addNotification:(a,b)=>document.body.append(b)};
'''
with sync_playwright() as p:
 browser=p.chromium.launch(executable_path=os.environ.get('STUDIO_CHROMIUM') or shutil.which('chromium'),args=['--no-sandbox'])
 page=browser.new_page(viewport={'width':1280,'height':900})
 errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
 def load(name):
  page.set_content('<!doctype html><html data-darkmode="true"><head><meta charset="utf-8"></head><body><main id="view"></main></body></html>')
  page.add_style_tag(path=str(root/'luci-theme-studio/htdocs/luci-static/studio/cascade.css'))
  page.add_style_tag(path=str(root/'luci-theme-studio/htdocs/luci-static/studio/studio.css'))
  page.evaluate('window.fixtures='+json.dumps(fixtures))
  page.evaluate(setup)
  page.evaluate('(async()=>{window.app=(new Function('+json.dumps((views/(name+'.js')).read_text())+'))();const data=app.load?await app.load():null;document.querySelector("#view").append(app.render(data));})()')
 load('overview')
 assert page.get_by_text('65.0%',exact=True).count()==1
 assert page.get_by_text('<img src=x onerror=alert(1)>',exact=True).count()==1
 assert page.locator('img').count()==0
 with page.expect_download() as download:page.get_by_text('Сохранить отчёт',exact=True).click()
 report=Path(download.value.path()).read_text()
 assert '192.0.2.2' not in report and '00:11' not in report and 'OpenWrt test router' in report
 fixtures['getDHCPLeases']['dhcp_leases'][0]['hostname']='Laptop'
 load('overview')
 out=root/'artifacts';out.mkdir(exist_ok=True)
 page.screenshot(path=str(out/'overview-dark.png'),full_page=True)
 page.set_viewport_size({'width':390,'height':844})
 assert page.evaluate('document.documentElement.scrollWidth <= 390')
 page.screenshot(path=str(out/'overview-mobile.png'),full_page=True)
 page.evaluate('window.failRpc=true;polls[0]()')
 assert page.get_by_text('Данные недоступны:',exact=False).count()==1
 assert page.get_by_text('65.0%',exact=True).count()==0
 load('capabilities')
 page.get_by_text('restart',exact=True).click()
 assert not any(x['method']=='action' for x in page.evaluate('calls'))
 page.get_by_text('Выполнить',exact=True).click()
 assert any(x['method']=='action' and x['args']==['restart','mihomo'] for x in page.evaluate('calls'))
 load('diagnostics');page.get_by_text('Проверить сеть',exact=True).click()
 assert page.get_by_text('WAN: up',exact=False).count()==1
 load('mihomo')
 page.locator('input').fill('javascript:alert(1)');page.get_by_text('Сформировать',exact=True).click()
 assert page.locator('textarea').input_value()==''
 page.locator('input').fill('https://example.org/private-subscription');page.get_by_text('Сформировать',exact=True).click()
 cfg=json.loads(page.locator('textarea').input_value())
 assert cfg['allow-lan'] is False and cfg['proxy-providers']['subscription']['url'].startswith('https://')
 page.get_by_text('Проверить ядром Mihomo и применить',exact=True).click()
 assert not any(x['method']=='mihomo' for x in page.evaluate('calls'))
 page.get_by_text('Проверить и применить',exact=True).click()
 assert page.get_by_text('Ошибка: Invalid test config',exact=True).count()==1
 load('vpn')
 key='A'*43+'='
 profile='[Interface]\nPrivateKey = '+key+'\nAddress = 10.0.0.2/24\nDNS = 1.1.1.1\nJc = 5\n[Peer]\nPublicKey = '+key+'\nAllowedIPs = 0.0.0.0/0, ::/0\nEndpoint = [2001:db8::1]:51820\nPersistentKeepalive = 25'
 parsed=page.evaluate('(text)=>app.parseWireGuard(text,"awg0","amneziawg")',profile)
 assert parsed['peers'][0]['endpoint_host']=='2001:db8::1'
 assert parsed['values']['jc']=='5' and parsed['values']['dns']==['1.1.1.1']
 for bad in [profile+'\nPreUp = touch /tmp/test',profile.replace('51820','99999'),profile.replace('10.0.0.2/24','10.0.0.2/99')]:
  assert page.evaluate('(text)=>{try{app.parseWireGuard(text,"awg0","amneziawg");return false;}catch(e){return true;}}',bad)
 for protocol in ['vless','hysteria2','tuic']:
  generated=page.evaluate('(protocol)=>app.buildSingbox({protocol,server:"example.org",port:"443",uuid:"00000000-0000-0000-0000-000000000000",password:"secret",sni:"example.org",publickey:"A".repeat(43),shortid:"abcd"})',protocol)
  assert generated['outbounds'][0]['type']==protocol
 load('mesh')
 page.locator('input[type=password]').fill('bad')
 page.get_by_text('Подключить Tailscale',exact=True).click()
 assert not any(x['method']=='mesh' for x in page.evaluate('calls'))
 page.locator('input[type=password]').fill('tskey-auth-EXAMPLE')
 page.get_by_text('Подключить Tailscale',exact=True).click()
 assert not any(x['method']=='mesh' for x in page.evaluate('calls'))
 page.evaluate("fixtures.mesh={ok:true,output:'Connected'}")
 page.get_by_text('Подключить',exact=True).click()
 assert any(x['method']=='mesh' and x['args'][0]=='tailscale' for x in page.evaluate('calls'))
 assert page.locator('input[type=password]').input_value()==''
 load('tools')
 assert page.locator('a').count()>=14
 assert not errors,errors
 browser.close()
print('Browser contracts passed: overview, polling failure, report privacy, mobile layout, service confirmation, diagnostics, Mihomo validation and error propagation, tools.')
