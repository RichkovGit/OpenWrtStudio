"""Verify the deployment smoke script restores the selected theme on success/failure."""
from pathlib import Path
import tempfile,subprocess,os
source=Path(__file__).resolve().parents[1]/'scripts/smoke-router.sh'
with tempfile.TemporaryDirectory() as tmp:
 root=Path(tmp);tools=root/'bin';tools.mkdir();state=root/'theme';release=root/'release';release.write_text("DISTRIB_RELEASE='24.10.4'\n")
 def tool(name,body):
  p=tools/name;p.write_text('#!/bin/sh\n'+body);p.chmod(0o755)
 tool('id','echo 0\n')
 tool('opkg','exit "${INSTALL_EXIT:-0}"\n')
 tool('uci','''[ "$1" = -q ] && shift
case "$1" in
 changes|commit) exit 0;;
 get) case "$2" in luci.main.mediaurlbase) [ -f "$THEME_STATE" ] || exit 1; cat "$THEME_STATE";; studio.sentinel.enabled) echo 0;; esac;;
 set) printf '%s' "${2#*=}" > "$THEME_STATE";;
 delete) rm -f "$THEME_STATE";;
esac
''')
 tool('ubus','echo \'{"ok":true}\'\n');tool('jsonfilter','echo true\n')
 tool('uclient-fetch','[ "${TEMPLATE_FAIL:-0}" = 1 ] && exit 1\nprintf "%s" \'<link href="/luci-static/studio/studio.css">\' > "$3"\n')
 service=root/'rpcd';service.write_text('#!/bin/sh\nexit 0\n');service.chmod(0o755)
 script=root/'smoke';script.write_text(source.read_text().replace('/etc/openwrt_release',str(release)).replace('/etc/init.d/rpcd',str(service)).replace('/tmp/studio-',str(root)+'/studio-'))
 for original in ['original-theme',None]:
  for install_failure,template_failure in [('0','0'),('1','0'),('0','1')]:
   if original is None:state.unlink(missing_ok=True)
   else:state.write_text(original)
   r=subprocess.run(['sh',str(script),'theme.ipk','app.ipk'],capture_output=True,text=True,env={**os.environ,'PATH':str(tools)+':'+os.environ['PATH'],'THEME_STATE':str(state),'INSTALL_EXIT':install_failure,'TEMPLATE_FAIL':template_failure})
   assert r.returncode == (0 if install_failure==template_failure=='0' else 1),(r.stdout,r.stderr)
   assert (state.read_text() if state.exists() else None)==original,(r.stdout,r.stderr)
   assert 'Original theme restored and verified' in r.stdout
print('Theme restoration contracts passed: existing/absent theme, successful install, failed install, failed template rendering.')
