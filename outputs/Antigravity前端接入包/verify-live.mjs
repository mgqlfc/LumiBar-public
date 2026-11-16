import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { LumiBarClient } from './lumibar-client.mjs';
const endpoint=process.argv[2]??process.env.LUMIBAR_ENDPOINT;
if(!endpoint)throw new Error('Usage: node verify-live.mjs <frontend-endpoint.json>');
const client=new LumiBarClient();
try {
  const hello=await client.connect(endpoint);
  assert.equal(hello.protocolVersion,1);
  assert.equal(hello.capabilities.precipitationTrend,true);
  assert.equal(hello.capabilities.taskbarGeometry,true);
  assert.equal(hello.capabilities.eventsLongPoll,true);
  assert.equal(hello.capabilities.extendedMediaControls,true);
  assert.equal(hello.capabilities.systemFonts,true);
  const state=await client.request('state.get');
  const spectrum=await client.request('spectrum.get');
  const fonts=await client.request('fonts.list',{limit:500,refresh:true});
  assert.equal(fonts.source,'windows-directwrite');assert.ok(fonts.installedCount>0&&fonts.items.length>0);
  const selected=fonts.items.find(f=>f.family==='Microsoft YaHei')??fonts.items[0];
  const originalFont=state.settings.fontFamily??null;
  try {
    const saved=await client.request('settings.patch',{fontFamily:selected.displayName});
    assert.equal(saved.fontFamily,selected.family);
    assert.equal(JSON.parse(await readFile(join(dirname(endpoint),'settings.json'),'utf8')).FontFamily,selected.family);
    await assert.rejects(client.request('settings.patch',{fontFamily:'LumiBar-Nonexistent-Font-Test-8D2A'}),error=>error.code==='font_not_found');
    assert.equal((await client.request('state.get')).settings.fontFamily,selected.family);
  } finally {await client.request('settings.patch',{fontFamily:originalFont});}
  assert.equal((await client.request('state.get')).settings.fontFamily,originalFont);
  assert.equal(JSON.parse(await readFile(join(dirname(endpoint),'settings.json'),'utf8')).FontFamily,originalFont);
  await client.request('settings.patch',{theme:state.settings.theme});
  const events=await client.request('events.wait',{afterVersion:state.stateVersion,timeoutMs:1000});
  assert.equal(events.instanceId,hello.instanceId);
  assert.ok(events.version>state.stateVersion&&events.topics.some(t=>t==='settings'||t==='appearance'));
  const customPlan=await client.request('windows.planCapsules',{musicPreferredDip:300,musicMinDip:64,weatherPreferredDip:120,weatherMinDip:72,heightDip:34});
  assert.ok(['full','lite','hidden','unavailable','reservation-required'].includes(customPlan.layoutState));
  await assert.rejects(client.request('windows.planCapsules',{musicPreferredDip:48,musicMinDip:200}),error=>error.code==='invalid_params');
  assert.equal(spectrum.bands.length,20);
  assert.ok(spectrum.bands.every(x=>Number.isFinite(x)&&x>=0&&x<=1));
  const rain=state.weather.precipitationNextHour;
  if(rain) {
    assert.equal(rain.kind,'model-trend');assert.equal(rain.intervalMinutes,15);
    assert.equal(Date.parse(rain.windowEnd)-Date.parse(rain.windowStart),3600000);
    for(const interval of rain.intervals) {
      assert.equal(Date.parse(interval.end)-Date.parse(interval.start),900000);
      assert.ok(interval.amountMm===null||(Number.isFinite(interval.amountMm)&&interval.amountMm>=0));
    }
  }
  let measured=0;
  for(let i=0;i<20;i++) {
    const hints=await client.request('windows.hints');
    if(hints.geometryStatus==='measured') {
      measured++;
      const r=hints.blankZoneRect;assert.ok(r&&r.left<=r.right&&r.top<r.bottom);
      assert.ok(r.left>=hints.taskbar.left&&r.right<=hints.taskbar.right);
      assert.ok(hints.layoutRecommendation.requiredWidth>=0);
    } else { assert.equal(hints.blankZoneRect,null);assert.equal(hints.layoutRecommendation,null); }
    await new Promise(resolve=>setTimeout(resolve,150));
  }
  console.log(JSON.stringify({backendVersion:hello.backendVersion,protocolVersion:1,geometrySamples:20,measuredSamples:measured,precipitationAvailable:!!rain,precipitationComplete:rain?.complete??false,precipitationIntervals:rain?.intervals.length??0,spectrumBands:spectrum.bands.length,solarTimesAvailable:!!state.weather.forecast?.sunrise&&!!state.weather.forecast?.sunset,isDayAvailable:typeof state.weather.forecast?.isDay==='boolean',eventNotification:true,customLayoutState:customPlan.layoutState,installedFonts:fonts.installedCount,fontSelectionRoundTrip:true,fontSettingsRestored:true,checkedAt:new Date().toISOString()},null,2));
} finally {client.close();}
