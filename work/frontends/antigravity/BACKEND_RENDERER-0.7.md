# LumiBar 0.7 外部渲染接管协议

面向 Antigravity WinUI 3 前端。2026-11-15，协议仍为 `protocolVersion: 1`，正常后端 `backendVersion: 0.7-dev`。

## 1. 连接哪个后端

正式替代接入连接 **正常 LumiBar**：`%LOCALAPPDATA%\LumiBar\frontend-endpoint.json`。当前可执行文件在 `outputs/LumiBar-实机版/LumiBar.exe`。

`work/frontend-dev-data` / `--frontend-backend` 是独立预览端点，不管理真实任务栏，注册返回 `preview_only`。本轮没有重启或替换 Antigravity 已运行的独立预览后端。不要继续用该端点尝试正式接管。

每条管道独立 hello。检查：

```json
{"id":"hello","method":"hello","params":{"protocolVersion":1,"clientName":"antigravity-winui"}}
```

正常后端 `mode: builtin`；`rendererReplacement: true`、`rendererLease: true`、`frontendDiscovery: true`。`taskbarReservation` / `builtinTaskbarReservation` 表示当前原生预留组件可用；启动时可能暂为 false，稍后读取 `windows.reservation`。`capabilities.maxConnections: 16` 是整个后端的并发上限。

## 2. 三条管道与心跳

保持现有 **State / Events / Spectrum** 三条长期连接，字体查询可开第 4 条短连接。事件挂起 25 秒不会挡住状态命令或频谱。每条连接内部仍按请求—响应串行，不要在同一条管道上并发发送帧。

租约按 Windows 实际客户端 PID + 随机 leaseToken 校验，**不绑定某一条管道**。可以在主状态通道每 1 秒续租，也可用第 4 条常驻控制通道，字体查询另开短连接。不要把续租排在 `events.wait` 后面；状态通道做慢操作时优先保证续租。某条事件/频谱连接断开不会单独释放租约。

租约有效期 **5000ms**，建议 **1000ms** 续租，后台每约 **250ms** 检查。截止时间用单调时钟，修改系统时间不延长租约。使用响应里的 `expiresInMs` 设置前端本地失效计时器，保守扣除往返耗时。不要把 token 写入日志、清单或设置。

## 3. 注册 → 准备 → 确认接管

创建两个不同的顶层胶囊 HWND，必须属于调用管道的同一进程。**注册时两个窗口都要隐藏**。首版不接受一个 HWND 同时画两个胶囊；两个胶囊可共享控件和 ViewModel。详情/悬停/设置窗口不注册，仍由前端自管。

```json
{"id":"register","method":"windows.registerRenderer","params":{
  "rendererId":"antigravity.lumibar-glass",
  "pid":12345,
  "capsuleHwnds":{"music":"0x000A1234","weather":"0x000B5678"},
  "preferredSizes":{"musicFullDip":238,"musicLiteDip":48,"weatherFullDip":104,"weatherLiteDip":64,"gapDip":8,"heightDip":34}
}}
```

HWND 是十进制或 `0x` 十六进制**字符串**。PID 来自 `Environment.ProcessId`；后端通过 `GetNamedPipeClientProcessId` 验证实际 PID，不信任请求里自报的身份。

注册响应：

```json
{"leaseId":"…","leaseToken":"…","phase":"pending","leaseDurationMs":5000,"renewAfterMs":1000,"expiresInMs":5000,"slotsVersion":12,"slots":{"musicSlot":{"rect":{"left":1302,"top":1532,"right":1697,"bottom":1589},"mode":"full"},"weatherSlot":{"rect":{"left":1711,"top":1532,"right":1884,"bottom":1589},"mode":"full"},"state":"full"},"renderingOwner":"builtin"}
```

坐标仅为格式示例。`pending` 时后端按请求尺寸维护预留，但内置界面继续显示，外部胶囊不得显示。开始续租，读取真实 slots，准备布局/封面/首帧。准备好后：

```json
{"id":"activate","method":"windows.activateTaskbarRenderer","params":{"leaseId":"…","leaseToken":"…"}}
```

成功返回 `phase: active`、`renderingOwner: external`、最新 slots。后端先立即隐藏内置胶囊、收起内置天气/音乐详情与速览，再返回成功。**拿到成功响应后**，前端按 slots 定位并显示自己的两个胶囊。不要在注册成功就显示。

`reservation_not_ready`：原生预留宽度还在调整，保持窗口隐藏、继续续租，约 200–500ms 后重试 activate。`reservation_unavailable`：暂时没有可用的原生预留。建议保留预览工作台并显示状态，不抢画任务栏。

首版尺寸范围：音乐 full 32–600 DIP、weather full 32–400 DIP，Lite 24 DIP 起且不能大于 full；gap 0–24 DIP，height 24–64 DIP。实际高度还会收敛到任务栏高度以内。当前原生预留最大 700 DIP，过大的组合不能保证激活成功，请使用上面的默认尺寸。

## 4. 真实槽位与事件

`windows.reservation` 返回：

```text
snapshot       原生占位组件的状态、宽度、Explorer PID
slots          当前真实 musicSlot / weatherSlot / state
slotsVersion   槽位内容变化递增，后端重启归零
owner          builtin | external | preview
renderer       { owner, lease: null | { leaseId, rendererId, pid, phase,
                 expiresInMs, leaseDurationMs, renewAfterMs, sizes }, lastReleaseReason }
```

`windows.hints` 也包含 `renderingOwner`、`slotsVersion`、`renderer`、`musicSlot`、`weatherSlot`、`layoutState`。接管时 `musicAnchor` / `weatherAnchor` 是隐藏的内置 HWND，只用于诊断，**不要用它们定位外部窗口**。

槽位的 `rect` 使用**物理屏幕像素**，可能有负坐标；尺寸是 `right-left` / `bottom-top`，切勿再乘 DPI。控件内部 DIP 布局使用 taskbar.scale 或 HWND DPI 换算。slots 是内容轮廓，若额外留透明阴影边距，前端自行偏移 HWND，透明区域应鼠标穿透，不能挤占其他图标。

托盘宽度、任务栏位置、Lite 切换、媒体关闭、系统隐藏任务栏等造成槽位改变时发布 `topics: ["windows"]`。收到事件读取 `windows.reservation`；仍可每 15 秒兜底读取。`windows.planCapsules` 只是假设布局计算，不是已分配槽位。

- `mode: full / lite`：按 rect 放置并使用相应布局。
- `mode: hidden`、`rect: null` 或整体 `state: unavailable / hidden`：隐藏对应 HWND，不画缓存的旧槽位。
- 两个胶囊先一起 Lite，再由原生预留推动系统图标压缩/溢出；前端不要创建第二份 Explorer 占位。
- 暂停音乐仍保留音乐槽位，媒体会话关闭才隐藏。
- 后端继续维护占位及胶囊相对任务栏的层级；外部前端负责 HWND 尺寸、平滑移动、显示、点击和所有弹窗行为。

## 5. 续租、释放和自动回退

```json
{"id":"heartbeat","method":"windows.renewTaskbarRenderer","params":{"leaseId":"…","leaseToken":"…"}}
{"id":"release","method":"windows.releaseTaskbarRenderer","params":{"leaseId":"…","leaseToken":"…"}}
```

续租响应包含新的 `expiresInMs`、phase、slotsVersion、slots、renderingOwner；续租不会生成一串无意义的 windows 事件。其他响应的 `leaseToken` 为 null，仅注册响应给出密钥。

释放前先隐藏前端自己的详情、速览和胶囊，随后 release。后端会隐藏已注册的外部胶囊 HWND，再恢复内置胶囊；不结束前端进程。

自动回退原因：

- `heartbeat_timeout`：5000ms 没有成功续租。
- `process_or_window_lost`：进程退出、进程启动时间改变或任一注册窗口失效。
- `reservation_lost`：原生预留丢失/不足，恢复内置安全布局。
- `user_release`：用户在插件页选择恢复内置界面。
- 后端正常退出会隐藏注册胶囊；后端崩溃时，前端必须依靠自己独立的租约计时器隐藏全部自管窗口。

`lease_invalid` / `lease_expired`、连接持续失败或 instanceId 变化时立即停止贴任务栏并隐藏全部自管窗口。重连后重新注册，不能重用旧 token。后端能强制隐藏两个已注册 HWND；未注册的外部详情/速览必须由前端自己收起。租约失效后不要再次显示过期窗口。

常见错误：`peer_mismatch`（自报PID与管道身份不一致）、`window_owner_mismatch`（外进程/非顶层/无效HWND）、`windows_must_be_hidden`、`renderer_busy`（已有独占租约）、`preview_only`、`invalid_params`。同进程重复注册也返回 renderer_busy；丢失注册响应时最多等待租约过期或在设置页恢复内置。

## 6. 前端清单管理

设置 → 插件已接入：发现外部前端、启动、查看当前渲染方、恢复内置界面。保留原有同进程插件清单展示，不加载第三方类库代码。第三方自定义菜单项注册不在 0.7 中。

只读发现：`frontends.list {}`；显式启动：`frontends.launch {"id":"antigravity.lumibar-glass"}`。

扫描位置：应用旁 `frontends/`、`%LOCALAPPDATA%/LumiBar/frontends/`，以及当前工作区 `work/frontends/`（开发兼容）。扫描深度上限 2，最多每目录100份清单；只读取 `plugin.manifest.json` 中 `kind: external-frontend`、`protocolVersion: 1` 的入口。入口必须是清单目录内的 exe，拒绝绝对路径、越界路径、符号链接/目录联接和重复 id。扫描不会执行程序。

用户点启动或调用 launch 后，后端使用 exe 原生进程启动，不经 shell，不执行清单中的命令字符串。传入环境变量：

```text
LUMIBAR_ENDPOINT_FILE = 正常后端 frontend-endpoint.json 的绝对路径
LUMIBAR_RENDERER_MODE = taskbar
```

Antigravity 需要优先读取这两个环境变量。未适配的旧版仍可以开预览工作台，但启动成功本身不会隐藏内置胶囊；只有注册+activate成功才接管。首版不自动开机启动外部前端，不因发现清单自动切换。

## 7. 已做验证与接入验收

- 185/185 后端测试：独占、错误PID/密钥、过期、旧token、Lite自定义尺寸、三长连接+短连接并发。
- 实机两个独立测试HWND：pending保留内置、activate隐藏内置、跨管道续租、关事件通道仍保有租约、release恢复内置。
- 实机停止续租：超时后隐藏测试HWND并恢复内置。
- 实机测试进程直接退出：约250ms检查周期恢复内置；占位仍reserved，错误码0。
- 扫描发现 `antigravity.lumibar-glass`；没有自动运行其前端。

Antigravity 下一步：读取正常端点 → 主/事件/频谱通道握手 → 建立两个隐藏胶囊 HWND → register → 开独立心跳 → 使用真实 slots 布局 → activate → 显示胶囊。详情/歌词/悬停的展开方式和尺寸仍由前端完全控制。最后验证托盘变宽、双 Lite、播放器关闭、前端闪退及后端重启。

## 8. 媒体封面异步更新（2026-11-15）

歌名、歌手和播放状态优先下发，封面读取与取色随后完成。初次收到一首歌时，`media.hasArtwork` 可能为 `false`；封面就绪后会再次发出 `media` 变化通知。同一个 `key` 也可能更新封面状态与颜色。

前端收到 `media` 通知后应重新读取状态；当 `hasArtwork` 从 `false` 变为 `true` 时，调用 `media.artwork`，再更新封面与双色渐变。不要仅凭歌曲 `key` 没变就跳过这次更新。切歌后应丢弃旧歌曲尚未完成的封面响应，避免封面串歌。
