# Antigravity WinUI 3 前端对接文档（面向 Codex / LumiBar 后端）

> 版本基线：对齐 **LumiBar Frontend API v1 (`0.4-dev` ~ `0.6-dev`)**  
> 生成日期：2026-11-14  
> 前端工程目录：`C:\Users\ROG\Documents\Codex\2026-11-14\ban\work\frontends\antigravity`

---

2026-11-15 后端接口增量见 [BACKEND_APPEARANCE-2026-11-15.md](./BACKEND_APPEARANCE-2026-11-15.md) 和 `contracts.d.ts`：双字体、封面饱和度、第二封面色及 GPS 状态。下面的前端实现描述保留原交付状态；本次没有修改前端源码，新增字段接入待前端完成。

## 1. 前端概况与交付清单

- **技术栈**：C# / .NET 8 / Windows App SDK 1.8 / WinUI 3（`net8.0-windows10.0.19041.0`，`x64`，自包含非打包运行）
- **清单文件**：`plugin.manifest.json`
  - `id`: `antigravity.lumibar-glass`
  - `kind`: `external-frontend`
  - `protocolVersion`: `1`
  - `entry`: `dist/AntigravityFrontend.exe`
  - `mode`: `preview`
  - `clientName`（`hello` 握手）：`"antigravity-winui"`
- **默认读取的后端端点描述文件**：
  `C:\Users\ROG\Documents\Codex\2026-11-14\ban\work\frontend-dev-data\frontend-endpoint.json`（可在设置页 `常规 -> 接口文件` 修改并持久化到 `antigravity-plugin.config.json`）。
- **示例数据回退**：前端内置可手动切换的 `示例数据` 开关（Mock Mode）；真实后端未连接或断线时保留最近快照与明确离线状态标识。

---

## 2. Named Pipe 并发连接模型

前端通过 `LumiBarClient`（Named Pipe + UTF-8 JSON Lines + `\n` 分隔）与后端维持以下连接方式，均使用 `protocolVersion: 1` 完成 `hello` 握手：

1. **主状态通道（`_stateClient`）**
   - 负责：`state.get`、`windows.hints`、`windows.reservation`、`media.artwork`（按 `media.key` 缓存，且仅在 `media.hasArtwork == true` 时请求），以及用户交互动作（经 `_actionLock` 串行执行；若主通道未就绪则回退创建一次性短连接）。
   - 轮询节奏：`events.wait` 心跳正常时每 `15s` 兜底校对一次；长轮询异常时退化为 `1s`（弹窗展开）/ `3s`（收起）兜底；断线后 `1s -> 2s -> 5s -> 10s` 退避重连。
2. **事件长轮询通道（`_eventsClient`）**
   - 调用 `events.wait`（参数：`afterVersion`, `timeoutMs: 25000`）。
   - 收到 `resyncRequired: true` 或 `instanceId` 变化时立刻触发全量 `state.get` + `windows.hints` + `windows.reservation`。
   - 根据 `topics` 增量刷新：`media` / `weather` / `lyrics` / `settings` / `appearance` 触发 `state.get`；`windows` 触发 `windows.hints` 与 `windows.reservation`。
3. **音频频谱通道（`_spectrumClient`）**
   - 独立调用 `spectrum.get`（20 频段）。
   - 仅当 `media.playing == true` 时约 `45ms` 拉取一次；暂停时降至 `140ms` 等待，不在后台空转刷频谱。
4. **字体查询短连接（`fonts.list`）**
   - 按需打开短连接查询系统字体（防抖 180ms + 手动刷新），不阻塞长轮询。

> 若后续后端对同一进程最大并发连接数有约束，目前 Antigravity 稳态同时持有 **3 条长连接**（State / Events / Spectrum），短连接最多额外 +1。

---

## 3. 已对接的后端接口与字段消费矩阵（API v1）

### 3.1 握手与只读查询

| RPC 方法 | 前端调用时机 | 前端消费字段 | 兼容/回退处理 |
| --- | --- | --- | --- |
| `hello` | 每条管道连接建立后立即调用 | `backendVersion`, `instanceId`, `mode`, `capabilities`（含 `builtinTaskbarReservation`, `taskbarReservation`, `rendererReplacement` 等）, `methods[]` | 旧版缺少新能力字段时按 `false` 处理 |
| `state.get` | 启动、事件通知、兜底校对 | `stateVersion`, `instanceId`, `appearance.*`, `weather.*`, `media.*`, `lyrics.*`, `settings.*` | 离线保留上次快照并标记离线横幅 |
| `spectrum.get` | 播放中 ~45ms | `playing`, `source`, `status`, `bands[20]` | Mock 模式下生成平滑模拟波形 |
| `media.artwork` | `media.key` 变更且 `hasArtwork == true` | `trackKey`, `mimeType`, `base64` | 内存字典按 `trackKey` 缓存 |
| `windows.hints` | `state.get` 后 & `windows` 事件 | `taskbar`, `layoutState`, `layoutRecommendation`, `musicSlot`, `weatherSlot`, `trayWidthPx`, `reservationSupported`, `nativeReservation`, `renderingOwner` | 字段缺失时安全跳过 |
| `windows.reservation` | 当 `hello.methods` 包含该方法（或为空）时随 `RefreshHintsAsync` 查询 | `snapshot`（`status`, `error`, `slot`, `widthDip`, `explorerPid`, `at`）, `slots`（`musicSlot`, `weatherSlot`, `state`）, `owner` | 老后端报未知方法时静默跳过 |
| `fonts.list` | 打开设置页 / 搜索 / 点击刷新 | `source`, `installedCount`, `total`, `offset`, `hasMore`, `items[]`（`family`, `displayName`, `aliases`） | Mock / 离线时回退至本地 GDI `EnumFontFamiliesExW` 实采 |

### 3.2 写入 / 控制命令

| RPC 方法 | 参数示例 | 说明与错误反馈 |
| --- | --- | --- |
| `media.toggle` / `media.previous` / `media.next` | `{}` | 切歌/播放控制，返回后强制刷新一次状态 |
| `media.seek` | `{ "seconds": 42.5 }` | 拖拽释放进度条、`+10s`/`-10s` 触发 |
| `media.setShuffle` | `{ "enabled": true }` | 仅在 `media.controls.shuffle == true` 时开放交互 |
| `media.setRepeat` | `{ "mode": "None" \| "Track" \| "List" }` | 仅在 `media.controls.repeat == true` 时开放交互 |
| `weather.refresh` | `{}` | 手动刷新天气并强制 `state.get` |
| `cities.search` | `{ "query": "青岛" }` | 城市搜索面板实时查询 |
| `cities.select` | `{ name, latitude, longitude, region, timeZone }` | 顶层参数；切换城市并刷新天气，暂停自动定位并取消旧 GPS 请求 |
| `cities.locateGps` | `{}` | 开启自动 GPS 定位，立即请求并在之后每 15 分钟检查；成功返回 City，失败/旧请求遭新选择取代返回 `location_unavailable`；仅读卫星来源 |
| `lyrics.refresh` | `{}` | 触发歌词重新匹配 |
| `lyrics.import` | `{ "trackKey": "...", "text": "..." }` | 导入 LRC 文本（限制 <= 12,000 字符） |
| `settings.patch` | `{ "chineseFontFamily": "微软雅黑", "westernFontFamily": null, "coverSaturation": 0.35 }` 等 | 旧 `fontFamily` 同时设置双字体，新字段优先；三个字体字段统一校验，失败返回 `font_not_found` 且整次不保存 |

新增只读状态：`weather.locationBusy` / `weather.locationStatus`、`settings.autoLocateCity`、`media.secondaryAccent`。`autoLocateCity` 不接受 patch；GPS 状态缺字段时按 false / 空字符串兼容，第二配色缺失/null 时回退 `media.accent`。`coverSaturation` 在 settings 与 appearance 均可读取，范围 0–1，缺字段时按 0.35 兼容。

---

## 4. 对 `0.6-dev` 任务栏占位约束的严格遵守

针对 [BACKEND_RESERVATION-0.6.md](./BACKEND_RESERVATION-0.6.md) 的规范要求，当前 Antigravity 前端严格执行以下边界：

1. **绝不自建或私下注入 Explorer**：不调用 Shell/Explorer 注入代码，不修改 Windows 任务栏缩小时尚策略，不额外创建占位 HWND。
2. **不抢占、不重复绘制系统任务栏胶囊**：当 `hello.capabilities.rendererReplacement == false` 或 `taskbarReservation == false`、且 `windows.hints.renderingOwner == "builtin"` 时，前端仅作为独立预览宿主（`MainWindow` 桌面工作台）与独立展开浮窗运行，**不在真实 Windows 任务栏托盘区叠画重复胶囊**。
3. **只读呈现原生占位遥测**：已在主控栏状态条汇总显示后端版本、`renderingOwner`（`builtin` / `preview`）、`layoutState` 与 `windows.reservation.snapshot.status / widthDip`，方便联调观察后端行为。

---

## 5. 前端自管范围与视觉特性（无需后端介入）

以下交互与视觉样式完全由 Antigravity 前端自行持久化与渲染（配置存放在程序目录 `antigravity-plugin.config.json`）：

- **窗口交互与弹出几何**：`PopupTrigger`（点击/悬停）、`PopupDirection`（上/下）、`CloseOnDeactivate`、`PinWeatherFlyout`、`PinLyricsFlyout`、展开窗口宽高、任务栏可用宽度模拟滑块（`SimulatedAvailableWidth`）。
- **无边框 DWM 玻璃浮窗**：通过 `OverlappedPresenter.SetBorderAndTitleBar(false, false)` + `ExtendsContentIntoTitleBar = false` + `DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND` + `DWMWA_BORDER_COLOR = DWMWA_COLOR_NONE` + 亚克力背景实现无白边/无黑边圆角玻璃浮窗，天气展开浮窗根据 `MainScrollViewer.ScrollableHeight` 自动精确贴合内容高度。
- **天气悬停简约摘要卡**：悬停任务栏天气胶囊弹出极简宽松卡（地点、体感温度、风速与湿度、未来 1h 降水单行摘要）。
- **天气展开动态天象光效**：根据 `forecast.code`、`isDay`、用户 `Glow` 强度与 `Animations` 开关绘制天象色温渐变、呼吸光斑与轻粒子动效（晴昼暖阳、晴夜星点、雨丝、飘雪、雷暴微闪、雾霭流云）。
- **原版单字体接入**：通过外层 `ContentControl` 继承 `appearance.fontFamily ?? settings.fontFamily`，保留 `FontIcon` 专用字体。2026-11-15 双字体升级待前端接入：按中西文片段使用对应新字段，字段存在且为 null 时恢复系统字体，仅缺字段时回退旧 `fontFamily`。
- **系统主音量滑块**：通过本地 WASAPI `IAudioEndpointVolume` COM 直接调节系统主音量与静音状态。

---

## 6. 建议下一阶段（`0.7+` 外部渲染接管）由 Codex 补充的接口

当后端准备开放外部前端正式取代内置任务栏胶囊渲染时，Antigravity 已备好渲染控件与胶囊规格（完整音乐 `238 DIP` / Lite `48 DIP`；完整天气 `104 DIP` / Lite `64 DIP`；间隙可配置 `gap`）。建议后端在下一版本补充：

1. **外部渲染器注册与租约接管（建议命名）**：
   - `windows.claimTaskbarRenderer`（或 `windows.registerRenderer`）：
     - 请求参数：`pid`、`capsuleHwnds`（或单胶囊宿主 `hwnd`）、`preferredSizes`（`musicFullDip`, `musicLiteDip`, `weatherFullDip`, `weatherLiteDip`, `gapDip`）。
     - 后端行为：校验独占租约 -> **自动隐藏后端内置 C++ / WPF 胶囊绘制** -> 将原 `StackPanel`/`Grid` 原生透明占位区域继续由后端与 Explorer 续租维护，并向该外部前端分配精确的物理坐标槽位 `slots.musicSlot` / `slots.weatherSlot`。
   - `windows.releaseTaskbarRenderer`（主动释放）；且后端沿用现有的进程存活探测 + 心跳超时（如 3s）兜底自动回退到内置渲染并清理/收回占位。
2. **槽位变更实时推送**：
   - 外部接管状态下，每当系统托盘左右缘、可用宽度、Lite 滞回或媒体隐退变化时，通过 `events.wait` 下发 `topics: ["windows"]`，前端读取 `windows.reservation.slots`（含 `mode: "full" | "lite" | "hidden"` 与 `rect`）即刻更新无边框胶囊 HWND 坐标与尺寸。
3. **清单自动发现与切换（可选）**：
   - 后续可在后端设置中扫描 `work/frontends/*/plugin.manifest.json`，提供切换渲染前端与一键拉起 `entry` 的入口。

---

## 7. 本地联调与验收指令

```powershell
# 1. 启动预览后端（独立数据目录 work\frontend-dev-data）
& "C:\Users\ROG\Documents\Codex\2026-11-14\ban\outputs\Antigravity前端接入包\start-backend.ps1"

# 2. 编译 Antigravity 前端至 dist
dotnet build "C:\Users\ROG\Documents\Codex\2026-11-14\ban\work\frontends\antigravity\AntigravityFrontend.csproj" -c Release -p:Platform=x64 -o "C:\Users\ROG\Documents\Codex\2026-11-14\ban\work\frontends\antigravity\dist" --no-restore

# 3. 启动前端（若无后端也可先开启右上角“示例数据”体验）
& "C:\Users\ROG\Documents\Codex\2026-11-14\ban\work\frontends\antigravity\dist\AntigravityFrontend.exe"
```

启动日志输出于 `dist\startup.log`，可用于核查 WinUI 初始化与窗口句柄创建时间线。
