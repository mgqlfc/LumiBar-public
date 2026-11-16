#!/usr/bin/python3
"""Fixed weather operations. stdin accepts configuration, never prints a key."""
import sys,json,os,re,subprocess,urllib.request,pathlib,tempfile,datetime
ENV=pathlib.Path('/etc/lumibar-weather.env')
SERVICE='lumibar-weather.service'
def run(args): return subprocess.run(args,check=True,capture_output=True,text=True).stdout.strip()
def config():
    return dict(line.split('=',1) for line in ENV.read_text().splitlines() if line and not line.startswith('#') and '=' in line)
def health():
    try:
        with urllib.request.urlopen('http://127.0.0.1:8767/healthz',timeout=3) as r:return json.load(r)
    except Exception:return {'configured':False,'reachable':False}
def main():
    action=sys.argv[1] if len(sys.argv)==2 else ''
    if action not in {'status','configure','restart','test','logs','requests'}:raise ValueError('Unsupported action')
    if action=='requests':
        try:items=json.loads(pathlib.Path('/var/lib/lumibar-weather/requests.json').read_text())
        except Exception:items=[]
        cutoff=datetime.datetime.now(datetime.timezone.utc)-datetime.timedelta(days=7)
        items=[r for r in items if datetime.datetime.fromisoformat(r['at'].replace('Z','+00:00'))>cutoff][-1000:]
        print(json.dumps({'scope':'weather-proxy','retentionDays':7,'maxRecords':1000,'total':len(items),'items':list(reversed(items))[:200]}));return
    if action=='configure':
        raw=sys.stdin.buffer.read(4097)
        if len(raw)>4096:raise ValueError('Configuration too large')
        data=json.loads(raw)
        if not isinstance(data,dict) or set(data)-{'apiHost','apiKey','dailyLimit'}:raise ValueError('Unknown configuration fields')
        old=ENV.read_bytes();values=config();host=data.get('apiHost',values.get('QWEATHER_API_HOST',''))
        if not isinstance(host,str) or not re.fullmatch(r'[a-z0-9]+(?:[a-z0-9.-]*[a-z0-9])?\.qweatherapi\.com',host) or '..' in host:raise ValueError('Invalid API host')
        key=data.get('apiKey','')
        if not isinstance(key,str) or key and not re.fullmatch(r'[A-Za-z0-9_-]{8,256}',key):raise ValueError('Invalid API key format')
        limit=data.get('dailyLimit',int(values.get('UPSTREAM_DAILY_LIMIT','1000')))
        if type(limit) is not int or not 1<=limit<=100000:raise ValueError('Invalid daily limit')
        values['QWEATHER_API_HOST']=host
        if key:values['QWEATHER_API_KEY']=key
        values['UPSTREAM_DAILY_LIMIT']=str(limit)
        text=''.join(k+'='+v+'\n' for k,v in values.items())
        fd,name=tempfile.mkstemp(prefix='.lumibar-weather-',dir='/etc')
        try:
            os.fchmod(fd,0o600)
            with os.fdopen(fd,'w') as f:f.write(text)
            os.replace(name,ENV)
            run(['/usr/bin/systemctl','restart',SERVICE])
        except Exception:
            ENV.write_bytes(old);ENV.chmod(0o600);run(['/usr/bin/systemctl','restart',SERVICE]);raise
        finally:
            if os.path.exists(name):os.unlink(name)
        print(json.dumps({'saved':True,'keyConfigured':bool(values.get('QWEATHER_API_KEY'))}));return
    if action=='restart':run(['/usr/bin/systemctl','restart',SERVICE]);print(json.dumps({'restarted':True}));return
    if action=='logs':
        print(run(['/usr/bin/journalctl','-u',SERVICE,'-n','80','--no-pager','-o','short-iso']));return
    if action=='test':
        try:
            with urllib.request.urlopen(urllib.request.Request('http://127.0.0.1:8767/v1/forecast?lat=39.36&lon=117.07',headers={'X-LumiBar-Client':'ServerCheck'}),timeout=15) as r:
                f=json.load(r)['forecast'];print(json.dumps({'ok':True,'source':f['source'],'hourCount':len(f['hours']),'dayCount':len(f['days']),'aqiStandard':f['aqiStandard'],'alertCount':len(f['alerts']),'notes':f.get('providerNote',[])},ensure_ascii=False))
        except Exception:print(json.dumps({'ok':False,'error':'weather_request_failed','health':health()}))
        return
    props=dict(line.split('=',1) for line in run(['/usr/bin/systemctl','show',SERVICE,'-p','ActiveState','-p','MainPID','-p','MemoryCurrent']).splitlines() if '=' in line)
    values=config()
    try:quota=json.loads(pathlib.Path('/var/lib/lumibar-weather/quota.json').read_text())
    except Exception:quota={'day':'','used':0}
    print(json.dumps({'service':props.get('ActiveState'),'pid':int(props.get('MainPID','0')),'memoryBytes':int(props['MemoryCurrent']) if props.get('MemoryCurrent','').isdigit() else 0,'apiHost':values.get('QWEATHER_API_HOST'),'keyConfigured':bool(values.get('QWEATHER_API_KEY')),'dailyLimit':int(values.get('UPSTREAM_DAILY_LIMIT','1000')),'quota':quota,'health':health(),'endpoint':'https://124.156.224.125/lumibar-weather/'}))
try:main()
except Exception as error:
    message=str(error) if isinstance(error,(ValueError,json.JSONDecodeError)) else 'Weather management failed'
    print(json.dumps({'error':message[:200]}));sys.exit(1)
