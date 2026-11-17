param([string]$Version='0.7.52')
$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$payload=Join-Path $workspace "work\package-$Version"
$destination=Join-Path $workspace 'outputs\LumiBar-实机版'
$files=@('LumiBar.exe','LumiBar.dll','LumiBar.deps.json','LumiBar.runtimeconfig.json')
foreach($file in $files){if(-not(Test-Path -LiteralPath (Join-Path $payload $file))){throw "Missing release file: $file"}}
$data=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'LumiBar'
$settings=Join-Path $data 'settings.json'
$before=(Get-FileHash -LiteralPath $settings).Hash
$saved=Get-Content -LiteralPath $settings -Raw|ConvertFrom-Json
$explorerBefore=((Get-Process explorer).Id|Sort-Object)-join','
$env:LUMIBAR_DATA_DIR=$null;$env:LUMIBAR_TRACE_GLASS=$null
$helper=Start-Process -FilePath (Join-Path $payload 'LumiBar.exe') -ArgumentList '--shutdown' -WorkingDirectory $payload -WindowStyle Hidden -PassThru
$helper.WaitForExit();if($helper.ExitCode -ne 0){throw 'Graceful shutdown failed'}
$exe=Join-Path $destination 'LumiBar.exe'
$oldApps=@(Get-Process LumiBar -ErrorAction SilentlyContinue | Where-Object Path -eq $exe)
foreach($oldApp in $oldApps){if(-not$oldApp.WaitForExit(5000)){throw 'Previous instance is still running'}}
Copy-Item -Path (Join-Path $payload '*') -Destination $destination -Recurse -Force
$app=Start-Process -FilePath $exe -WorkingDirectory $destination -WindowStyle Hidden -PassThru
$deadline=[Environment]::TickCount64+10000
$descriptor=Join-Path $data 'frontend-endpoint.json';$ready=$false
do{if(Test-Path -LiteralPath $descriptor){try{$endpoint=Get-Content -LiteralPath $descriptor -Raw|ConvertFrom-Json;$ready=$endpoint.pid-eq$app.Id}catch{}};if($ready){break};Start-Sleep -Milliseconds 150}while([Environment]::TickCount64 -lt $deadline)
if($app.HasExited){throw 'Release exited during startup'}
if(-not$ready){throw 'Release endpoint unavailable'}
$pipe=[IO.Pipes.NamedPipeClientStream]::new('.',$endpoint.pipeName,[IO.Pipes.PipeDirection]::InOut);$pipe.Connect(2000)
$reader=[IO.StreamReader]::new($pipe);$writer=[IO.StreamWriter]::new($pipe,[Text.UTF8Encoding]::new($false));$writer.AutoFlush=$true
try{foreach($method in @('hello','state.get')){
    $writer.WriteLine((@{id='release';method=$method;params=@{protocolVersion=1}}|ConvertTo-Json -Compress))
    $read=$reader.ReadLineAsync();if(-not$read.Wait(5000)){throw 'Release state read timed out'};$reply=$read.Result|ConvertFrom-Json;if(-not$reply.ok){throw 'Release state rejected'}
};$state=$reply.data}finally{$writer.Dispose();$reader.Dispose();$pipe.Dispose()}
$settingsSame=(Get-FileHash -LiteralPath $settings).Hash -eq $before
if((((Get-Process explorer).Id|Sort-Object)-join',')-ne$explorerBefore){throw 'Explorer changed during release activation'}
$preferencesSame=$true
foreach($name in @('Theme','GlassOpacity','Glow','CoverSaturation','CoverGlow','Spectrum','Sensitivity','Animations','MusicWidth','Gap','LyricsFontSize','ChineseFontFamily','WesternFontFamily')){
    $key=$name.Substring(0,1).ToLowerInvariant()+$name.Substring(1);if($state.settings.$key-ne$saved.$name){$preferencesSame=$false}
}
$expectedAccent=if($saved.AccentColor){$saved.AccentColor}else{'blue'}
$result=[pscustomobject]@{version=$Version;pid=$app.Id;explorerPid=$explorerBefore;settingsFileUnchanged=$settingsSame;preferencesPreserved=$preferencesSame;accentCorrect=$state.settings.accentColor-eq$expectedAccent;payloadMatches=(Get-FileHash -LiteralPath (Join-Path $payload 'LumiBar.dll')).Hash-eq(Get-FileHash -LiteralPath (Join-Path $destination 'LumiBar.dll')).Hash;path=$exe}
$result|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $workspace "work\v$($Version.Split('.')[-1])-live-verification.json") -Encoding utf8
$result|ConvertTo-Json
if(-not$preferencesSame-or-not$result.accentCorrect-or-not$result.payloadMatches){throw 'Release verification failed'}
