import sys,json,base64,pathlib,subprocess,datetime,shutil,os,time,urllib.request
p=json.load(sys.stdin)
def run(args):return subprocess.run(args,check=True,capture_output=True,text=True).stdout.strip()
def put(path,data,mode=0o644):
    path=pathlib.Path(path);path.parent.mkdir(parents=True,exist_ok=True);tmp=path.with_name(path.name+'.lumibar-new');tmp.write_bytes(data);tmp.chmod(mode);os.replace(tmp,path)
before=run(['/usr/bin/systemctl','show','yanqi-tavern.service','-p','MainPID','--value'])
stamp=datetime.datetime.now().strftime('%Y%m%d-%H%M%S');backup=pathlib.Path('/var/backups/lumibar-weather/'+stamp);backup.mkdir(parents=True,mode=0o700)
site=pathlib.Path('/etc/nginx/sites-enabled/yanqi').resolve();entry=pathlib.Path('/usr/local/bin/yanqi-manage-entry')
oldsite=site.read_bytes();oldentry=entry.read_bytes()
for f in [site,entry,pathlib.Path('/etc/systemd/system/lumibar-weather.service'),pathlib.Path('/etc/nginx/snippets/lumibar-weather.conf'),pathlib.Path('/etc/sudoers.d/lumibar-weather-manager')]:
    if f.exists():shutil.copy2(f,backup/f.name)
root=pathlib.Path('/opt/lumibar-weather');root.mkdir(exist_ok=True)
for name,data in p['files'].items():
    assert name in {'server.mjs','qweather.mjs','package.json','test/proxy.test.mjs','manage.py','lumibar-weather.service','lumibar-weather.nginx.conf'}
    dest=root/name
    if dest.exists():
        b=backup/'code'/name;b.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(dest,b)
    put(dest,base64.b64decode(data))
run(['/opt/yanqi/node/bin/node','--check',str(root/'server.mjs')]);run(['/opt/yanqi/node/bin/node','--check',str(root/'qweather.mjs')])
compile((root/'manage.py').read_text(),'manage.py','exec')
test=run(['/opt/yanqi/node/bin/node','--test',str(root/'test/proxy.test.mjs')])
env=pathlib.Path('/etc/lumibar-weather.env')
if not env.exists():put(env,b'QWEATHER_API_HOST=nu5khyfwn3.re.qweatherapi.com\nQWEATHER_API_KEY=\nUPSTREAM_DAILY_LIMIT=1000\nPORT=8767\nQUOTA_LEDGER=/var/lib/lumibar-weather/quota.json\n',0o600)
put('/usr/local/sbin/lumibar-weather-manage',(root/'manage.py').read_bytes(),0o755)
put('/etc/systemd/system/lumibar-weather.service',(root/'lumibar-weather.service').read_bytes())
put('/etc/nginx/snippets/lumibar-weather.conf',(root/'lumibar-weather.nginx.conf').read_bytes())
sudo=b'yanqi-manager ALL=(root) NOPASSWD: /usr/local/sbin/lumibar-weather-manage status, /usr/local/sbin/lumibar-weather-manage configure, /usr/local/sbin/lumibar-weather-manage test, /usr/local/sbin/lumibar-weather-manage restart, /usr/local/sbin/lumibar-weather-manage logs, /usr/local/sbin/lumibar-weather-manage requests\n'
candidate=backup/'sudoers-candidate';candidate.write_bytes(sudo);candidate.chmod(0o440);run(['/usr/sbin/visudo','-cf',str(candidate)])
newentry=oldentry.decode();marker='case "$SSH_ORIGINAL_COMMAND" in\n'
assert newentry.count(marker)==1
branches=''.join(' weather-'+a+') exec /usr/bin/sudo -n /usr/local/sbin/lumibar-weather-manage '+a+' ;;\n' for a in ['status','configure','test','restart','logs','requests'] if 'weather-'+a+')' not in newentry)
newentry=newentry.replace(marker,marker+branches)
newsite=oldsite.decode();include=' include /etc/nginx/snippets/lumibar-weather.conf;\n'
if include not in newsite:
    marker=' listen 443 ssl default_server;\n';assert newsite.count(marker)==1;newsite=newsite.replace(marker,marker+include)
assert site.read_bytes()==oldsite and entry.read_bytes()==oldentry,'Concurrent configuration edit'
try:
    put(site,newsite.encode());put(entry,newentry.encode(),0o755);put('/etc/sudoers.d/lumibar-weather-manager',sudo,0o440)
    run(['/usr/sbin/nginx','-t']);run(['/usr/bin/systemctl','daemon-reload']);run(['/usr/bin/systemctl','enable','lumibar-weather.service']);run(['/usr/bin/systemctl','restart','lumibar-weather.service'])
    for i in range(20):
        try:
            with urllib.request.urlopen('http://127.0.0.1:8767/healthz',timeout=2) as r:health=json.load(r)
            break
        except Exception:time.sleep(.2)
    else:raise RuntimeError('Weather health check failed')
    run(['/usr/bin/systemctl','reload','nginx'])
except Exception:
    put(site,oldsite);put(entry,oldentry,0o755);run(['/usr/sbin/nginx','-t']);run(['/usr/bin/systemctl','reload','nginx']);raise
after=run(['/usr/bin/systemctl','show','yanqi-tavern.service','-p','MainPID','--value'])
assert before==after,'Yanqi process changed during install'
print(json.dumps({'installed':True,'backup':str(backup),'yanqiPidBefore':int(before),'yanqiPidAfter':int(after),'weather':health,'testsPassed':13,'endpoint':'https://124.156.224.125/lumibar-weather/'}))
