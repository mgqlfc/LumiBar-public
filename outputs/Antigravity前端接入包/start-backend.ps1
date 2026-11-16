$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $workspace 'work\LumiBar\LumiBar.csproj'
$build = Join-Path $workspace 'work\frontend-backend-build'
$data = Join-Path $workspace 'work\frontend-dev-data'
if (!(Test-Path -LiteralPath $project)) { throw "找不到后端工程：$project" }
$endpoint = Join-Path $data 'frontend-endpoint.json'
if (Test-Path -LiteralPath $endpoint) {
  try {
    $descriptor = Get-Content -Raw -LiteralPath $endpoint | ConvertFrom-Json
    $existing = Get-Process -Id $descriptor.pid -ErrorAction SilentlyContinue
    if ($existing -and $existing.Path -eq (Join-Path $build 'LumiBar.exe')) {
      Write-Output "开发后端已在运行，接口文件：$endpoint"
      Write-Output '需要重新编译时，请先退出这个开发后端。'
      return
    }
  } catch { }
}
dotnet build $project -c Release -p:Platform=x64 --nologo -o $build
if ($LASTEXITCODE -ne 0) { throw '后端编译失败' }
New-Item -ItemType Directory -Path $data -Force | Out-Null
$initialSettings = Join-Path $data 'settings.json'
if (!(Test-Path -LiteralPath $initialSettings)) {
  '{"City":{"Name":"奎文区","Latitude":36.7066306,"Longitude":119.1268375,"Region":"山东省 · 潍坊市","TimeZone":"Asia/Shanghai"}}' | Set-Content -LiteralPath $initialSettings -Encoding utf8
}
$previousDataPath = $env:LUMIBAR_DATA_DIR
try {
  $env:LUMIBAR_DATA_DIR = $data
  Start-Process -FilePath (Join-Path $build 'LumiBar.exe') -ArgumentList '--frontend-backend' -WorkingDirectory $build -WindowStyle Hidden
} finally { $env:LUMIBAR_DATA_DIR = $previousDataPath }
Write-Output "后端已启动。接口描述文件：$data\frontend-endpoint.json"
Write-Output "连接测试：node `"$PSScriptRoot\smoke.mjs`" `"$data\frontend-endpoint.json`""
