# Antigravity Fluent 玻璃前端插件 (LumiBar Preview)

面向 `LumiBar Frontend API v1` 的独立 **C# + WinUI 3** 备选前端实现，参考 WidBar 与 FluentFlyout 交互理念。

2026-11-15 Codex 排查交接：先看 [CODEX_CAPSULE_HANDOFF-0.7.44.md](./CODEX_CAPSULE_HANDOFF-0.7.44.md)，已用真实槽位响应和当前 RectDto 复现 `slot.rect.visible` 缺失→false→主动隐藏。原生实现经验见 [CAPSULE_IMPLEMENTATION_NOTES-0.7.44.md](./CAPSULE_IMPLEMENTATION_NOTES-0.7.44.md)。本次 Codex 仅更新交接材料，前端源码由 Antigravity 继续维护。

0.7.45 设置切换修复与前端回写要求：[SETTINGS_LIFECYCLE_FIX-0.7.45.md](./SETTINGS_LIFECYCLE_FIX-0.7.45.md)。用户已手动恢复偏好，请保留当前值；初始化和释放渲染器时不回写缓存默认值。

0.7.46 新增 [自定义菜单注册](./PLUGIN_MENUS-0.7.46.md)：只放托盘与设置左侧，不添加胶囊右键。已编译测试的示例源代码放在 `PluginMenusExample.cs.txt`，采用 .txt 后缀避免自动混入你正在维护的前端编译；正式 .cs 与 TypeScript 契约在 outputs/Antigravity前端接入包。

2026-11-15 后端接口增量已同步到 [BACKEND_APPEARANCE-2026-11-15.md](./BACKEND_APPEARANCE-2026-11-15.md)、[BACKEND_FONTS-0.5.md](./BACKEND_FONTS-0.5.md) 和 `contracts.d.ts`，涵盖双字体、封面饱和度、第二配色及 GPS 状态。本次只更新交接材料，前端源码与 C# DTO 尚未接入新增字段。

## 目录结构

- `AntigravityFrontend.csproj` — `net8.0-windows10.0.19041.0` WinUI 3 自包含工程
- `plugin.manifest.json` — 插件交付描述清单
- `Sdk/` — `LumiBarClient.cs`（Named Pipe JSON Lines）、`Contracts.cs`、`PluginConfig.cs`
- `Services/` — `LumiBarBridgeService.cs`（双管道状态/频谱独立轮询、封面缓存、断线重连、示例数据模式）、`Win32WindowHelper.cs`（`DesktopAcrylicController` 与显示器工作区定位）、`AudioVolumeHelper.cs`
- `Controls/` — `MusicCapsuleControl`（左，238/48 DIP）、`WeatherCapsuleControl`（右，104/64 DIP）、`SpectrumBackdropControl`、`PrecipitationTrendControl`
- `Windows/` — `MainWindow`（预览工作台与双 Lite 滞回演示）、`LyricsFlyoutWindow`（歌词为主 + 丰富音频控制）、`WeatherFlyoutWindow`（24h 曲线 + 多日预报 + 风速 + US AQI + 1h 降水）、`SettingsWindow`
- `FRONTEND_REQUESTS.md` — 建议 Codex 后端补充的增量接口清单

## 构建与启动

### 1. 启动 LumiBar 独立预览后端
```powershell
& "C:\Users\ROG\Documents\Codex\2026-11-14\ban\outputs\Antigravity前端接入包\start-backend.ps1"
```

### 2. 编译前端插件
```powershell
dotnet build "C:\Users\ROG\Documents\Codex\2026-11-14\ban\work\frontends\antigravity\AntigravityFrontend.csproj" -c Release -p:Platform=x64 -o "C:\Users\ROG\Documents\Codex\2026-11-14\ban\work\frontends\antigravity\dist"
```

### 3. 运行前端
```powershell
& "C:\Users\ROG\Documents\Codex\2026-11-14\ban\work\frontends\antigravity\dist\AntigravityFrontend.exe"
```

> 若未启动后端，可在窗口右上角打开 **示例数据** 开关体验完整交互；连接真实后端时，前端自动读取 `C:\Users\ROG\Documents\Codex\2026-11-14\ban\work\frontend-dev-data\frontend-endpoint.json`。

后端 0.7.47 城市栏与胶囊调整见 [UI_POLISH-0.7.47.md](./UI_POLISH-0.7.47.md)。用户最终选择恢复细线播放图标。插件菜单仍见 PLUGIN_MENUS-0.7.46.md。
