# Synthwave 渲染与后端支持 · 0.7.49

2026-11-16，Codex → Antigravity。工程在 `work/frontends/antigravity/AntigravityFrontend.csproj`；协议保持 v1。

## 接入结果

- 加入 Microsoft.Graphics.Win2D **1.3.2**，已安装到当前用户 NuGet 缓存，生成 `packages.lock.json`。
- 保留 Windows App SDK **1.8.251106002**、net8.0-windows10.0.19041.0、win-x64、自包含。Win2D 1.4.0 要求 WinUI 1.8.260204000；这轮选用能匹配现有 SDK 的 1.3.2。
- `AntigravityFrontend.pri` 合并资源索引已加入 publish 输出。构建脚本检查托管 Interop、原生 Canvas DLL、WinUI DLL 和 PRI。
- 提供 `Rendering/SynthwaveEffects.cs`。Antigravity 可直接把这些真实 Win2D 效果接进现有 CanvasControl，继续完成布局中的视觉绑定。

## 依赖与离线编译

在线准备一次：

```powershell
.\prepare-offline.ps1
```

它会锁定还原并导出 19 个 NuGet 包，包括 Win2D、Windows App SDK、Windows SDK 元数据和 .NET win-x64 运行包。当前离线源：

`C:\Users\ROG\Documents\Codex\2026-11-14\ban\work\offline-nuget\antigravity`

依赖包约 434 MB，附 `packages-manifest.json` 的逐包 SHA-256 和锁文件。离线包不放入 Git，也不放入安装包；安装包只携带运行所需的 DLL 和资源。

```powershell
.\build.ps1 -Mode Build -Offline
.\build.ps1 -Mode Publish -Offline -NoRestore -OutputDirectory .\dist
.\build.ps1 -Mode Publish -Offline -GraphicsSelfTest -OutputDirectory .\dist
```

`-NoRestore` 使用已经还原的 assets；首次离线构建先运行不带 `-NoRestore` 的命令。脚本内部的 build/publish 全部显式使用 `--no-restore`。离线还原只使用本地包源，关闭 NuGet Audit；包校验失败就停止。

在另一台 Windows 机器上准备 .NET 8 SDK 和 Windows 构建环境，将整份离线源复制过去，再传 `-PackageFeed <新位置>`。脚本会按新位置生成专用 NuGet 配置。当前完整验证使用 SDK 8.0.427 / 运行包 8.0.33；不同 SDK 补丁可能需要额外运行包。

项目原有命令也已可用：

```powershell
dotnet build .\AntigravityFrontend.csproj -c Release -p:Platform=x64 --no-restore
```

## 效果链和像素约定

使用 `Microsoft.Graphics.Canvas.UI.Xaml.CanvasControl`，ClearColor 设为 Transparent。先绘制离屏文字/砖块/环形，再组合效果。

`SynthwaveEffects` 已提供：

- `Glow(source, 5)`：高斯模糊，默认 5 DIP；宽光晕可传 11。
- `Screen(background, foreground)`：BlendEffectMode.Screen。
- `Mask(destination, alphaMask)`：DestinationIn，输入顺序为底图在前、遮罩在后。
- `Reflection(bricks, bottomRows, mirrorAxisY, fresnelMask)`：裁取底部 1–3 排砖块的区域，垂直翻转，模糊 3.2，再应用 Alpha 衰减。
- `LyricGradient(device, height)`：白 → #67E8F9 → #FDE68A，色标 0 / .55 / 1，可继续按设计调整。
- `ScanlineMask(device, widthDip, heightDip, dpi)`：每 3 个物理像素生成 1 像素遮罩，使用 Aliased 精确绘制。
- `PunchOut(textLayer, scanlineMask)`：DestinationOut，扫描线处实际变透明。
- `TearSlice(text, width, height, y, shift:7, sliceHeight:7, dpi:canvas.Dpi)`：7 个物理像素的错位切片；几何位置/尺寸使用 DIP。

青色/品红色差层可用 TintEffect + Transform2DEffect 与渐变正文叠合，再对文字离屏层做 PunchOut；最后将文字层叠到落日/网格背景。G3 内发光环再与胶囊圆角 Alpha 遮罩做 Mask，保持边界整洁。

在 CreateResources、DPI/尺寸变化时创建离屏目标和效果图；Draw 中复用，更新颜色/变换/参数，避免每帧分配整套 RenderTarget。设备丢失后重建资源；CanvasControl 卸载时 RemoveFromVisualTree，释放目标、画刷和效果。

`--graphics-self-test <输出目录>` 在独立进程中实际绘制并检查像素，不连接后端、不启动任务栏接管。8/8：CanvasControl、GaussianBlur、Screen、DestinationIn、DestinationOut、倒影、VHS 切片、渐变文字。发布目录的实际 exe 也已通过。

## 专辑取色

线上的 JSON 字段是 `state.media.accent` / `secondaryAccent`（#RRGGBB）；C# DTO 读取为 Media.Accent。

主色来自当前封面的原始彩色采样，过滤黑灰背景，保留色相与饱和度；用户的 CoverSaturation / 玻璃降饱和只作用于绘制，不改变 IPC 中的取色。纯黑灰封面使用有颜色的蓝紫备用色。

切歌的缩略图尚未到达时，现在会先重置为备用色，防止新歌短暂沿用上一首的颜色。HasArtwork 由当前曲目的封面加载状态决定；缓存中的当前封面则直接复用。

## 频谱

沿用三连接模型：状态/操作、events.wait、spectrum.get。频谱通过专用连接主动读取，hello 中 eventPush 仍为 false。

- 保留 20 段 bands，数值 0..1；暂停或关闭频谱时返回零值。
- 后端保持 2048 点 FFT，加入重叠窗口和更密集采样步长，给音频回调的分包留出余量。
- spectrum.get 直接读取线程安全快照，绕开 UI Dispatcher；媒体/频谱开关状态在通知时更新。
- 前端改为 25ms 固定节拍（目标 40Hz），请求耗时计入周期；UI 通知会合并，忙碌时显示最新一帧。
- 实测真实 BridgeService + 命名管道：连续 6.48 秒，37.94 回调/秒、34.08 新帧/秒，P95 间隔 31.74ms、最大 33.33ms。测试期间后端 UI 多次忙碌 450ms。
- 该性能检查使用无声合成采样，不改音量、不向播放器发送控制。源码提供 `--spectrum-self-test <隔离端点> <输出目录>`；后端测试模式要求 `--frontend-backend --inspect-spectrum-ipc` 和明确的独立 LUMIBAR_DATA_DIR。

## v6 配置和安装

PluginConfig.Load 新建配置为 ConfigVersion=6、LyricsFlyoutWidth=420、LyricsFlyoutHeight=384；旧版本的 548 高度会迁移。已有 v6 用户自定义尺寸保留。

`antigravity-plugin.defaults.json` 是出厂模板，随发布携带；不覆盖用户的 `antigravity-plugin.config.json`。构建脚本检查已有配置的文件哈希。迁移和保留行为 7/7 通过。

主安装脚本支持：

```powershell
.\work\installer\build-release.ps1 -Version 0.7.49 -IncludeAntigravity -OfflineFrontend
```

它会发布到 `payload/frontends/antigravity/dist`，复制相对入口清单，携带完整 Win2D 运行组件；不捆绑本机用户配置。普通安装包仍可单独构建。带前端的包使用 `-with-antigravity-setup.exe` 后缀，发布目录也独立。

## 胶囊位置

用户追加要求：相对 0.7.48 整体上移 1 DIP。后端共同偏移现在是相对居中下移 1 DIP，Full/Lite 与外部插件真实槽位同步。rect 已含偏移，前端不要再次叠加。封面、歌名和歌手名的相对位置不动。