param(
    [ValidateSet('weather','weather-alerts','weather-hover','weather-capsule','weather-lite','weather-wide','music','settings')][string]$Case='weather',
    [ValidateSet('Light','Dark')][string]$Theme='Light',
    [switch]$Build,
    [switch]$Stop
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$scratch=Join-Path $repo 'work\mac-reference-preview'
$source=Join-Path $scratch 'source';$app=Join-Path $scratch 'app';$data=Join-Path $scratch "data-$Case-$Theme"
$exe=Join-Path $app 'LumiBar.exe'
if($Build){
    New-Item -ItemType Directory -Path $source -Force|Out-Null
    Get-ChildItem -LiteralPath (Join-Path $repo 'work\LumiBar') -Force|Where-Object Name -notin @('bin','obj')|ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination $source -Recurse -Force}
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'VisualPolishInspection.cs.template') -Destination (Join-Path $source 'VisualPolishInspection.cs') -Force
    foreach($name in @('MusicWindow','HoverPreviewWindow')){
        $file=Join-Path $source "$name.cs";$text=[IO.File]::ReadAllText($file)
        $before="internal sealed class $name";$after="internal sealed partial class $name"
        if(-not$text.Contains($before)){throw "Reference fixture class signature changed: $name"}
        $text=$text.Replace($before,$after)
        if($name-eq'HoverPreviewWindow'){$text=$text.Replace('Environment.GetCommandLineArgs().Contains("--inspect-hover-layout")','true')}
        [IO.File]::WriteAllText($file,$text,[Text.UTF8Encoding]::new($false))
    }
    $project=Join-Path $source 'LumiBar.csproj'
    $projectText=[IO.File]::ReadAllText($project)
    $bridgeRoot=(Join-Path $repo 'work\TaskbarBridge\build\').Replace('\','/')
    [IO.File]::WriteAllText($project,$projectText.Replace('../TaskbarBridge/build/',$bridgeRoot),[Text.UTF8Encoding]::new($false))
    & dotnet restore $project -p:Platform=x64 -r win-x64 -p:NuGetAudit=false --ignore-failed-sources
    if($LASTEXITCODE){throw 'Reference dependency restore failed'}
    & dotnet publish $project -c Release -p:Platform=x64 -r win-x64 --self-contained true --no-restore -p:DebugType=None -p:DebugSymbols=false -o $app -v:minimal
    if($LASTEXITCODE){throw 'Reference preview build failed'}
    Add-Type -AssemblyName System.Drawing
    $bitmap=[Drawing.Bitmap]::new(256,256);$g=[Drawing.Graphics]::FromImage($bitmap)
    try{
        $g.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $gradient=[Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Rectangle]::new(0,0,256,256),[Drawing.ColorTranslator]::FromHtml('#173D69'),[Drawing.ColorTranslator]::FromHtml('#B3C7E6'),45)
        try{$g.FillRectangle($gradient,0,0,256,256)}finally{$gradient.Dispose()}
        $pen=[Drawing.Pen]::new([Drawing.Color]::FromArgb(100,255,255,255),2)
        try{foreach($diameter in @(190,154,118,82)){$g.DrawEllipse($pen,(256-$diameter)/2,(256-$diameter)/2,$diameter,$diameter)}}finally{$pen.Dispose()}
        $bitmap.Save((Join-Path $app 'reference-cover.png'),[Drawing.Imaging.ImageFormat]::Png)
    }finally{$g.Dispose();$bitmap.Dispose()}
}
if(-not(Test-Path -LiteralPath $exe)){throw 'Build the isolated preview with -Build first.'}
$oldData=$env:LUMIBAR_DATA_DIR;$oldCase=$env:LUMIBAR_REFERENCE_CASE;$oldTheme=$env:LUMIBAR_REFERENCE_THEME
try{
    $env:LUMIBAR_DATA_DIR=$data;$env:LUMIBAR_REFERENCE_CASE=$Case;$env:LUMIBAR_REFERENCE_THEME=$Theme
    if($Stop){$helper=Start-Process -FilePath $exe -ArgumentList '--frontend-backend','--shutdown' -WindowStyle Hidden -PassThru;$helper.WaitForExit();if($helper.ExitCode){throw 'Reference shutdown failed'};return}
    New-Item -ItemType Directory -Path $data -Force|Out-Null
    $metadata=Join-Path $data 'reference-window.json'
    if(Test-Path -LiteralPath $metadata){Remove-Item -LiteralPath $metadata}
    # An empty isolated profile prevents startup refresh/location before fixture initialization.
    [IO.File]::WriteAllText((Join-Path $data 'settings.json'),'{"City":null,"AutoLocateCity":false,"AutoStart":false}',[Text.UTF8Encoding]::new($false))
    $p=Start-Process -FilePath $exe -ArgumentList '--frontend-backend','--inspect-ui','--inspect-media-polish' -WorkingDirectory $app -WindowStyle Hidden -PassThru
    $until=[Environment]::TickCount64+15000
    while(-not(Test-Path -LiteralPath $metadata)-and-not$p.HasExited-and[Environment]::TickCount64-lt$until){Start-Sleep -Milliseconds 100}
    if($p.HasExited){throw 'Reference preview exited before rendering'}
    if(-not(Test-Path -LiteralPath $metadata)){throw 'Reference preview did not report its layout'}
    [pscustomobject]@{pid=$p.Id;case=$Case;theme=$Theme;metadata=$metadata;title="LumiBar Reference · $Case · $Theme"}|ConvertTo-Json
}finally{$env:LUMIBAR_DATA_DIR=$oldData;$env:LUMIBAR_REFERENCE_CASE=$oldCase;$env:LUMIBAR_REFERENCE_THEME=$oldTheme}
