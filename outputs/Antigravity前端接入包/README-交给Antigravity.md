# LumiBar 备选前端接入说明

更新：2026-11-15。协议：`LumiBar Frontend API v1`，当前发布版本 `0.7.48`；hello 的 backendVersion 暂为系列标识 `0.7-dev`。新增字段向后兼容；前端应允许字段缺失，以兼容旧后端。

新增 [插件菜单注册](./PLUGIN_MENUS-0.7.46.md)：只支持托盘与设置左侧，附已验证的 C# 示例。

设置切换异常与回写保护见 [SETTINGS_LIFECYCLE_FIX-0.7.45.md](./SETTINGS_LIFECYCLE_FIX-0.7.45.md)。

> 最新接入顺序：先看 [BACKEND_RENDERER-0.7.md](./BACKEND_RENDERER-0.7.md) 的真实任务栏接管，再看 [ACCENT_COLORS-0.7.44.md](./ACCENT_COLORS-0.7.44.md) 的九色强调色和 [GLASS_SETTINGS_UPDATE-0.7.41.md](./GLASS_SETTINGS_UPDATE-0.7.41.md) 的玻璃设置更新规则。定位以 [BACKEND_LOCATION-2026-11-15.md](./BACKEND_LOCATION-2026-11-15.md) 为准。早期请求逐项回复保留在 BACKEND_RESPONSE-0.4.md 与 BACKEND_FONTS-0.5.md。

## 1. 任务与分工

请为 LumiBar 制作一套可独立启动、可单独交付的备选前端插件。现有后端继续由 Codex 维护；你负责前端视觉、窗口和交互。代码放在：

`C:\Users\ROG\Documents\Codex\2026-11-14\ban\work\frontends\antigravity`

后端工程：

`C:\Users\ROG\Documents\Codex\2026-11-14\ban\work\LumiBar\LumiBar.csproj`

后端已经接好本机命名管道，支持真实天气、媒体会话、歌词、20 段频谱、封面与播放控制。前端通过协议读取数据和发出操作。请保持后端源码、协议 v1 和现有前端文件完整；新增接口需求写入自己目录的 `FRONTEND_REQUESTS.md`，便于双方协作。

**窗口由前端插件自己创建和管理。** 展开触发方式、悬停延迟、弹出方向、窗口宽高、拖拽缩放、失焦收起、固定、页面切换、动画和键盘交互，都属于前端职责。后端不会强制套用现有浮窗或固定尺寸。

技术栈优先考虑独立 C# + WinUI 3 前端，便于使用真实桌面亚克力。也可制作 Electron / WebView2 方案；网页组件负责内容，桌面宿主负责窗口与原生材质。shadcn/ui、Radix 可作为组件与配色参考。普通浏览器网页不能直接访问 Windows 命名管道。

### 已确认的加载与通信选择

**选择独立前端宿主 + Windows Named Pipe + JSON Lines。** 前端可以用 WinUI 3，但交付物是单独的前端进程，不是由后端 `Assembly.Load` 的 WinUI 类库。不要求实现 `ITaskbarPlugin`，不跨进程共享 `UserControl`、`DispatcherQueue` 或 ViewModel 实例。

前端自行建立 ViewModel，把协议快照映射到数据绑定。既有 `IWidgetPlugin` 仅作为未来进程内组件方案预留，不是这次 Antigravity 的接入入口。

**v1 保持请求 / 响应格式，0.4-dev 已新增 `events.wait` 长轮询变化通知。** `state.get` 低频读取完整快照；`spectrum.get` 独立高频读取频谱。可增加独立事件连接以减少状态轮询，原有方法保持可用。不要等待不存在的 `state.changed` 消息，也不要另建 Local WebSocket 服务作为后端接入前提。

## 2. 本轮交付范围

交付一个独立预览插件，包含两个胶囊、天气浮窗、歌词浮窗和设置页。预览模式可以与现有 LumiBar 同时运行，使用独立后端配置。用户对比后再选择正式前端。

当前 `hello.capabilities` 中：

- `independentWindows: true`：插件自由管理自己的窗口。
- `rendererReplacement`：正常实机后端为 true，独立预览后端为 false；正式接管走注册、激活、续租、释放流程。
- `taskbarReservation`：正常后端且本机任务栏预留组件兼容时为 true；请读取实际 hello 能力和槽位/租约状态，独立预览后端为 false。

本目录的 `plugin.manifest.example.json` 用于描述交付物。现有 `Widgets.cs` 是另一套预留的进程内组件接口；本方案走进程外 Frontend API，不要将这个清单交给旧扫描器。

## 3. 用户已经确认的设计要求

- 两个独立胶囊：**音乐在左，天气在右，天气靠近系统托盘**。
- 通透玻璃、柔和磨砂、封面取色光效；胶囊边框低存在感。
- 标准音乐胶囊同时显示歌名和歌手；频谱融合在歌曲信息背景，不能单独占一栏。文字始终清晰。
- 悬停音乐胶囊出现上一首、播放 / 暂停、下一首。控件出现不改变胶囊外部宽度，不推动天气。
- 暂停后音乐胶囊继续显示，频谱回落；媒体会话结束后淡出。不要把暂停、静音、封面缺失当作音乐关闭。
- 音乐浮窗以歌词为主；封面、歌名、歌手和控制缩在底部。封面色彩柔和地融入亚克力。
- 天气浮窗参考 WidBar：当前天气、24 小时趋势、多日预报、风速、空气质量，另有一小块未来一小时降水趋势。
- 天气支持城市搜索、收藏和 Windows 自动定位，接受 Windows 返回的 Wi-Fi / 卫星等来源。新安装不带个人城市，手动选城或自动定位后获取天气。
- 跟随 Windows 明暗模式；支持手动选择。任务栏主题与应用主题可能不同，分别读取后端主题字段。
- 设置页分组清楚，不在右上角堆固定、刷新等无关按钮。
- 空间紧张时**两个胶囊一起转 Lite，继续保留**；音乐缩成封面与播放状态，天气保留天气图标与温度。空间恢复后展开，保留缓冲，避免来回切换。
- 用户最终希望 Windows 程序图标缩小或进入溢出区域，为 Lite 胶囊让位。请演示该交互；实际 Explorer 适配由后端负责，前端不修改系统任务栏内部结构。

建议起点：音乐标准宽度 238 DIP，天气 104 DIP；Lite 音乐 48 DIP、天气 64 DIP；尺寸可按可读性调整。概念图中的尺寸只用于讨论，实际高度按任务栏、DPI 和点击目标调整。Lite 中歌名和歌手可通过悬停提示完整显示。

设计图位于相邻目录 `..\concepts\`：

- `LumiBar-玻璃界面概念-v2.png`：天气、歌词、设置的总体方向。
- `LumiBar-双胶囊与Lite交互.png`：标准、悬停、Lite 三种状态。

图中数据均为示例。落地时以本说明中的状态逻辑和真实数据为准。

## 4. 启动与连接

需要 Windows、.NET 8 SDK；使用 Node 示例还需要 Node 18+。在接入包目录运行：

```powershell
.\start-backend.ps1
```

脚本编译现有后端，以 `--frontend-backend` 启动隐藏的独立后端。该模式不显示系统托盘图标或现有胶囊；设置位于工作区的 `work\frontend-dev-data`。后台仍读取真实媒体会话和系统输出音频。

接口描述文件：

`C:\Users\ROG\Documents\Codex\2026-11-14\ban\work\frontend-dev-data\frontend-endpoint.json`

```json
{"protocolVersion":1,"pipeName":"LumiBar.Frontend.v1.<用户标识>.<会话ID>.Preview","pid":12345,"mode":"preview","startedAt":"2026-11-14T09:00:00Z"}
```

读取 `pipeName`，连接 Windows 路径 `\\.\pipe\<pipeName>`。不要硬编码用户标识或进程号。服务仅接受当前 Windows 用户的本机连接，不监听 TCP 端口。请在普通用户桌面会话里运行客户端；受限开发沙箱可能拒绝命名管道访问。

连接测试：

```powershell
node .\smoke.mjs "C:\Users\ROG\Documents\Codex\2026-11-14\ban\work\frontend-dev-data\frontend-endpoint.json"
```

启动脚本的编译输出为 `work\frontend-backend-build`。重新编译前退出该独立开发后端，避免 EXE 锁定；不要停止用户正在使用的正式实例、WidBar 或 FluentFlyout。开发后端无退出 UI，可先核对描述文件 PID 对应的完整可执行路径，再结束该开发进程。

## 5. 传输协议

UTF-8，无 BOM；每一行一个 JSON 对象，以 LF 结束。连接可复用。每连接串行处理请求，响应携带同一个字符串 `id`。

第一条请求：

```json
{"id":"1","method":"hello","params":{"protocolVersion":1,"clientName":"antigravity-preview"}}
```

成功：`{"id":"1","ok":true,"data":{...}}`

失败：`{"id":"1","ok":false,"error":{"code":"version_mismatch","message":"..."}}`

约束：请求最多 16,384 个 UTF-16 字符，`id` 为 1–64 字符；JSON 深度最多 16；最多 4 个并发连接；空闲 60 秒断开；慢速写出超过 5 秒断开。错误 JSON 会返回结构化错误，超长输入直接断开。正常控制命令不要重试发送，防止重复跳歌；连接恢复后重新握手、读取状态。

连接断开时保留最近状态并显示离线提示，按 1、2、5、10 秒间隔重连，最长 10 秒。超时只说明客户端停止等待，命令可能已经执行；重新读取状态后再决定下一步。

## 6. 可用接口

| 方法 | params | 返回 / 行为 |
|---|---|---|
| `hello` | `protocolVersion:1, clientName?` | 版本、能力、方法列表 |
| `state.get` | `{}` | 天气、媒体、歌词、主题、设置完整快照 |
| `spectrum.get` | `{}` | `bands[20]`、播放状态、采样时间、来源 |
| `media.artwork` | `trackKey` | 当前歌曲封面 Base64、MIME；歌曲变化报 `stale_track` |
| `media.toggle` | `{}` | 播放 / 暂停 |
| `media.previous` | `{}` | 上一首 |
| `media.next` | `{}` | 下一首 |
| `media.seek` | `seconds` | 相对歌曲起点的秒数，后端限制在有效范围 |
| `weather.refresh` | `{}` | 发起更新，立即返回 `accepted:true`；重复在途刷新合并 |
| `cities.search` | `query` | `City[]`，可能耗时，请显示加载状态 |
| `cities.select` | `name, latitude, longitude, region?, timeZone?` | 保存城市并刷新天气 |
| `cities.locateGps` | `{}` | 开启自动定位并立即请求 GPS；成功返回 City 并选中位置；每 15 分钟检查一次，手动切城后暂停 |
| `lyrics.refresh` | `{}` | 重新读取 / 匹配当前歌词 |
| `lyrics.import` | `trackKey, text` | 导入当前歌曲 LRC；本协议文本限制 12,000 字符，整条请求仍受 16,384 限制 |
| `settings.patch` | 下节允许字段的部分对象 | 保存并返回完整设置 |
| `windows.hints` | `{}` | 任务栏和胶囊锚点、缩放比例；窗口布局由插件决定 |

`accepted:true` 表示请求已接受。天气刷新、歌词刷新通过后续快照观察结果；媒体命令要由播放器确认，播放器拒绝会返回错误。

错误码：`invalid_request`、`invalid_json`、`invalid_params`、`handshake_required`、`version_mismatch`、`method_not_found`、`no_session`、`stale_track`、`city_required`、`font_not_found`、`location_unavailable`、`shutting_down`、`operation_failed`。处理错误后界面继续可用。

GPS 只接受 Windows 的卫星来源。UI 与 API 共用定位请求：手动切城、关闭自动定位或新定位请求会取消旧请求；旧结果不会覆盖新选择。定位失败或请求遭新选择取代时返回 `location_unavailable`。`state.weather.locationBusy`、`locationStatus` 提供忙碌和提示文字；旧后端缺字段时按 false / 空字符串处理。`state.settings.autoLocateCity` 供前端读取，当前不接受 `settings.patch` 修改。

## 7. 数据语义

完整类型见 `contracts.d.ts`，示例见 `mock-state.json`。

### 后端当前实际输出的数据结构

| 类别 | 字段与类型 | 约定 |
|---|---|---|
| 当前天气 | `temperature:number, feelsLike:number, code:number, humidity:number, wind:number, windDirection:number, rain:number|null` | °C、WMO 天气码、%、km/h、角度、降水概率 |
| 小时预报 | `hours:[{time,temperature,code,rain}]` | 最多未来 24 小时，城市当地时间 |
| 日预报 | `days:[{date,min,max,code,rain}]` | 天数按源实际返回，最多 10 天 |
| 空气质量 | `aqi, pm25, pm10:number|null`，`airError:string|null` | US AQI，颗粒物 µg/m³；独立缺失态 |
| 一小时降水 | `weather.precipitationNextHour:PrecipitationTrend|null` | 滚动的未来 60 分钟窗口，15 分钟区间降水量；带精度说明与完整性标记 |
| 歌曲信息 | `key,title,artist,album,source:string` | key 用于封面和歌词版本一致性检查 |
| 播放状态 | `playing:boolean, position:number, duration:number` | 秒；`media:null` 表示无会话 |
| 控制能力 | `controls:{toggle,previous,next,seek}` | 各值为 boolean，按播放器实际能力显示 |
| 封面与色彩 | `hasArtwork:boolean, accent:string, secondaryAccent?:string|null`；单独 `media.artwork` 返回 `{trackKey,mimeType,base64}` | 配色为 #RRGGBB；第二配色缺失/null 时沿用主色；图像按需取，不随频谱反复传 |
| 实时频谱 | `spectrum.get → bands:number[20]` | 固定 20 个对数频带，FFT 后归一化 0–1；不是原始 PCM 或 10 / 26 段 |
| 歌词 | `lyrics:{trackKey,status,offsetSeconds,lines:[{seconds:number|null,text:string}]}` | 已解析 LRC；null 秒数为无同步时间的纯文本 |

WMO 天气码保留源数值，前端可统一映射为自己的枚举与图标：0 晴；1 晴间多云；2 多云；3 阴；45 / 48 雾；51–57 毛毛雨；61–67 雨；71–77 雪；80–82 阵雨；85 / 86 阵雪；95–99 雷雨。天气代码本身不区分白天 / 夜晚；日夜图标需要结合城市时间，0.4-dev 已提供 `forecast.sunrise`、`forecast.sunset`、`forecast.isDay` 和 `hours[].isDay`，优先采用实际日夜标记。

频谱是 2048 点 FFT 聚合出的 20 段对数能量，目标范围约 45 Hz–16 kHz。它是用于绘制的归一化强度，不是声压级测量值。`contracts.d.ts` 用 `number[]`，运行时约定长度固定为 20；前端允许对这 20 段作视觉插值。

C# / WinUI 3 可直接加入 `LumiBarClient.cs`：

```csharp
using var client = new LumiBar.Frontend.Sdk.LumiBarClient();
await client.ConnectAsync(endpointPath);
var state = await client.RequestAsync("state.get");
var hints = await client.RequestAsync("windows.hints");
// 下一首按钮点击时：await client.RequestAsync("media.next");
```

Node / Electron 可导入 `lumibar-client.mjs`。建议在主进程维护连接，通过范围明确的 IPC 向 renderer 提供状态和操作；不要把整个 Node API 暴露给网页。

- `media:null`：没有可显示的媒体会话。后端已经做会话短暂断开的缓冲，前端再完成淡出即可。
- `media.playing:false`：暂停 / 非播放状态，胶囊继续存在。
- `controls` 各标记为 false 时禁用对应控件，并提供简短说明。不要伪造操作成功。
- `position`、`duration`、歌词 `seconds` 均为秒。`duration:0` 时禁用进度拖动；前端可用单调时钟补间显示，后续快照校正。
- `lyrics.lines[].seconds:null` 表示纯文本歌词，不参与时间定位。`offsetSeconds` 加到播放位置后寻找当前行。相同时间的原文与译文会合并为带换行的文本。
- 封面在歌曲 key 改变后读取一次、按 key 缓存；不要随频谱反复传大图。无封面使用统一占位，保留歌手名。
- `accent` 为 `#RRGGBB`，用于背景光效。按界面主题调整强度，正文使用独立前景色。
- 频谱为 20 个 0–1 数值，来源为**系统输出音频**，当前未隔离到单个播放器。暂停时后端返回全零。前端负责视觉缓动，不能用随机条形伪造实时音频。
- 天气温度恒为摄氏度，风速恒为 km/h，湿度和降水概率为百分数；设置中的华氏度和 m/s 由前端换算。AQI 当前为 **US AQI**，PM2.5 / PM10 单位 µg/m³。
- `forecast.updated` 是本机拉取时间；小时与日期字符串为天气城市当地时间，勿直接当 UTC 解析。失去网络时保留缓存，更新时间和状态仍应可见。
- `forecast:null`、AQI 为 null、`airError`、无歌词、空媒体都需要完整的空态 / 错误态。
- MET Norway 备用源当前缺少降水概率，`source=="MET Norway"` 时隐藏概率数值，不能把小时记录的占位 0 展示为“无雨”。
- **一小时降水已经接入 `weather.precipitationNextHour`。** `windowStart` / `windowEnd` 为当前未来 60 分钟；每个 `intervals` 项含绝对时间 `start`、`end` 与该完整 15 分钟区间的 `amountMm`。窗口可能与首尾区间部分重叠，因此通常有 4–5 个区间；不要直接把它们相加当成严格未来 60 分钟总量，也不要按分钟拆出虚构观测值。
- `kind:"model-trend"`、`intervalMinutes:15`、`precisionNote` 明确说明预报精度。奎文区来自小时预报插值，界面使用“15 分钟降水趋势”。`amountMm:null` 保留为图表缺口，`complete:false` 显示“部分数据暂缺”，整个字段为 null 时显示“短时降水暂不可用”。正常 0 mm 可以显示“预计暂无降水”。不要标成雷达分钟级预报或精确下雨倒计时。
- 后端缓存约 3 小时的短时预报，按每次读取的当前时间截取未来一小时；无需每秒重新请求天气网络 API。备用源当前返回 null。`forecast.shortTermPrecipitation` 是原始短时序列，前端优先使用已经裁好窗口的 `weather.precipitationNextHour`。

建议轮询：`state.get` 可见时 1 秒、收起后 3–5 秒；频谱仅在可见且播放时 25–30 Hz；页面隐藏后停止频谱轮询。窗口开闭无需等待后端。频谱与慢速城市搜索可用两个独立连接，避免互相阻塞。网络刷新由后端调度，别用 `weather.refresh` 代替状态轮询。

## 8. 设置更新

`settings.patch` 允许以下字段，其他字段拒绝：

`theme, fontFamily, chineseFontFamily, westernFontFamily, coverGlow, coverSaturation, glow, glassOpacity, animations, showWeather, showMusic, spectrum, hideInFullscreen, autoLyrics, weatherRefreshMinutes, fahrenheit, windMetersPerSecond, gap, musicWidth, lyricsFontSize, lyricsOffset, sensitivity`

范围：`glow .08–.55`、`coverSaturation 0–1`、`glassOpacity .35–.95`、`sensitivity .5–2.5`、`lyricsOffset -60–60 秒`、`gap 4–24 DIP`、`musicWidth 180–340 DIP`、`lyricsFontSize 18–36`；刷新间隔只接受 10 / 20 / 30 / 60 分钟。后端规范化数值，前端以返回值回填控件。

2026-11-15 字体扩展：`chineseFontFamily` 与 `westernFontFamily` 独立选择，null 恢复对应文字的系统字体。旧 `fontFamily` 请求同时设置两者；同一次请求中的新字段优先，显式 null 也优先。三个字段都接受已安装字体的规范名或别名，统一保存规范名；任一字体不存在时返回 `font_not_found`，整次修改不落盘。读取规则见 [BACKEND_FONTS-0.5.md](./BACKEND_FONTS-0.5.md)。

`state.appearance.coverSaturation` 与 `state.settings.coverSaturation` 返回封面着色饱和度，缺失时按 0.35 兼容。`state.media.secondaryAccent` 是封面第二配色（`#RRGGBB` 或 null），缺失或为 null 时沿用 `media.accent`；该字段只读，不接受 `settings.patch`。详见 [BACKEND_APPEARANCE-2026-11-15.md](./BACKEND_APPEARANCE-2026-11-15.md)。

插件自己的弹出方式、宽高、悬停延迟、动效等写入**插件配置**，不要塞进后端设置。启动项、进程内插件启用和安装管理本轮不开放修改。

## 9. 窗口与交互所有权

### 任务栏分工，双方不要各跑一套定位器

| 工作 | 负责方 |
|---|---|
| `Shell_TrayWnd`、`TrayNotifyWnd` 发现，托盘宽度、程序图标占用检测 | Codex 后端 / 平台适配层 |
| Explorer 重启、任务栏自动隐藏、全屏、DPI 和显示器变化 | Codex 后端 / 平台适配层 |
| 可用空白区域 `BlankZoneRect` 的可信测量、Lite 决策、系统图标避让与空间预留 | Codex 后端 / 平台适配层 |
| 正式任务栏胶囊的外部屏幕位置、宽高和移动协调 | 后端统一下发；前端宿主按布局结果安放自己的胶囊窗口 |
| 胶囊内部布局、文字、封面、频谱、悬停控制、光效 | Antigravity 前端 |
| 浮窗何时展开、方向、尺寸、固定、焦点、滚动与动画 | Antigravity 前端 |
| 自己的窗口消息、窗口关闭与释放、DPI 资源重绘 | Antigravity 前端宿主 |

**当前已经开放的几何信息**包括 `windows.hints` 中的 `taskbar` 外框、DPI `scale`、内置胶囊锚点、`blankZoneRect`、`geometryStatus`、`measuredAt` 和 `layoutRecommendation`。`blankZoneRect` 为实际 XAML 程序按钮右侧到托盘左侧的矩形；无空间时宽度为 0。读取失败、布局未知或测量超过 800 ms 时返回 null / `unavailable`，前端不要继续沿用旧坐标。

`layoutRecommendation` 中 `weatherWidth` / `musicWidth` / `requiredWidth` 均为物理像素；`lite` 表示双 Lite 建议，`requiresReservation:true` 表示空白不足，需要系统预留，不允许按宽度直接覆盖程序按钮。完整模式恢复时额外留 16 DIP 缓冲，避免临界宽度反复切换。音乐会话不存在或用户关掉对应模块时，其宽度为 0。

0.4-dev 可通过 `events.wait` 接收 windows 变化主题，并通过 `windows.planCapsules` 自定义尺寸、获取 `musicSlot` / `weatherSlot` / `trayWidthPx`。实际注册外部胶囊 HWND 的接口仍待接入。`taskbarReservation` 仍为 false，正式窗口调度尚未接通。前端先在独立预览宿主中消费这些布局数据；对新增能力检查 `hello.capabilities.taskbarGeometry`。

0.4-dev 已提供胶囊槽位矩形及期望 / 最小宽度规划；后续正式挂接仍需窗口注册、系统空间预留和生命周期协议完成后再开放相应 capability。前端无需自己监听 `Shell_TrayWnd` / `TrayNotifyWnd`，也不应同时启动另一个胶囊位置平滑器，避免与后端指令打架。

浮窗仍由前端按自己的胶囊锚点自由定位，后端不会因为接管任务栏布局而限制浮窗宽高或弹出动效。

`windows.hints` 返回的是建议锚点，绝不调用后端内置天气 / 音乐窗口。所有位置均为屏幕**物理像素**，包含可能为负值的多显示器坐标；`scale` 用于 DIP / 像素换算。不要把物理像素直接当 CSS px。

插件自行选择鼠标悬停、单击、双击、快捷键、点击外部、Esc、固定窗口等行为，并处理焦点、DPI、工作区边缘和多显示器。弹窗必须落在当前显示器可见工作区内；前端宿主调用系统监视器 API 获取工作区。

独立后端模式中内置胶囊没有显示，因此 `weatherAnchor.visible` / `musicAnchor.visible` 通常为 false；此时使用插件自己绘制的胶囊作为锚点。`taskbar:null` 或任务栏隐藏时，保留独立预览窗口，不猜测系统位置。

插件应把“显示一个窗口”与“动画如何展开”分开：窗口边界覆盖需要的内容，内容层执行透明度 / 位移 / 缩放动画，避免每帧重排整个桌面窗口。触控、键盘焦点进入也应能使用悬停控件；尊重减少动画、高对比度和关闭透明效果的系统偏好。

不要通过反复压缩 Explorer 窗口、改全局图标大小或注入未经验证的代码来抢占空间。后端会单独提供经过版本检测的空间预留能力；等 capability 开启后再接正式任务栏。当前模拟完整 / Lite / 拥挤状态应在预览宿主内完成。

## 10. 前端验收清单

1. 两个独立胶囊、左右顺序正确，正常 / 悬停 / Lite / 暂停 / 会话关闭状态完整。
2. 标准态可读到歌名和歌手，背景频谱不占独立栏位；悬停不改变外部宽度。
3. 暂停不消失，关闭媒体会话后淡出；没有歌曲时天气仍可用。
4. 歌词优先、封面光效、无歌词 / 封面 / 时长时的状态完整。
5. 日夜主题、125% / 150% / 200% DPI、长歌名 / 长城市名和窗口边缘均可用。
6. 天气有更新时间、手动刷新和错误反馈；GPS 不回退到 IP。
7. 后端断开可恢复，重连不重复跳歌；慢搜索不拖慢频谱动画。
8. 无数据时不伪造实况；预览数据带标签。
9. 弹出方向、窗口尺寸与动画能独立调整，保存在插件配置。
10. 给用户提供可执行预览、源码、清单、启动说明及截图；列出需要后端补充的接口。

## 11. 可以直接发给 Antigravity 的任务

> 阅读本文件、contracts.d.ts、客户端示例和两张概念图。为 LumiBar 做独立备选前端插件，源码放在指定的 work/frontends/antigravity 目录。由你自行设计和实现窗口展开方式、弹出方向、尺寸、动效、页面布局及插件专属设置；通过 Frontend API v1 复用后端数据和控制。先完成可独立启动的预览，并可切换真实后端 / 明确标记的 Mock 数据。遵守已确认的音乐左、天气右、背景频谱、歌名加歌手、悬停控制、双 Lite 保留和歌词优先要求。保持现有后端和正式 UI 文件完整。未开放的任务栏空间预留和正式前端替换先记录接口需求，不自行修改 Explorer。

## 12. 本轮验证

- 后端 Windows x64 构建成功。
- 87 项自动检查通过，覆盖设置恢复、请求取消、播放状态、Lite 布局、实际按钮几何、负坐标、降水时间窗口 / 时区 / 缺失 / 过期、协议握手与客户端。
- 使用本包 `lumibar-client.mjs` / `smoke.mjs` 实连独立后端成功：协议 v1，奎文区 Open-Meteo 天气、真实媒体状态、20 段频谱、前端拥有窗口尺寸。
- `0.3-dev` 真实验证：空白区域返回 `geometryStatus:measured`，降水返回 5 个覆盖未来一小时的有效区间。几何随任务栏变化，不能把测试数值写死。
- 开发环境无法访问 NuGet 漏洞元数据，构建出现 NU1900 警告；依赖恢复与编译成功。
- 正式任务栏空间预留、自动装载备选 UI、前端热切换是下一阶段工作。

## 0.6-dev 更新

任务栏内置原生占位、双 Lite 与系统缩小/溢出已实测。外部窗口接管仍未开放；请先读 BACKEND_RESERVATION-0.6.md，按 capabilities 区分内置能力与插件能力。

最新界面协作：[城市栏与音乐胶囊微调](./UI_POLISH-0.7.47.md)。播放控件按用户最终选择恢复细线。

[0.7.48 胶囊整体下移](./CAPSULE_ALIGNMENT-0.7.48.md)：槽位已包含 2 DIP 偏移，前端不要重复添加。
