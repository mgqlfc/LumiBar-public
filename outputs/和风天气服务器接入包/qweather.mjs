const number=v=>v===null||v===undefined||v===''?null:Number.isFinite(Number(v))?Number(v):null;
const value=v=>number(v?.value);
const wall=t=>typeof t==='string'?t.replace(/(?:Z|[+-]\d{2}:\d{2})$/,''):null;
const pct=v=>number(v)===null?null:Math.max(0,Math.min(100,Number(v)*100));
export function conditionCode(code){
 const c=Number(code);
 if(c===100||c===150)return 0;if([101,102,151,152].includes(c))return 1;if([103,153].includes(c))return 2;if(c===104)return 3;
 if([302,303,304].includes(c))return 95;if([300,301].includes(c))return 80;if(c===309)return 51;if(c===305)return 61;if(c===306||c===314)return 63;if(c>=307&&c<=399)return c===313?66:65;
 if(c===400)return 71;if(c===407)return 85;if(c>=401&&c<=499)return [402,403,409,410].includes(c)?75:73;
 if([502,511,512,513].includes(c))return -1;if([503,504,507,508].includes(c))return -2;if(c>=500&&c<=515)return 45;return -3;
}
export function parseAlerts(doc,now=Date.now()){
 const raw=Array.isArray(doc?.alerts)?doc.alerts:[];const superseded=new Set(raw.flatMap(a=>a.messageType?.supersedes??[]));
 return raw.filter(a=>a.messageType?.code!=='cancel'&&!superseded.has(a.id)&&(!a.effectiveTime||Date.parse(a.effectiveTime)<=now)&&(!a.expireTime||Date.parse(a.expireTime)>now)).map(a=>({
  id:String(a.id??''),event:String(a.eventType?.name??'天气'),level:['red','orange','yellow','blue'].includes(a.color?.code)?a.color.code:'unknown',headline:String(a.headline??''),description:String(a.description??''),instruction:String(a.instruction??''),source:a.senderName?`${a.senderName} · 和风天气`:'和风天气',area:'',issuedAt:a.issuedTime??null,startsAt:a.effectiveTime??null,endsAt:a.expireTime??null,cancelled:false
 })).filter(a=>a.id).slice(0,32);
}
export function forecast(bundle,fetchedAt=new Date().toISOString()){
 const c=bundle.current;if(value(c?.temperature)===null)throw Error('invalid_current');
 const hours=(bundle.hourly?.hours??[]).filter(h=>wall(h.forecastTime)&&value(h.temperature)!==null).slice(0,24).map(h=>({time:wall(h.forecastTime),temperature:value(h.temperature),code:conditionCode(h.condition?.code),rain:pct(h.precipitation?.probability)??0,isDay:null}));
 const days=(bundle.daily?.days??[]).filter(d=>d.forecastStartTime&&value(d.temperatureMin)!==null&&value(d.temperatureMax)!==null).slice(0,10).map(d=>({date:d.forecastStartTime.slice(0,10),min:value(d.temperatureMin),max:value(d.temperatureMax),code:conditionCode(d.daytime?.condition?.code),rain:Math.max(pct(d.daytime?.precipitation?.probability)??0,pct(d.nighttime?.precipitation?.probability)??0)}));
 const indexes=bundle.air?.indexes??[];const index=indexes.find(i=>['chn-mee','cn-mee'].includes(i.code))??indexes.find(i=>i.code==='us-epa')??indexes.find(i=>i.code==='qaqi');
 const standard=index?.code==='us-epa'?'US AQI':index?.code==='qaqi'?'QAQI':'CN AQI';
 const pollutants=bundle.air?.pollutants??[];const concentration=code=>value(pollutants.find(p=>p.code===code)?.concentration);
 const sun=bundle.daily?.days?.[0]?.astro;const at=Date.parse(c.validTime);const sunrise=Date.parse(sun?.sunrise),sunset=Date.parse(sun?.sunset);
 const isDay=Number.isFinite(at)&&Number.isFinite(sunrise)&&Number.isFinite(sunset)?at>=sunrise&&at<sunset:[150,151,152,153].includes(Number(c.condition?.code))?false:null;
 const intervals=(bundle.minutely?.minutely??[]).filter(m=>Number.isFinite(Date.parse(m.fxTime))).slice(0,30).map(m=>({start:m.fxTime,end:new Date(Date.parse(m.fxTime)+300000).toISOString(),amountMm:number(m.precip)}));
 const attrs=[...new Set(Object.values(bundle).flatMap(d=>d?.metadata?.attributions??d?.refer?.sources??[]))].filter(s=>typeof s==='string').slice(0,20);
 return {temperature:value(c.temperature),feelsLike:value(c.feelsLike)??value(c.temperature),estimatedFeelsLike:value(c.feelsLike)===null,code:conditionCode(c.condition?.code),conditionText:c.condition?.text??null,humidity:(number(c.humidity)??0)*100,wind:(value(c.wind?.speed)??0)*(c.wind?.speed?.unit==='km/h'?1:3.6),windDirection:number(c.wind?.direction?.degree)??0,rain:pct(bundle.hourly?.hours?.[0]?.precipitation?.probability),hours,days,updated:fetchedAt,aqi:number(index?.aqi),aqiStandard:standard,aqiCategory:index?.category??null,pm25:concentration('pm2p5'),pm10:concentration('pm10'),airError:bundle.air?null:'空气质量暂不可用',source:'和风天气',timeZoneLabel:'城市当地时间',sunrise:wall(sun?.sunrise),sunset:wall(sun?.sunset),isDay,pressureHpa:value(c.pressure),uvIndex:number(c.uvIndex),alerts:parseAlerts(bundle.alerts),attributions:attrs,providerNote:bundle.notes??[],shortTermPrecipitation:intervals.length?{updatedAt:bundle.minutely.updateTime??fetchedAt,source:'和风天气',precisionNote:'5 分钟临近预报',intervals}:null};
}
