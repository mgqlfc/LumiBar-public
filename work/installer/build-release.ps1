param([string]$Version='0.7.48')
$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$payload=Join-Path $workspace "work\package-$Version"
if(Test-Path -LiteralPath $payload){throw "Publish directory already exists: $payload"}
& dotnet publish (Join-Path $workspace 'work\LumiBar\LumiBar.csproj') -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:DebugType=None -p:DebugSymbols=false -p:NuGetAudit=false -o $payload --no-restore -v:minimal
if($LASTEXITCODE -ne 0){throw 'Publish failed'}
$forbidden=Get-ChildItem -LiteralPath $payload -Recurse -File | Where-Object { $_.Extension -in '.pdb','.pem','.key','.env','.log' -or $_.Name -in 'settings.json','frontend-endpoint.json' }
if($forbidden){throw 'Publish contains development or user data files'}
& (Join-Path $workspace 'work\build-tools\InnoSetup7\ISCC.exe') "/DAppVersion=$Version" "/DPayloadDir=$payload" (Join-Path $PSScriptRoot 'LumiBar.iss')
if($LASTEXITCODE -ne 0){throw 'Installer build failed'}
$setup=Join-Path $workspace "outputs\安装包\LumiBar-$Version-windows-x64-setup.exe"
Get-Item -LiteralPath $setup | Select-Object FullName,Length
Get-FileHash -LiteralPath $setup -Algorithm SHA256
