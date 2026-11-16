param([Parameter(Mandatory)][string]$DataDir)
$ErrorActionPreference='Stop'
$endpoint=Get-Content -LiteralPath (Join-Path $DataDir 'frontend-endpoint.json') -Raw|ConvertFrom-Json
$pipe=[IO.Pipes.NamedPipeClientStream]::new('.',$endpoint.pipeName,[IO.Pipes.PipeDirection]::InOut);$pipe.Connect(2000)
$reader=[IO.StreamReader]::new($pipe,[Text.UTF8Encoding]::new($false),$false,4096,$true)
$writer=[IO.StreamWriter]::new($pipe,[Text.UTF8Encoding]::new($false),4096,$true);$writer.AutoFlush=$true
$results=[Collections.Generic.List[object]]::new();$sequence=0
function Rpc([string]$Method,$Params){
    $script:sequence++;$writer.WriteLine((@{id="accent-$sequence";method=$Method;params=$Params}|ConvertTo-Json -Depth 8 -Compress))
    $read=$reader.ReadLineAsync();if(-not$read.Wait(5000)){throw 'Accent RPC timed out'}
    return $read.Result|ConvertFrom-Json
}
function Check([bool]$Ok,[string]$Name){$results.Add([pscustomobject]@{pass=$Ok;name=$Name});if(-not$Ok){throw "FAIL: $Name"}}
try{
    Check (Rpc 'hello' @{protocolVersion=1}).ok 'Handshake'
    $before=(Rpc 'state.get' @{}).data.settings
    $ids=@('blue','mint','lime','butter','apricot','peach','berry','lavender','periwinkle')
    $null=Rpc 'settings.patch' @{accentColor='mint'}
    foreach($id in $ids){
        Check (Rpc 'settings.patch' @{accentColor=$id}).ok "Accept $id"
        $state=(Rpc 'state.get' @{}).data
        $saved=Get-Content -LiteralPath (Join-Path $DataDir 'settings.json') -Raw|ConvertFrom-Json
        Check ($state.settings.accentColor-eq$id-and$state.appearance.accentColor-eq$id-and$saved.AccentColor-eq$id) "Apply and persist $id"
    }
    $invalid=Rpc 'settings.patch' @{accentColor='invalid-accent'}
    Check (-not$invalid.ok) 'Reject unsupported accent'
    for($round=0;$round-lt 5;$round++){foreach($id in $ids){$reply=Rpc 'settings.patch' @{accentColor=$id};if(-not$reply.ok){throw 'Rapid accent request failed'}}}
    Check (Rpc 'settings.patch' @{accentColor='periwinkle'}).ok 'Repeated unchanged request'
    $after=(Rpc 'state.get' @{}).data.settings
    Check ($after.accentColor-eq'periwinkle') '45 rapid changes settle to final selection'
    $unchanged=$true;foreach($name in @('glassOpacity','glow','coverSaturation','coverGlow','spectrum','theme','musicWidth','gap')){if($before.$name-ne$after.$name){$unchanged=$false}}
    Check $unchanged 'Accent changes preserve glass, cover, audio and layout settings'
    $null=Rpc 'settings.patch' @{accentColor='butter'}
    $log=Get-Content -LiteralPath (Join-Path $DataDir 'diagnostics.log') -Raw
    Check ($log-notmatch'\[accent-transition\]|\[unhandled\]|\[fatal\]') 'No accent animation or fatal errors'
}finally{
    $writer.Dispose();$reader.Dispose();$pipe.Dispose()
    $results|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $DataDir 'accent-results.json') -Encoding utf8
    $results|Format-Table -AutoSize
}
