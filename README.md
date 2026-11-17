# LumiBar

Ma Guanqun with Codex

Windows 11 任务栏天气与音乐双胶囊。当前版本 0.7.54，使用 C# / .NET 8 / WinUI 3；原生 C++ 组件处理任务栏空间预留。自带天气详情、系统媒体控制、频谱、同步歌词和可替换前端接口。Mac 版目标 macOS 27：音乐围绕刘海展开，天气放在右侧菜单栏，详见 [Mac 移植交接](MACOS_HANDOFF.md)。

## 目录

- `work/LumiBar`：当前 Windows 主程序，含内置前端与后端。
- `work/BackendTests`：不依赖 WinUI 的后端回归检查。
- `work/FrontendSupportTests`：插件配置默认值与迁移检查。
- `work/TaskbarBridge`：任务栏原生桥接组件。
- `work/frontends/antigravity`：备选进程外前端源码。
- `work/ConceptUI`、`outputs/concepts`：交互原型与设计参考。
- `outputs/Antigravity前端接入包`：协议 v1、客户端 SDK、插件接口和交接文档。
- `outputs/和风天气服务器接入包`：天气代理服务源码与部署模板。
- `work/installer`：Windows x64 安装脚本及历次验证记录。

## 开发环境

Windows 程序当前支持 Windows 11 x64。安装 .NET 8 SDK、Visual Studio 2022 的「使用 C++ 的桌面开发」和 Windows SDK。WinUI NuGet 包首次构建时从官方源还原。

先构建原生桥接，再构建主程序：

```powershell
& .\work\TaskbarBridge\build.ps1
dotnet restore .\work\LumiBar\LumiBar.csproj -p:Platform=x64
dotnet build .\work\LumiBar\LumiBar.csproj -c Release -p:Platform=x64
```

桥接脚本通过 vswhere 查找 C++ 工具链，并选取本机已安装的 Windows SDK；也可传入 `-MsvcRoot`、`-WindowsSdkRoot`、`-WindowsSdkVersion`。

后端回归检查：

```sh
dotnet run --project work/BackendTests/BackendTests.csproj -c Release
```

0.7.54 后端已在开发机通过 348 项检查，修复开始菜单期间的误降级与位置跳变，预警徽章只显示事件名称。macOS 原生客户端处于概念与移植准备阶段，Windows 10 任务栏适配待完成。

Antigravity 前端新增 Win2D 1.3.2、锁文件与离线构建脚本。最新配置版本为 v7，歌词浮窗默认 512×384；历史 0.7.49 文档记录的是 v6 的 420×384，当前默认值以 `Sdk/PluginConfig.cs` 和 `antigravity-plugin.defaults.json` 为准。

## 安装包

GitHub 页面打开 **Actions → Build Release → Run workflow**，分支选 `main`，版本留空会读取工程版本；可勾选 `include_antigravity`。运行成功后，在该次运行底部的 **Artifacts** 下载 `LumiBar-版本-windows-x64`，解压即可得到 EXE 安装包和 SHA-256 校验文件。该流程生成 Windows 安装包，使用 GitHub 的 Windows 构建机，Mac 上也能从网页触发。产物保留 14 天。

本机入口 `work/installer/build-release.ps1` 发布自包含程序并调用 Inno Setup。编译器从 `-IsccPath`、`LUMIBAR_ISCC`、本机工具目录或已安装的 Inno Setup 查找。

```powershell
& .\work\installer\build-release.ps1 -Version '0.7.54'
```

已存在同名发布目录时脚本会停止，使用新版本或新的发布目录构建。安装包和中间产物保留在本机，不纳入 Git。

脚本会还原 NuGet 依赖，并在缺少 TaskbarBridge 时构建桥接组件；离线且已准备依赖时可传 `-NoRestore`。新电脑先完成上面的环境准备并安装 Inno Setup。可选 `-IncludeAntigravity` 将前端插件一并打包；`-OfflineFrontend` 使用本机导出的 NuGet 源。离线包目录在各开发机通过前端的 `prepare-offline.ps1` 生成。

## 天气与插件

天气服务读取服务器环境配置；API 密钥、管理员凭据和用户配置由各设备本地保存。新设备通过设置页配置天气来源，服务端部署参考服务接入包。

进程外前端从 `outputs/Antigravity前端接入包/README.md` 开始接入。各设备的进程、HWND、资料目录和端点不同，运行时读取当前后端的端点文件。

## 两台电脑协作

在 MacBook 上克隆同一私有仓库，可查看源码、设计与协议，并开展 Mac 客户端开发。当前 WinUI 程序仍在 Windows 上构建和运行。

```sh
git clone https://github.com/mgqlfc/LumiBar.git
```

克隆后先打开 [MACOS_HANDOFF.md](MACOS_HANDOFF.md)，概念图在 `outputs/concepts/LumiBar-macOS27-concept-v1.png`。

仓库保留项目既有目录，便于现有脚本和相对引用继续工作。运行数据、城市收藏、歌词缓存、崩溃转储、密钥及发布安装包均不参与同步。
