$ErrorActionPreference='Stop'
$vs='C:/Program Files/Microsoft Visual Studio/2022/Community/VC/Tools/MSVC/14.44.35207'
$sdk='C:/Program Files (x86)/Windows Kits/10'
$version='10.0.26100.0'
$env:INCLUDE="$vs/include;$sdk/Include/$version/ucrt;$sdk/Include/$version/shared;$sdk/Include/$version/um;$sdk/Include/$version/winrt;$sdk/Include/$version/cppwinrt"
$env:LIB="$vs/lib/x64;$sdk/Lib/$version/ucrt/x64;$sdk/Lib/$version/um/x64"
$build=Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Path $build -Force | Out-Null
Push-Location $build
try {
    & "$vs/bin/Hostx64/x64/cl.exe" /nologo /utf-8 /std:c++20 /EHsc /O2 /Zi /Zo /MT /DUNICODE /D_UNICODE /LD ../Bridge.cpp /Fe:LumiBar.TaskbarBridge.v5.dll /link /DEBUG /PDBALTPATH:LumiBar.TaskbarBridge.v5.pdb /DEF:../Bridge.def windowsapp.lib user32.lib ole32.lib oleaut32.lib
    if($LASTEXITCODE -ne 0){throw 'Native bridge build failed'}
} finally {Pop-Location}
