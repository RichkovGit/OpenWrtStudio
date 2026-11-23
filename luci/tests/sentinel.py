from pathlib import Path
import tempfile,subprocess,os
source=Path(__file__).resolve().parents[1]/'luci-app-studio/root/usr/libexec/studio-sentinel'
with tempfile.TemporaryDirectory() as tmp:
 root=Path(tmp);tools=root/'bin';tools.mkdir();etc=root/'init.d';etc.mkdir()
 functions=root/'functions.sh';functions.write_text('''config_load() { :; }
config_get() { case "$3" in interval) export "$1=30";; failures) export "$1=3";; cooldown) export "$1=300";; proxy) export "$1=${TEST_PROXY:-none}";; esac; }
config_get_bool() { export "$1=1"; }
''')
 def tool(name,text):
  path=tools/name;path.write_text('#!/bin/sh\n'+text);path.chmod(0o755)
 tool('ping','exit "${PING_EXIT:-0}"\n');tool('nslookup','exit "${DNS_EXIT:-1}"\n');tool('ubus','echo \'{"up":false}\'\n');tool('jsonfilter','echo false\n');tool('date','echo 10000\n');tool('logger','printf "%s\\n" "$*" >> "$LOG_FILE"\n')
 for name in ['dnsmasq','mihomo']:
  path=etc/name;path.write_text('#!/bin/sh\n[ "$1" = running ] && exit 1\nprintf "%s\\n" "'+name+' $1" >> "$ACTION_FILE"\n');path.chmod(0o755)
 script=root/'sentinel';script.write_text(source.read_text().replace('/lib/functions.sh',str(functions)).replace('/etc/init.d',str(etc)).replace(' sleep "$interval"',' cycles=$((cycles + 1)); [ "$cycles" -ge 8 ] && exit 0'))
 actions=root/'actions';log=root/'log'
 def run(**env):
  actions.write_text('');log.write_text('');subprocess.run(['sh',str(script)],env={**os.environ,'PATH':str(tools)+':'+os.environ['PATH'],'LOG_FILE':str(log),'ACTION_FILE':str(actions),**env},check=True,timeout=5);return actions.read_text(),log.read_text()
 result,logs=run();assert result=='dnsmasq restart\n';assert 'local_dns' in logs
 result,logs=run(PING_EXIT='1');assert result=='';assert 'wan_down' in logs
 result,logs=run(PING_EXIT='1',TEST_PROXY='mihomo');assert result=='mihomo restart\n';assert 'proxy_stopped' in logs
 result,logs=run(DNS_EXIT='0');assert result=='';assert 'healthy' in logs
print('Sentinel contracts passed: repeated failures, cooldown, DNS recovery, WAN observation, stopped proxy recovery, healthy state.')
