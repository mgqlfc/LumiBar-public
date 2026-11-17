param(
    [ValidateSet('Build','Publish')][string]$Mode='Build',
    [string]$OutputDirectory=(Join-Path $PSScriptRoot 'dist'),
    [string]$PackageFeed=(Join-Path $PSScriptRoot '..\..\offline-nuget\antigravity'),
    [string]$PackagesDirectory,
    [switch]$Offline,[switch]$NoRestore,[switch]$GraphicsSelfTest
)
$ErrorActionPreference='Stop'
$project=Join-Path $PSScriptRoot 'AntigravityFrontend.csproj'
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory);$PackageFeed=[IO.Path]::GetFullPath($PackageFeed)
$exe=Join-Path $OutputDirectory 'AntigravityFrontend.exe'
if(@(Get-Process AntigravityFrontend -ErrorAction SilentlyContinue|Where-Object Path -eq $exe).Count){throw 'The output frontend is running. Use another output directory or close that instance first.'}
$configPath=Join-Path $OutputDirectory 'antigravity-plugin.config.json'
$configHash=if(Test-Path -LiteralPath $configPath){(Get-FileHash -LiteralPath $configPath).Hash}else{$null}
if($Offline){
    $manifest=Get-Content -LiteralPath (Join-Path $PackageFeed 'packages-manifest.json') -Raw|ConvertFrom-Json
    foreach($package in $manifest){if((Get-FileHash -LiteralPath (Join-Path $PackageFeed $package.file) -Algorithm SHA256).Hash-ne$package.sha256){throw "Offline package checksum mismatch: $($package.file)"}}
    # Regenerate from the supplied folder so the cache can move to another PC.
    $offlineConfig=Join-Path $PSScriptRoot 'obj\NuGet.Offline.generated.config'
    New-Item -ItemType Directory -Path (Split-Path $offlineConfig) -Force|Out-Null
    $escaped=[Security.SecurityElement]::Escape($PackageFeed)
    [IO.File]::WriteAllText($offlineConfig,"<?xml version=`"1.0`" encoding=`"utf-8`"?><configuration><packageSources><clear/><add key=`"offline`" value=`"$escaped`"/></packageSources><disabledPackageSources><clear/></disabledPackageSources></configuration>",[Text.UTF8Encoding]::new($false))
}
if(-not$NoRestore){
    $restore=@('restore',$project,'-r','win-x64','-p:Platform=x64','-p:NuGetAudit=false','--locked-mode')
    if($Offline){$restore+=@('--configfile',$offlineConfig)}
    if($PackagesDirectory){$restore+=@('--packages',[IO.Path]::GetFullPath($PackagesDirectory))}
    & dotnet @restore;if($LASTEXITCODE-ne0){throw 'Frontend restore failed'}
}
$command=if($Mode-eq'Publish'){'publish'}else{'build'}
& dotnet $command $project -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:NuGetAudit=false -p:DebugType=None -p:DebugSymbols=false --no-restore -o $OutputDirectory -v:minimal
if($LASTEXITCODE-ne0){throw 'Frontend compilation failed'}
foreach($file in @('AntigravityFrontend.exe','AntigravityFrontend.pri','Microsoft.Graphics.Canvas.dll','Microsoft.Graphics.Canvas.Interop.dll','Microsoft.ui.xaml.dll')){
    if(-not(Test-Path -LiteralPath (Join-Path $OutputDirectory $file))){throw "Deployment dependency missing: $file"}
}
if($configHash-and(Get-FileHash -LiteralPath $configPath).Hash-ne$configHash){throw 'Build changed the existing plugin configuration'}
if($GraphicsSelfTest){
    $output=Join-Path $OutputDirectory 'graphics-self-test'
    $p=Start-Process -FilePath $exe -ArgumentList @('--graphics-self-test',('"'+$output+'"')) -WorkingDirectory $OutputDirectory -WindowStyle Hidden -PassThru
    if(-not$p.WaitForExit(30000)){Stop-Process -Id $p.Id;throw 'Graphics self-test timed out'}
    if($p.ExitCode-ne0){throw "Graphics self-test failed. Read $output\graphics-result.json and error.txt"}
}
[pscustomobject]@{mode=$Mode;output=$OutputDirectory;offline=[bool]$Offline;noImplicitRestore=$true;existingConfigPreserved=$true}|ConvertTo-Json
