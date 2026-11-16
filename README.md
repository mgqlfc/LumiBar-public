# LumiBar

Ma Guanqun with Codex

Windows 11 任务栏天气与音乐双胶囊。当前版本 0.7.48，使用 C# / .NET 8 / WinUI 3；原生 C++ 组件处理任务栏空间预留。自带天气详情、系统媒体控制、频谱、同步歌词和可替换前端接口。

## 目录

- `work/LumiBar`：当前 Windows 主程序，含内置前端与后端。
- `work/BackendTests`：不依赖 WinUI 的后端回归检查。
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

当前桥接脚本预设 Visual Studio Community 2022 / MSVC 14.44.35207 / Windows SDK 10.0.26100.0；其他工具链版本请先调整脚本开头的路径与版本变量。

后端回归检查：

```sh
dotnet run --project work/BackendTests/BackendTests.csproj -c Release
```

0.7.48 已在开发机通过 338 项检查。是否支持其他系统以实际构建和测试为准；macOS 原生客户端尚未开始，Windows 10 任务栏适配待完成。

## 安装包

`work/installer/build-release.ps1` 发布自包含程序并调用 Inno Setup 7。当前脚本从 `work/build-tools/InnoSetup7/ISCC.exe` 读取编译器；该开发工具不放入源码仓库，请在本机准备官方 Inno Setup，或修改该调用路径。

```powershell
& .\work\installer\build-release.ps1 -Version '0.7.48'
```

已存在同名发布目录时脚本会停止，使用新版本或新的发布目录构建。安装包和中间产物保留在本机，不纳入 Git。

## 天气与插件

天气服务读取服务器环境配置；API 密钥、管理员凭据和用户配置由各设备本地保存。新设备通过设置页配置天气来源，服务端部署参考服务接入包。

进程外前端从 `outputs/Antigravity前端接入包/README.md` 开始接入。各设备的进程、HWND、资料目录和端点不同，运行时读取当前后端的端点文件。

## 两台电脑协作

在 MacBook 上克隆同一私有仓库，可查看源码、设计与协议，并开展 Mac 客户端开发。当前 WinUI 程序仍在 Windows 上构建和运行。

```sh
git clone https://github.com/mgqlfc/LumiBar.git
```

仓库保留项目既有目录，便于现有脚本和相对引用继续工作。运行数据、城市收藏、歌词缓存、崩溃转储、密钥及发布安装包均不参与同步。