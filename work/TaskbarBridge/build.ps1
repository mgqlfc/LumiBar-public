param([string]$MsvcRoot,[string]$WindowsSdkRoot,[string]$WindowsSdkVersion)
$ErrorActionPreference='Stop'
if(-not$MsvcRoot){
    $vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if(-not(Test-Path -LiteralPath $vswhere)){throw 'Install Visual Studio C++ desktop tools first.'}
    $installation=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if(-not$installation){throw 'Visual Studio C++ desktop tools were not found.'}
    $toolVersion=(Get-Content -LiteralPath (Join-Path $installation 'VC\Auxiliary\Build\Microsoft.VCToolsVersion.default.txt') -Raw).Trim()
    $MsvcRoot=Join-Path $installation "VC\Tools\MSVC\$toolVersion"
}
if(-not$WindowsSdkRoot){$WindowsSdkRoot=Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'}
if(-not$WindowsSdkVersion){
    $WindowsSdkVersion=Get-ChildItem -LiteralPath (Join-Path $WindowsSdkRoot 'Include') -Directory |
        Where-Object {$_.Name-match'^10\.0\.\d+\.\d+$'-and(Test-Path -LiteralPath (Join-Path $WindowsSdkRoot "Lib\$($_.Name)\um\x64\windowsapp.lib"))} |
        Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1 -ExpandProperty Name
}
if(-not$WindowsSdkVersion){throw 'Install the Windows 10/11 SDK first.'}
$vs=$MsvcRoot;$sdk=$WindowsSdkRoot;$version=$WindowsSdkVersion
if(-not(Test-Path -LiteralPath (Join-Path $vs 'bin\Hostx64\x64\cl.exe'))){throw 'The selected x64 MSVC compiler is missing.'}
$env:INCLUDE="$vs/include;$sdk/Include/$version/ucrt;$sdk/Include/$version/shared;$sdk/Include/$version/um;$sdk/Include/$version/winrt;$sdk/Include/$version/cppwinrt"
$env:LIB="$vs/lib/x64;$sdk/Lib/$version/ucrt/x64;$sdk/Lib/$version/um/x64"
$build=Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Path $build -Force | Out-Null
Push-Location $build
try {
    & "$vs/bin/Hostx64/x64/cl.exe" /nologo /utf-8 /std:c++20 /EHsc /O2 /Zi /Zo /MT /DUNICODE /D_UNICODE /LD ../Bridge.cpp /Fe:LumiBar.TaskbarBridge.v6.dll /link /DEBUG /PDBALTPATH:LumiBar.TaskbarBridge.v6.pdb /DEF:../Bridge.def windowsapp.lib user32.lib ole32.lib oleaut32.lib
    if($LASTEXITCODE -ne 0){throw 'Native bridge build failed'}
} finally {Pop-Location}
