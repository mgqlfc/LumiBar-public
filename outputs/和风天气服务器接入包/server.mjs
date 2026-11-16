import http from 'node:http';
import {readFileSync,writeFileSync,renameSync,mkdirSync} from 'node:fs';
import {dirname} from 'node:path';
import {pathToFileURL} from 'node:url';
import {isIP} from 'node:net';
import {randomUUID} from 'node:crypto';
import {forecast,parseAlerts} from './qweather.mjs';

export function coordinates(url){
 for(const key of ['lat','lon'])if(!url.searchParams.has(key)||!url.searchParams.get(key)?.trim())throw Error('invalid_coordinates');
 const lat=Number(url.searchParams.get('lat')),lon=Number(url.searchParams.get('lon'));
 if(!Number.isFinite(lat)||!Number.isFinite(lon)||Math.abs(lat)>90||Math.abs(lon)>180)throw Error('invalid_coordinates');
 return [lat.toFixed(2),lon.toFixed(2)];
}
export function createWeatherService(config,{fetchImpl=fetch,clock=()=>Date.now()}={}){
 const host=(config.host??'').toLowerCase();if(!/^[a-z0-9]+(?:[a-z0-9.-]*[a-z0-9])?\.qweatherapi\.com$/.test(host)||host.includes('..'))throw Error('invalid_host');
 const key=config.key??'';if(/[\r\n]/.test(key))throw Error('invalid_key');
 const cache=new Map(),pending=new Map(),rates=new Map();let active=0;
 const retention=7*86400000;let records=[];let flushTimer;
 if(config.journal)try{const saved=JSON.parse(readFileSync(config.journal,'utf8'));if(Array.isArray(saved))records=saved.filter(r=>Date.parse(r.at)>clock()-retention).slice(-1000);}catch{}
 function flush(){if(flushTimer)clearTimeout(flushTimer);flushTimer=undefined;if(!config.journal)return;try{mkdirSync(dirname(config.journal),{recursive:true});writeFileSync(config.journal+'.tmp',JSON.stringify(records),{mode:0o600});renameSync(config.journal+'.tmp',config.journal);}catch{console.error('request_journal_write_failed');}}
 function record(item){records=records.filter(r=>Date.parse(r.at)>clock()-retention);records.push(item);records=records.slice(-1000);if(config.journal&&!flushTimer)flushTimer=setTimeout(flush,200).unref();}
 let ledger={day:'',used:0};if(config.ledger)try{ledger=JSON.parse(readFileSync(config.ledger,'utf8'));}catch{}
 const limit=Number(config.dailyLimit??1000);if(!Number.isInteger(limit)||limit<1||limit>1000000)throw Error('invalid_daily_limit');
 function reserve(){const day=new Date(clock()).toISOString().slice(0,10);if(ledger.day!==day)ledger={day,used:0};if(ledger.used>=limit)throw Error('daily_limit');ledger.used++;
  if(config.ledger){mkdirSync(dirname(config.ledger),{recursive:true});writeFileSync(config.ledger+'.tmp',JSON.stringify(ledger),{mode:0o600});renameSync(config.ledger+'.tmp',config.ledger);}}
 const spec={current:['/weather/v1/current/',300],hourly:['/weather/v1/hourly/',3600],daily:['/weather/v1/daily/',10800],air:['/airquality/v1/current/',900],alerts:['/weatheralert/v1/current/',300],minutely:['/v7/minutely/5m',300]};
 async function load(kind,lat,lon,context){
  const id=`${kind}/${lat}/${lon}`;const hit=cache.get(id);if(hit&&hit.until>clock()){context.cacheHits++;if(hit.error)throw Error(hit.error);return hit;}
  if(pending.has(id)){context.coalesced++;return pending.get(id);}if(!key)throw Error('not_configured');if(pending.size>=16||active>=12)throw Error('busy');
  const task=Promise.resolve().then(async()=>{active++;try{
   reserve();context.upstreamRequests++;const [prefix,ttl]=spec[kind];const suffix=kind==='minutely'?`?location=${lon},${lat}&lang=zh`: `${lat}/${lon}?lang=zh&localTime=true${kind==='daily'?'&days=10':kind==='hourly'?'&hours=24':''}`;
   const response=await fetchImpl(`https://${host}${prefix}${suffix}`,{headers:{'X-QW-Api-Key':key,Accept:'application/json'},redirect:'error',signal:AbortSignal.timeout(10000)});
   if(!response.ok)throw Error(`upstream_${response.status}`);
   const reader=response.body.getReader();const buffers=[];let bytes=0;try{while(true){const part=await reader.read();if(part.done)break;bytes+=part.value.length;if(bytes>1500000)throw Error('upstream_too_large');buffers.push(Buffer.from(part.value));}}finally{await reader.cancel().catch(()=>{});}
   const doc=JSON.parse(Buffer.concat(buffers).toString('utf8'));if(doc.code&&doc.code!=='200')throw Error(`upstream_${/^[0-9]{3}$/.test(doc.code)?doc.code:'error'}`);
   const entry={data:doc,at:new Date(clock()).toISOString(),until:clock()+ttl*1000};cache.set(id,entry);if(cache.size>256)cache.delete(cache.keys().next().value);return entry;
  }catch(error){const safe=/^(upstream_[0-9]{3}|daily_limit|not_configured|busy)$/.test(error.message)?error.message:'upstream_unavailable';cache.set(id,{error:safe,until:clock()+60000});if(cache.size>256)cache.delete(cache.keys().next().value);throw Error(safe);
  }finally{active--;pending.delete(id);}});pending.set(id,task);return task;
 }
 function send(res,status,data){res.writeHead(status,{'Content-Type':'application/json; charset=utf-8','Cache-Control':'no-store','X-Content-Type-Options':'nosniff'});res.end(JSON.stringify(data));}
 async function handler(req,res){
  const context={cacheHits:0,coalesced:0,upstreamRequests:0};const started=performance.now();let endpoint='',tracked=false,sourceIp=String(req.headers['x-real-ip']??req.socket.remoteAddress??'');if(!isIP(sourceIp))sourceIp='unknown';
  const name=String(req.headers['x-lumibar-client']??'');const client=/^[A-Za-z0-9 ._/-]{1,64}$/.test(name)?name:'Unknown';
  res.once('finish',()=>{if(tracked)record({id:randomUUID(),at:new Date(clock()).toISOString(),client,sourceIp,endpoint,status:res.statusCode,durationMs:Math.round(performance.now()-started),...context});});
  try{
   const url=new URL(req.url,'http://localhost');endpoint=url.pathname;if(req.method!=='GET'){send(res,405,{error:'method_not_allowed'});return;}
   if(url.pathname==='/healthz'){send(res,200,{service:'lumibar-weather',version:'1.0.0',configured:!!key});return;}
   if(!['/v1/forecast','/v1/alerts'].includes(url.pathname)){send(res,404,{error:'not_found'});return;}
   const ip=sourceIp;const now=clock();let rate=rates.get(ip);if(!rate||rate.until<=now)rate={until:now+60000,count:0};rate.count++;rates.set(ip,rate);if(rates.size>1024)rates.delete(rates.keys().next().value);tracked=rate.count<=21;if(rate.count>20){send(res,429,{error:'rate_limit'});return;}
   const [lat,lon]=coordinates(url);
   if(url.pathname==='/v1/alerts'){const entry=await load('alerts',lat,lon,context);send(res,200,{alerts:parseAlerts(entry.data,clock()),attributions:entry.data.metadata?.attributions??[],updatedAt:entry.at});return;}
   const kinds=Object.keys(spec);const result=await Promise.allSettled(kinds.map(kind=>load(kind,lat,lon,context)));if(result[0].status!=='fulfilled')throw result[0].reason;
   const bundle={notes:[]};result.forEach((r,i)=>{if(r.status==='fulfilled')bundle[kinds[i]]=r.value.data;else bundle.notes.push(`${kinds[i]}: ${r.reason.message}`);});
   send(res,200,{forecast:forecast(bundle,result[0].value.at)});
  }catch(error){const code=/^(invalid_coordinates|not_configured|daily_limit|busy|upstream_[0-9]{3}|upstream_unavailable)$/.test(error.message)?error.message:'unavailable';send(res,code==='invalid_coordinates'?400:503,{error:code});}
 }
 return {handler,stats:()=>({cacheEntries:cache.size,pending:pending.size,upstreamRequestsToday:ledger.used,day:ledger.day,configured:!!key}),records:()=>records.slice().reverse(),flush};
}
if(process.argv[1]&&import.meta.url===pathToFileURL(process.argv[1]).href){
 const service=createWeatherService({host:process.env.QWEATHER_API_HOST,key:process.env.QWEATHER_API_KEY,ledger:process.env.QUOTA_LEDGER??'/var/lib/lumibar-weather/quota.json',journal:process.env.REQUEST_JOURNAL??'/var/lib/lumibar-weather/requests.json',dailyLimit:Number(process.env.UPSTREAM_DAILY_LIMIT??1000)});
 const server=http.createServer(service.handler);server.requestTimeout=15000;server.headersTimeout=10000;server.maxConnections=32;
 server.listen(Number(process.env.PORT??8767),'127.0.0.1',()=>console.log('LumiBar weather proxy listening on loopback'));
 process.on('SIGTERM',()=>{service.flush();server.close();setTimeout(()=>process.exit(0),2000).unref();});
}
