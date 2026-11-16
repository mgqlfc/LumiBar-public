param([Parameter(Mandatory)][string]$DataDir)
$ErrorActionPreference='Stop'
$endpoint=Get-Content -LiteralPath (Join-Path $DataDir 'frontend-endpoint.json') -Raw|ConvertFrom-Json
$clients=@();$counter=0;$registration=$null;$actions=[Collections.Generic.List[object]]::new()
function Connect {
    $pipe=[IO.Pipes.NamedPipeClientStream]::new('.',$endpoint.pipeName,[IO.Pipes.PipeDirection]::InOut);$pipe.Connect(3000)
    $client=@{pipe=$pipe;reader=[IO.StreamReader]::new($pipe);writer=[IO.StreamWriter]::new($pipe,[Text.UTF8Encoding]::new($false))};$client.writer.AutoFlush=$true;$script:clients+=,$client
    $hello=Rpc $client 'hello' @{protocolVersion=1;clientName='codex-menu-inspection'}
    if(-not$hello.ok){throw 'Handshake rejected'}
    return $client
}
function Rpc($Client,[string]$Method,$Params){
    $script:counter++;$Client.writer.WriteLine((@{id="menu-$counter";method=$Method;params=$Params}|ConvertTo-Json -Depth 12 -Compress))
    $read=$Client.reader.ReadLineAsync();if(-not$read.Wait(8000)){throw "Menu RPC timed out: $Method"};return $read.Result|ConvertFrom-Json
}
function Auth {return @{registrationId=$registration.registrationId;registrationToken=$registration.registrationToken}}
function SettingsHash {$file=Join-Path $DataDir 'settings.json';if(Test-Path -LiteralPath $file){return (Get-FileHash -LiteralPath $file).Hash};return 'absent'}
try{
    $control=Connect;$callback=Connect
    $before=SettingsHash
    $bad=Rpc $control 'menus.register' @{pluginId='codex.invalid';displayName='Invalid';items=@(@{id='bad';label='Bad';locations=@('music')})}
    if($bad.ok-or$bad.error.code-ne'invalid_params'){throw 'Capsule menu location was not rejected'}
    $definition=@{pluginId='codex.menu-inspection';displayName='插件菜单测试';navigationTitle='菜单测试';navigationDescription='核对插件操作、点击响应和动态状态。';items=@(
        @{id='open-settings';label='打开测试设置'},
        @{id='pin';label='固定歌词';checked=$false},
        @{kind='separator'},
        @{id='disabled';label='暂不可用';enabled=$false},
        @{id='tools';label='更多操作';kind='submenu';children=@(@{id='refresh';label='刷新测试内容'})}
    )}
    $registered=Rpc $control 'menus.register' $definition;if(-not$registered.ok){throw ('Register failed: '+$registered.error.code)};$registration=$registered.data
    $renew=Rpc $callback 'menus.renew' (Auth);if(-not$renew.ok){throw 'Cross-pipe menu renewal failed'}
    @{pid=$PID;registered=$true;crossPipeRenewed=$renew.ok;capsuleLocationRejected=(-not$bad.ok);settingsUnchanged=(SettingsHash)-eq$before}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $DataDir 'menu-probe-ready.json') -Encoding utf8
    Write-Output 'Menu probe ready; credentials stay in memory.'
    $cursor=0L;$lastRenew=[Environment]::TickCount64;$deadline=$lastRenew+1200000
    while([Environment]::TickCount64-lt$deadline-and-not(Test-Path -LiteralPath (Join-Path $DataDir 'stop-menu-probe'))){
        if([Environment]::TickCount64-$lastRenew-ge 3000){$renew=Rpc $control 'menus.renew' (Auth);if(-not$renew.ok){throw 'Menu renewal rejected'};$lastRenew=[Environment]::TickCount64}
        $readArgs=Auth;$readArgs.afterSequence=$cursor;$readArgs.limit=32
        $reply=Rpc $callback 'menus.actions.read' $readArgs;if(-not$reply.ok){throw ('Action read failed: '+$reply.error.code)}
        foreach($action in $reply.data.actions){
            $actions.Add($action);$cursor=[long]$action.sequence
            if($action.actionId-eq'pin'){$definition.items[1].checked=[bool]$action.proposedChecked}
            $definition.navigationDescription='已响应：'+$action.actionId
            $update=Auth;$update.definition=$definition;$updated=Rpc $control 'menus.update' $update;if(-not$updated.ok){throw 'Dynamic menu update rejected'}
            $ack=Auth;$ack.throughSequence=$cursor;$ackResult=Rpc $callback 'menus.actions.ack' $ack;if(-not$ackResult.ok){throw 'Action acknowledgement rejected'}
            $actions.ToArray()|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $DataDir 'menu-probe-actions.json') -Encoding utf8
        }
        Start-Sleep -Milliseconds 200
    }
}finally{
    if($null-ne$registration){try{$removed=Rpc $control 'menus.unregister' (Auth)}catch{}}
    @{received=$actions.Count;settingsUnchanged=(SettingsHash)-eq$before;unregistered=$removed.ok}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $DataDir 'menu-probe-final.json') -Encoding utf8
    foreach($client in $clients){$client.writer.Dispose();$client.reader.Dispose();$client.pipe.Dispose()}
}
