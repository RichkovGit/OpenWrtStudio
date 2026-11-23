#!/usr/bin/env python3
"""Build architecture-independent opkg packages using only the Python standard library.
OpenWrt 24.10 IPK layout: gzipped tar containing debian-binary/control.tar.gz/data.tar.gz.
For apk-based firmware use the OpenWrt SDK and the package Makefiles.
"""
from pathlib import Path
import io,tarfile,gzip,json,hashlib
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'dist'
VERSION='1.0.0~rc1-1'
EPOCH=1781827200

def archive(files):
 buf=io.BytesIO()
 with tarfile.open(fileobj=buf,mode='w',format=tarfile.GNU_FORMAT) as tar:
  for name,data,mode in sorted(files):
   item=tarfile.TarInfo('./'+name);item.size=len(data);item.mode=mode;item.mtime=EPOCH;item.uid=item.gid=0;item.uname=item.gname='root';tar.addfile(item,io.BytesIO(data))
 return gzip.compress(buf.getvalue(),mtime=0)

def build(package,depends):
 folder=ROOT/package;files=[]
 for source,dest in [('htdocs','www'),('root',''),('ucode','usr/share/ucode/luci')]:
  for path in sorted((folder/source).rglob('*')):
   if path.is_file():
    name='/'.join(p for p in [dest,str(path.relative_to(folder/source))] if p)
    executable=name.startswith('usr/libexec/') or name.startswith('etc/uci-defaults/') or name.startswith('etc/init.d/')
    files.append((name,path.read_bytes(),0o755 if executable else 0o644))
 license_name='Apache-2.0' if package=='luci-theme-studio' else 'MIT'
 license_source=folder/'LICENSE'
 files.append(('usr/share/licenses/'+package+'/LICENSE',license_source.read_bytes(),0o644))
 if (folder/'NOTICE').exists():files.append(('usr/share/licenses/'+package+'/NOTICE',(folder/'NOTICE').read_bytes(),0o644))
 control=f'Package: {package}\nVersion: {VERSION}\nArchitecture: all\nMaintainer: OpenWrt Studio contributors\nSection: luci\nPriority: optional\nLicense: {license_name}\nDepends: {depends}\nInstalled-Size: {sum(len(x[1]) for x in files)}\nDescription: OpenWrt Studio LuCI release candidate\n'
 postinst='''#!/bin/sh
[ -n "$IPKG_INSTROOT" ] && exit 0
'''
 if package=='luci-app-studio':postinst+='sh /etc/uci-defaults/91-luci-app-studio || exit 1\nrm -f /etc/uci-defaults/91-luci-app-studio\n'
 if package=='luci-theme-studio':postinst+='sh /etc/uci-defaults/90-luci-theme-studio || exit 1\nrm -f /etc/uci-defaults/90-luci-theme-studio\n'
 postinst+='rm -f /tmp/luci-indexcache.*\n/etc/init.d/rpcd reload >/dev/null 2>&1 || true\nexit 0\n'
 controls=[('control',control.encode(),0o644),('postinst',postinst.encode(),0o755)]
 if package=='luci-app-studio':
  controls.append(('conffiles',b'/etc/config/studio\n',0o644))
  controls.append(('prerm',b'#!/bin/sh\n[ -n "$IPKG_INSTROOT" ] && exit 0\n/etc/init.d/studio-sentinel stop\n/etc/init.d/studio-sentinel disable\nexit 0\n',0o755))
 if package=='luci-theme-studio':
  postrm='''#!/bin/sh
[ -n "$IPKG_INSTROOT" ] && exit 0
if [ "$(uci -q get luci.main.mediaurlbase)" = /luci-static/studio ]; then
 uci set luci.main.mediaurlbase=/luci-static/bootstrap
fi
uci -q delete luci.themes.OpenWrtStudio
uci commit luci
exit 0
'''
  controls.append(('postrm',postrm.encode(),0o755))
 output=OUT/f'{package}_{VERSION}_all.ipk'
 output.write_bytes(archive([('debian-binary',b'2.0\n',0o644),('control.tar.gz',archive(controls),0o644),('data.tar.gz',archive(files),0o644)]))
 # Inspect packaged payload and ensure no test files or credentials entered the package.
 with tarfile.open(output) as tar:
  assert sorted(x.name for x in tar)==['./control.tar.gz','./data.tar.gz','./debian-binary']
  with tarfile.open(fileobj=io.BytesIO(tar.extractfile('./data.tar.gz').read())) as payload:
   assert all(not x.name.endswith('.py') for x in payload)
 return output

if __name__=='__main__':
 OUT.mkdir(exist_ok=True)
 packages=[build('luci-theme-studio','luci-base, luci-theme-bootstrap'),build('luci-app-studio','luci-base, rpcd, rpcd-mod-file, rpcd-mod-iwinfo, luci-mod-admin-full, jshn, luci-app-firewall, luci-app-opkg')]
 source=OUT/'OpenWrtStudio-LuCI-v1.0.0-rc1-source.tar.gz'
 members=[]
 for path in ROOT.rglob('*'):
  if path.is_file() and not any(x in path.relative_to(ROOT).parts for x in ['dist','artifacts','__pycache__']):
   members.append(('OpenWrtStudio-LuCI/'+str(path.relative_to(ROOT)),path.read_bytes(),0o755 if path.stat().st_mode&0o111 else 0o644))
 source.write_bytes(archive(members))
 packages.append(source)
 (OUT/'SHA256SUMS.txt').write_text(''.join(hashlib.sha256(p.read_bytes()).hexdigest()+'  '+p.name+'\n' for p in packages))
 for path in packages:print(path.name, path.stat().st_size,'bytes')
