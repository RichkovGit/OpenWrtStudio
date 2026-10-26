"""Exercise rpcd dispatch in an isolated filesystem with stubbed OpenWrt tools."""
from pathlib import Path
import tempfile,subprocess,json,os
source=Path(__file__).resolve().parents[1]/'luci-app-studio/root/usr/libexec/rpcd/studio'
with tempfile.TemporaryDirectory() as temporary:
 root=Path(temporary);etc=root/'etc';(etc/'init.d').mkdir(parents=True);(etc/'mihomo').mkdir()
 tools=root/'bin';tools.mkdir()
 # jshn contract shim for scalar request/response methods. status is checked in browser fixtures only.
 shim=root/'jshn.sh';shim.write_text('''json_load() { request_json=$(printf '%s' "$1" | jq -ce .); }
json_get_var() { local value; value=$(printf '%s' "$request_json" | jq -r --arg key "$2" '.[$key] // ""'); export "$1=$value"; }
json_init() { output_json='{}'; }
json_add_boolean() { output_json=$(printf '%s' "$output_json" | jq -c --arg key "$1" --argjson value "$2" '. + {($key): ($value == 1)}'); }
json_add_string() { output_json=$(printf '%s' "$output_json" | jq -c --arg key "$1" --arg value "$2" '. + {($key): $value}'); }
json_dump() { printf '%s\\n' "$output_json"; }
''')
 script=root/'studio';script.write_text(source.read_text().replace('/usr/share/libubox/jshn.sh',str(shim)).replace('/etc/',str(etc)+'/'))
 marker=root/'executed';service=etc/'init.d/mihomo';service.write_text('#!/bin/sh\nprintf "%s" "$1" > "'+str(marker)+'"\nexit "${SERVICE_EXIT:-0}"\n');service.chmod(0o755)
 mihomo=tools/'mihomo';mihomo.write_text('#!/bin/sh\n[ "$MIHOMO_INVALID" != 1 ]\n');mihomo.chmod(0o755)
 def call(method,payload,**env):
  result=subprocess.run(['sh',str(script),'call',method],input=json.dumps(payload)+'\n',text=True,capture_output=True,env={**os.environ,'PATH':str(tools)+':'+os.environ['PATH'],**env})
  assert result.returncode==0,result.stderr
  return json.loads(result.stdout)
 assert call('action',{'name':'restart','service':'mihomo'})['ok']
 assert marker.read_text()=='restart';marker.unlink()
 for payload in [{'name':'restart; touch injected','service':'mihomo'},{'name':'restart','service':'../../bin/sh'},{'name':'restart','service':'mihomo; touch injected'}]:
  assert not call('action',payload)['ok'];assert not marker.exists()
 assert not call('action',{'name':'restart','service':'mihomo'},SERVICE_EXIT='1')['ok']
 config=etc/'mihomo/config.yaml';config.write_text('old config')
 assert not call('mihomo',{'content':'invalid'},MIHOMO_INVALID='1')['ok'];assert config.read_text()=='old config'
 assert call('mihomo',{'content':'{"rules":["MATCH,DIRECT"]}'})['ok']
 assert config.read_text().strip()=='{"rules":["MATCH,DIRECT"]}'
 assert (etc/'mihomo/config.yaml.studio-backup').read_text()=='old config'
 assert config.stat().st_mode & 0o777 == 0o600
 config.unlink();target=root/'protected';target.write_text('untouched');config.symlink_to(target)
 assert not call('mihomo',{'content':'{}'})['ok'];assert target.read_text()=='untouched'
 assert not call('missing',{})['ok']
 assert not list((etc/'mihomo').glob('.studio.*'))
 for service_name in ['sing-box','openvpn']:
  service_file=etc/'init.d'/service_name;service_file.write_text(service.read_text());service_file.chmod(0o755)
  core=tools/service_name;core.write_text('#!/bin/sh\n[ "$VPN_INVALID" != 1 ]\n');core.chmod(0o755)
 uci=tools/'uci';uci.write_text('#!/bin/sh\nexit 0\n');uci.chmod(0o755)
 assert not call('vpnconfig',{'kind':'../../bad','content':'{}'})['ok']
 assert not call('vpnconfig',{'kind':'sing-box','content':'{}'},VPN_INVALID='1')['ok']
 assert call('vpnconfig',{'kind':'sing-box','content':'{"log":{"level":"info"}}'})['ok']
 assert (etc/'sing-box/config.json').stat().st_mode & 0o777 == 0o600
 profile='client\ndev tun\nremote vpn.example.org 1194\n<ca>\nCERTIFICATE\n</ca>\n'
 assert call('vpnconfig',{'kind':'openvpn','content':profile})['ok']
 ovpn=etc/'openvpn/studio.conf';assert ovpn.read_text().strip()==profile.strip()
 for directive in ['up /tmp/script','plugin /tmp/lib.so','config /etc/other.conf','script-security 2','log /etc/passwd','<connection>','<ca>\nunterminated']:
  assert not call('vpnconfig',{'kind':'openvpn','content':profile+directive+'\n'})['ok'],directive
  assert ovpn.read_text().strip()==profile.strip()

 for name in ['tailscale','zerotier']:
  init=etc/'init.d'/name;init.write_text(service.read_text());init.chmod(0o755)
 for name in ['tailscale','zerotier-cli']:
  core=tools/name;core.write_text('#!/bin/sh\nprintf "%s\\n" "$*" > "$MESH_ARGS"\nexit 0\n');core.chmod(0o755)
 meshargs=root/'meshargs'
 assert not call('mesh',{'kind':'zerotier','key':'abc; touch unsafe'},MESH_ARGS=str(meshargs))['ok']
 assert call('mesh',{'kind':'zerotier','key':'0123456789abcdef'},MESH_ARGS=str(meshargs))['ok']
 assert meshargs.read_text().strip()=='join 0123456789abcdef'
 payload={'kind':'tailscale','key':'tskey-auth-TESTKEY','routes':'192.168.1.0/24','exitnode':True,'accept':True}
 assert call('mesh',payload,MESH_ARGS=str(meshargs))['ok']
 args=meshargs.read_text();assert 'TESTKEY' not in args and 'file:' in args
 for key,value in [('key','tskey-auth-X;bad'),('routes','192.168.1.0/24;bad'),('exitnode','anything')]:
  assert not call('mesh',{**payload,key:value},MESH_ARGS=str(meshargs))['ok']
print('Backend contracts passed: allowlists, traversal/injection rejection, failures, Mihomo validation, backup, permissions, symlink rejection, cleanup.')
