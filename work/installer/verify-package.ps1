param([Parameter(Mandatory)][string]$Installer,[Parameter(Mandatory)][string]$Payload)
$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$testRoot=Join-Path $workspace ('work\installer-qa-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
$appDir=Join-Path $testRoot 'app';$dataDir=Join-Path $testRoot 'data'
New-Item -ItemType Directory -Path $dataDir -Force|Out-Null
$oldData=$env:LUMIBAR_DATA_DIR
$runKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$oldRun=(Get-ItemProperty -LiteralPath $runKey -Name LumiBar -ErrorAction SilentlyContinue).LumiBar
$shortcutHashes=@{};$programs=[Environment]::GetFolderPath('Programs')
foreach($path in @((Join-Path $programs 'LumiBar.lnk'),(Join-Path $programs 'LumiBar\LumiBar.lnk'))){if(Test-Path -LiteralPath $path){$shortcutHashes[$path]=(Get-FileHash -LiteralPath $path).Hash}}
if(Test-Path -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{5F98F556-86DE-48A8-B81B-5296D2A1C9A4}_is1'){throw 'Existing installed LumiBar found; use a separate test account for isolated installation QA.'}
$results=[Collections.Generic.List[object]]::new();$server=$null
function Check([bool]$Ok,[string]$Name){$results.Add([pscustomobject]@{pass=$Ok;name=$Name});if(-not $Ok){throw "FAIL: $Name"}}
function Execute([string]$File,[string[]]$Arguments){$p=Start-Process -FilePath $File -ArgumentList $Arguments -WorkingDirectory (Split-Path $File) -WindowStyle Hidden -PassThru;$p.WaitForExit();return $p.ExitCode}
function ReadSettings([string]$Descriptor){
    $endpoint=Get-Content -LiteralPath $Descriptor -Raw|ConvertFrom-Json
    $pipe=[IO.Pipes.NamedPipeClientStream]::new('.',$endpoint.pipeName,[IO.Pipes.PipeDirection]::InOut);$pipe.Connect(2000)
    $reader=[IO.StreamReader]::new($pipe,[Text.UTF8Encoding]::new($false),$false,4096,$true);$writer=[IO.StreamWriter]::new($pipe,[Text.UTF8Encoding]::new($false),4096,$true);$writer.AutoFlush=$true
    try{foreach($method in @('hello','state.get')){
        $writer.WriteLine((@{id='installer-defaults';method=$method;params=@{protocolVersion=1}}|ConvertTo-Json -Compress))
        $read=$reader.ReadLineAsync();if(-not$read.Wait(8000)){throw "Installed settings read timed out: $method"}
        $reply=$read.Result|ConvertFrom-Json;if(-not$reply.ok){throw 'Installed settings read rejected'}
    };return $reply.data.settings}finally{$writer.Dispose();$reader.Dispose();$pipe.Dispose()}
}
try{
    $env:LUMIBAR_DATA_DIR=$dataDir
    Check ((Execute $Installer @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS','/NOSHORTCUTS=1',"/DIR=`"$appDir`"",'/GROUP=LumiBar-QA',"/LOG=`"$testRoot\install.log`"")) -eq 0) 'Per-user silent installation'
    $exe=Join-Path $appDir 'LumiBar.exe'
    Check (Test-Path -LiteralPath $exe) 'Installed executable exists'
    Check ((Get-FileHash (Join-Path $Payload 'LumiBar.dll')).Hash -eq (Get-FileHash (Join-Path $appDir 'LumiBar.dll')).Hash) 'Installed application matches published payload'
    Check (Test-Path -LiteralPath (Join-Path $appDir 'LumiBar.pri')) 'Merged WinUI theme resources are installed'
    $pluginRelative='frontends\antigravity\dist';$pluginExe=Join-Path $appDir "$pluginRelative\AntigravityFrontend.exe"
    $pluginConfigPath=Join-Path $appDir "$pluginRelative\antigravity-plugin.config.json";$pluginConfigHash=$null
    if(Test-Path -LiteralPath $pluginExe){
        Check ((Test-Path -LiteralPath (Join-Path $appDir "$pluginRelative\Microsoft.Graphics.Canvas.dll"))-and(Test-Path -LiteralPath (Join-Path $appDir "$pluginRelative\Microsoft.Graphics.Canvas.Interop.dll"))) 'Installed frontend includes native and managed Win2D components'
        $factory=Get-Content -LiteralPath (Join-Path $appDir "$pluginRelative\antigravity-plugin.defaults.json") -Raw|ConvertFrom-Json
        Check ($factory.ConfigVersion-eq7-and$factory.LyricsFlyoutWidth-eq512-and$factory.LyricsFlyoutHeight-eq384-and-not(Test-Path -LiteralPath $pluginConfigPath)) 'Installed frontend ships v7 compact defaults without a user config'
        $graphics=Join-Path $testRoot 'frontend-graphics'
        Check ((Execute $pluginExe @('--graphics-self-test',('"'+$graphics+'"')))-eq0-and(Get-Content -LiteralPath (Join-Path $graphics 'graphics-result.json') -Raw|ConvertFrom-Json).pass) 'Installed frontend actually renders all Win2D effect probes'
    }
    $defaultsPath=Join-Path $appDir 'Assets\default-settings.json'
    Check ((Test-Path -LiteralPath $defaultsPath)-and(Get-FileHash -LiteralPath $defaultsPath).Hash-eq(Get-FileHash -LiteralPath (Join-Path $Payload 'Assets\default-settings.json')).Hash) 'Installed default settings manifest matches the payload'
    Check ((Execute $exe @('--frontend-backend','--inspect-glass','--inspect-release')) -eq 0) 'Installed settings UI opens and closes with its native theme resources'
    $zip=Join-Path $testRoot 'diagnostics.zip'
    Check ((Execute $exe @('--frontend-backend','--inspect-export-diagnostics',"`"$zip`"")) -eq 0) 'Installed self-contained executable starts and exits cleanly'
    Check (Test-Path -LiteralPath $zip) 'Installed executable exports diagnostics'
    $server=Start-Process -FilePath $exe -ArgumentList '--frontend-backend' -WorkingDirectory $appDir -WindowStyle Hidden -PassThru
    $descriptor=Join-Path $dataDir 'frontend-endpoint.json';$deadline=[Environment]::TickCount64+15000
    $ready=$false
    do{if(Test-Path -LiteralPath $descriptor){try{$ready=(Get-Content -LiteralPath $descriptor -Raw|ConvertFrom-Json).pid-eq$server.Id}catch{}};if($ready){break};Start-Sleep -Milliseconds 150}while([Environment]::TickCount64 -lt $deadline)
    Check $ready 'Local backend becomes ready'
    $actualSettings=ReadSettings $descriptor;$expectedDefaults=Get-Content -LiteralPath $defaultsPath -Raw|ConvertFrom-Json;$defaultsMatch=$true
    foreach($property in $expectedDefaults.PSObject.Properties){
        $name=$property.Name.Substring(0,1).ToLowerInvariant()+$property.Name.Substring(1);$actual=$actualSettings.$name;$expected=$property.Value
        if($expected-is[double]-or$expected-is[long]-or$expected-is[int]){if($null-eq$actual-or[Math]::Abs([double]$actual-[double]$expected)-gt 1e-8){$defaultsMatch=$false}}
        elseif($null-eq$expected){if($null-ne$actual){$defaultsMatch=$false}}
        elseif($expected-ne$actual){$defaultsMatch=$false}
    }
    Check $defaultsMatch 'Fresh installed backend uses all captured shipping defaults'
    Check ($null-eq$actualSettings.city-and@($actualSettings.favorites).Count-eq 0) 'Fresh installation has no bundled personal location or saved cities'
    Check ((Execute $exe @('--frontend-backend','--shutdown')) -eq 0) 'Graceful shutdown helper acknowledges request'
    Check ($server.WaitForExit(5000)) 'Backend and native shutdown leave no empty process'
    $sentinel=Join-Path $dataDir 'settings-preservation.txt';[IO.File]::WriteAllText($sentinel,'preserve-user-data')
    $savedSettings=Join-Path $dataDir 'settings.json';[IO.File]::WriteAllText($savedSettings,'{"GlassOpacity":0.73,"Glow":0.41,"Gap":12,"AutoStart":false,"AutoLocateCity":false,"AccentColor":"butter"}');$settingsHash=(Get-FileHash -LiteralPath $savedSettings).Hash
    if(Test-Path -LiteralPath $pluginExe){[IO.File]::WriteAllText($pluginConfigPath,'{"ConfigVersion":7,"LyricsFlyoutWidth":444,"LyricsFlyoutHeight":410}');$pluginConfigHash=(Get-FileHash -LiteralPath $pluginConfigPath).Hash}
    Check ((Execute $Installer @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS','/NOSHORTCUTS=1',"/DIR=`"$appDir`"",'/GROUP=LumiBar-QA',"/LOG=`"$testRoot\upgrade.log`"")) -eq 0) 'Reinstallation / upgrade completes'
    Check ((Get-Content -LiteralPath $sentinel -Raw) -eq 'preserve-user-data') 'Upgrade preserves user data'
    Check ((Get-FileHash -LiteralPath $savedSettings).Hash-eq$settingsHash) 'Upgrade does not replace existing preferences with new defaults'
    if($pluginConfigHash){Check ((Get-FileHash -LiteralPath $pluginConfigPath).Hash-eq$pluginConfigHash) 'Upgrade preserves existing frontend v7 dimensions'}
    $uninstaller=Join-Path $appDir 'unins000.exe'
    Check ((Execute $uninstaller @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/LOG=`"$testRoot\uninstall.log`"")) -eq 0) 'Silent uninstall completes'
    Check (-not(Test-Path -LiteralPath $exe)) 'Uninstall removes application files'
    Check ((Get-Content -LiteralPath $sentinel -Raw) -eq 'preserve-user-data') 'Uninstall preserves user data'
    $changed=@($shortcutHashes.Keys|Where-Object {-not(Test-Path -LiteralPath $_)-or(Get-FileHash -LiteralPath $_).Hash -ne $shortcutHashes[$_]})
    Check ($changed.Count -eq 0) 'Installation QA preserves existing Start Menu shortcuts'
}finally{
    try{
        # Clean only this run's isolated installation if a check failed early.
        $env:LUMIBAR_DATA_DIR=$dataDir
        if($null-ne$server-and-not$server.HasExited){$null=Execute (Join-Path $appDir 'LumiBar.exe') @('--frontend-backend','--shutdown');$null=$server.WaitForExit(5000)}
        $cleanupUninstaller=Join-Path $appDir 'unins000.exe'
        if(Test-Path -LiteralPath $cleanupUninstaller){$null=Execute $cleanupUninstaller @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')}
    }catch{Write-Warning ("Isolated QA cleanup: "+$_.Exception.Message)}
    $env:LUMIBAR_DATA_DIR=$oldData
    if($null -ne $oldRun){Set-ItemProperty -LiteralPath $runKey -Name LumiBar -Value $oldRun}else{Remove-ItemProperty -LiteralPath $runKey -Name LumiBar -ErrorAction SilentlyContinue}
    $results|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $testRoot 'results.json') -Encoding utf8
    $results|Format-Table -AutoSize
    Write-Output "Evidence: $testRoot"
}
