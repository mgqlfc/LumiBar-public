param([string]$Version='0.7.58',[switch]$IncludeAntigravity,[switch]$OfflineFrontend,[switch]$NoRestore,[string]$IsccPath)
$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$suffix=if($IncludeAntigravity){'-with-antigravity'}else{''}
$payload=Join-Path $workspace "work\package-$Version$suffix"
if(Test-Path -LiteralPath $payload){throw "Publish directory already exists: $payload"}
if($Version-notmatch'^\d+\.\d+\.\d+$'){throw 'Use a numeric major.minor.patch version.'}
if(-not$IsccPath){
    $compiler=Get-Command ISCC.exe -ErrorAction SilentlyContinue
    $candidates=@($env:LUMIBAR_ISCC,(Join-Path $workspace 'work\build-tools\InnoSetup7\ISCC.exe'))
    if($compiler){$candidates+=$compiler.Source}
    foreach($root in @(${env:ProgramFiles(x86)},$env:ProgramFiles)){if($root){foreach($major in @('7','6')){$candidates+=Join-Path $root "Inno Setup $major\ISCC.exe"}}}
    $IsccPath=$candidates|Where-Object {$_-and(Test-Path -LiteralPath $_)}|Select-Object -First 1
}
if(-not$IsccPath-or-not(Test-Path -LiteralPath $IsccPath)){throw 'Install Inno Setup or supply -IsccPath.'}
if(-not(Test-Path -LiteralPath (Join-Path $workspace 'work\TaskbarBridge\build\LumiBar.TaskbarBridge.v6.dll'))){
    & (Join-Path $workspace 'work\TaskbarBridge\build.ps1')
}
if(-not$NoRestore){
    & dotnet restore (Join-Path $workspace 'work\LumiBar\LumiBar.csproj') -r win-x64 -p:Platform=x64 -p:NuGetAudit=false
    if($LASTEXITCODE-ne0){throw 'Backend restore failed'}
}
& dotnet publish (Join-Path $workspace 'work\LumiBar\LumiBar.csproj') -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:DebugType=None -p:DebugSymbols=false -p:NuGetAudit=false -o $payload --no-restore -v:minimal
if($LASTEXITCODE -ne 0){throw 'Publish failed'}
foreach($fontAsset in @('SourceHanSansSC-VF.ttf','SourceHanSans-OFL.txt')){
    if(-not(Test-Path -LiteralPath (Join-Path $payload "Assets\Fonts\$fontAsset") -PathType Leaf)){
        throw "Publish is missing $fontAsset"
    }
}
if($IncludeAntigravity){
    $frontend=Join-Path $workspace 'work\frontends\antigravity'
    $plugin=Join-Path $payload 'frontends\antigravity'
    & (Join-Path $frontend 'build.ps1') -Mode Publish -Offline:$OfflineFrontend -OutputDirectory (Join-Path $plugin 'dist')
    Copy-Item -LiteralPath (Join-Path $frontend 'plugin.manifest.json') -Destination (Join-Path $plugin 'plugin.manifest.json') -Force
}
$forbidden=Get-ChildItem -LiteralPath $payload -Recurse -File | Where-Object { $_.Name -like 'PingFang*' -or $_.Extension -in '.pdb','.pem','.key','.env','.log','.nupkg' -or $_.Name -in 'settings.json','frontend-endpoint.json','antigravity-plugin.config.json' }
if($forbidden){throw 'Publish contains retired fonts, development files, or user data'}
$defines=@("/DAppVersion=$Version","/DPayloadDir=$payload");if($IncludeAntigravity){$defines+='/DIncludeAntigravity=1'}
& $IsccPath @defines (Join-Path $PSScriptRoot 'LumiBar.iss')
if($LASTEXITCODE -ne 0){throw 'Installer build failed'}
$setup=Join-Path $workspace "outputs\安装包\LumiBar-$Version-windows-x64$suffix-setup.exe"
Get-Item -LiteralPath $setup | Select-Object FullName,Length
Get-FileHash -LiteralPath $setup -Algorithm SHA256
