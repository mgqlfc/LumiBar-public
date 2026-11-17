param([string]$PackageFeed=(Join-Path $PSScriptRoot '..\..\offline-nuget\antigravity'),[switch]$SkipRestore)
$ErrorActionPreference='Stop'
$project=Join-Path $PSScriptRoot 'AntigravityFrontend.csproj'
$PackageFeed=[IO.Path]::GetFullPath($PackageFeed)
if(-not$SkipRestore){
    & dotnet restore $project -r win-x64 -p:Platform=x64 -p:NuGetAudit=false --locked-mode --source https://api.nuget.org/v3/index.json
    if($LASTEXITCODE-ne0){throw 'Online dependency preparation failed'}
}
$assets=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'obj\project.assets.json') -Raw|ConvertFrom-Json
$packages=@{}
foreach($entry in $assets.libraries.PSObject.Properties){
    if($entry.Value.type-eq'package'){$pair=$entry.Name.Split('/');$packages[$entry.Name]=@($pair[0].ToLowerInvariant(),$pair[1].ToLowerInvariant())}
}
foreach($framework in $assets.project.frameworks.PSObject.Properties){
    foreach($dep in $framework.Value.downloadDependencies){
        $version=$dep.version.Trim('[',']',' ');$pair=$version.Split(',')
        if($pair.Length-ne2-or$pair[0].Trim()-ne$pair[1].Trim()){throw "Unpinned SDK download: $($dep.name)"}
        $id=$dep.name.ToLowerInvariant();$ver=$pair[0].Trim().ToLowerInvariant();$packages["$id/$ver"]=@($id,$ver)
    }
}
New-Item -ItemType Directory -Path $PackageFeed -Force|Out-Null
$manifest=@()
foreach($key in @($packages.Keys|Sort-Object)){
    $id,$version=$packages[$key];$name="$id.$version.nupkg";$source=$null
    foreach($folder in $assets.packageFolders.PSObject.Properties.Name){
        $candidate=Join-Path $folder "$id\$version\$name"
        if(Test-Path -LiteralPath $candidate){$source=$candidate;break}
    }
    if(-not$source){throw "Cached package is missing: $key. Run prepare-offline.ps1 online once."}
    $destination=Join-Path $PackageFeed $name
    Copy-Item -LiteralPath $source -Destination $destination -Force
    $manifest+=[pscustomobject]@{id=$id;version=$version;file=$name;sha256=(Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash;bytes=(Get-Item -LiteralPath $destination).Length}
}
$escaped=[Security.SecurityElement]::Escape($PackageFeed)
$config="<?xml version=`"1.0`" encoding=`"utf-8`"?><configuration><packageSources><clear/><add key=`"offline`" value=`"$escaped`" /></packageSources><disabledPackageSources><clear/></disabledPackageSources></configuration>"
[IO.File]::WriteAllText((Join-Path $PackageFeed 'NuGet.Offline.Config'),$config,[Text.UTF8Encoding]::new($false))
$manifest|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $PackageFeed 'packages-manifest.json') -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'packages.lock.json') -Destination (Join-Path $PackageFeed 'packages.lock.json') -Force
[pscustomobject]@{feed=$PackageFeed;packages=$manifest.Count;bytes=($manifest|Measure-Object bytes -Sum).Sum}|ConvertTo-Json
