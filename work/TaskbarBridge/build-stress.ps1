$ErrorActionPreference='Stop'
$vs='C:/Program Files/Microsoft Visual Studio/2022/Community/VC/Tools/MSVC/14.44.35207'
$sdk='C:/Program Files (x86)/Windows Kits/10'
$v='10.0.26100.0'
$env:INCLUDE="$vs/include;$sdk/Include/$v/ucrt;$sdk/Include/$v/shared;$sdk/Include/$v/um;$sdk/Include/$v/winrt;$sdk/Include/$v/cppwinrt"
$env:LIB="$vs/lib/x64;$sdk/Lib/$v/ucrt/x64;$sdk/Lib/$v/um/x64"
Push-Location (Join-Path $PSScriptRoot 'build')
try {& "$vs/bin/Hostx64/x64/cl.exe" /nologo /utf-8 /std:c++20 /EHsc /O2 /MT /DUNICODE /D_UNICODE ../Stress.cpp /Fe:LumiBar.LayoutStress.exe /link /SUBSYSTEM:WINDOWS windowsapp.lib user32.lib gdi32.lib ole32.lib shell32.lib propsys.lib; if($LASTEXITCODE -ne 0){throw 'Stress harness build failed'}}finally{Pop-Location}
