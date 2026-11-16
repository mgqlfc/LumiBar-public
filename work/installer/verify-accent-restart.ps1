param([Parameter(Mandatory)][string]$Exe,[Parameter(Mandatory)][string]$DataDir)
$ErrorActionPreference='Stop'
$oldData=$env:LUMIBAR_DATA_DIR;$oldTrace=$env:LUMIBAR_TRACE_GLASS
$saved=Get-Content -LiteralPath (Join-Path $DataDir 'settings.json') -Raw|ConvertFrom-Json
try{
    $env:LUMIBAR_DATA_DIR=$DataDir;$env:LUMIBAR_TRACE_GLASS=$null
    $app=Start-Process -FilePath $Exe -ArgumentList '--frontend-backend' -WorkingDirectory (Split-Path $Exe) -WindowStyle Hidden -PassThru
    $descriptor=Join-Path $DataDir 'frontend-endpoint.json';$deadline=[Environment]::TickCount64+15000;$ready=$false
    do{if(Test-Path -LiteralPath $descriptor){try{$endpoint=Get-Content -LiteralPath $descriptor -Raw|ConvertFrom-Json;$ready=$endpoint.pid-eq$app.Id}catch{}};if($ready){break};Start-Sleep -Milliseconds 150}while([Environment]::TickCount64-lt$deadline)
    if(-not$ready){throw 'Restart endpoint unavailable'}
    $pipe=[IO.Pipes.NamedPipeClientStream]::new('.',$endpoint.pipeName,[IO.Pipes.PipeDirection]::InOut);$pipe.Connect(2000)
    $reader=[IO.StreamReader]::new($pipe);$writer=[IO.StreamWriter]::new($pipe,[Text.UTF8Encoding]::new($false));$writer.AutoFlush=$true
    try{
        foreach($method in @('hello','state.get','app.shutdown')){
            $writer.WriteLine((@{id='restart';method=$method;params=@{protocolVersion=1}}|ConvertTo-Json -Compress))
            $read=$reader.ReadLineAsync();if(-not$read.Wait(5000)){throw 'Restart RPC timeout'};$reply=$read.Result|ConvertFrom-Json;if(-not$reply.ok){throw 'Restart RPC rejected'}
            if($method-eq'state.get'){$state=$reply.data}
        }
    }finally{$writer.Dispose();$reader.Dispose();$pipe.Dispose()}
    $result=[pscustomobject]@{accentRestored=$state.settings.accentColor-eq$saved.AccentColor;themeRestored=$state.settings.theme-eq$saved.Theme;paletteHasThreeColors=@($state.appearance.accentPalette.colors).Count-eq 3;cleanExit=$app.WaitForExit(5000)}
    $result|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $DataDir 'accent-restart-result.json') -Encoding utf8
    $result|ConvertTo-Json
    if($result.PSObject.Properties.Value-contains$false){throw 'Accent restart check failed'}
}finally{$env:LUMIBAR_DATA_DIR=$oldData;$env:LUMIBAR_TRACE_GLASS=$oldTrace}
