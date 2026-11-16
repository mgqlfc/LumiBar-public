# 对 FRONTEND_REQUESTS.md 的后端回复

2026-11-14，后端 `0.4-dev`；协议仍为 v1，旧客户端继续可用。

请更新 DTO / Bridge，前端源码仍由 Antigravity 负责。本文件同时放在前端工程根目录，便于双方跟踪。

## 1. 任务栏：几何与槽位规划已开放，系统预留 / HWND 接管继续开发

`windows.hints` 现在包含：

```json
{
  "coordinateSpace": "physical-screen-pixels",
  "geometryStatus": "measured",
  "measuredAt": "2026-11-14T09:00:00Z",
  "blankZoneRect": {"left": 800,"top":1522,"right":1900,"bottom":1600,"visible":true},
  "musicSlot": {"rect":{"left":1300,"top":1540,"right":1650,"bottom":1590},"mode":"full"},
  "weatherSlot": {"rect":{"left":1670,"top":1540,"right":1880,"bottom":1590},"mode":"full"},
  "trayWidthPx": 660,
  "layoutState": "full",
  "layoutRecommendation": {"musicWidth":350,"weatherWidth":210,"lite":false,"requiredWidth":620,"requiresReservation":false},
  "reservationSupported": false
}
```

上例仅展示结构。所有正式坐标以运行时返回为准；字段 `rect` 是嵌套对象，`mode` 与它同级。

如需自定义期望尺寸，调用只读接口 `windows.planCapsules`，返回结构与 `windows.hints` 相同：

```json
{"musicPreferredDip":300,"musicMinDip":64,"weatherPreferredDip":120,"weatherMinDip":72,"heightDip":34,"wasLite":false}
```

- 参数单位 DIP，结果单位物理像素。音乐宽度范围 32–600，天气 32–400，高度 24–64；最小宽度不超过期望宽度。
- `wasLite` 传入上一次布局状态，恢复完整模式时会保留额外 16 DIP 缓冲。默认 `windows.hints` 的布局状态由后端维护。
- `layoutState` 为 `full / lite / hidden / unavailable / reservation-required`。
- 空间足够时返回槽位；音乐无会话或模块关闭时，槽位 `mode:"hidden", rect:null`。
- 空间不足以容纳双 Lite 时，返回 `reservation-required`，两个 slot 均为 null，`layoutRecommendation` 保留双 Lite 所需宽度。不要拿旧槽位盖住应用按钮，也不要用隐藏两颗胶囊来实现用户的拥挤策略。
- 测量失效时，`blankZoneRect` 和 `layoutRecommendation` 为 null。任务栏自动隐藏 / 全屏隐藏时，槽位为 hidden。
- `taskbarReservation:false`、`rendererReplacement:false` 保持不变。`windows.registerCapsules` 和实际 HWND 调度还未开放，当前可以在独立预览宿主中展示后端规划。

Windows 29704 的旧 `MSTaskListWClass` 矩形比实际应用按钮区域短。本轮已改用 `Shell_TrayWnd` 下实际 XAML 按钮边界，并缓存后台 UIA 测量，避免阻塞 WinUI 主线程。

## 2. 随机与循环控制已开放

`state.get -> media` 新增：

```json
{"shuffleEnabled":false,"repeatMode":"None","controls":{"toggle":true,"previous":true,"next":true,"seek":true,"shuffle":true,"repeat":true}}
```

状态可为 null；能力位由播放器通过 GSMTC 提供。对应方法：

- `media.setShuffle {"enabled":true}`
- `media.setRepeat {"mode":"None"}`，也支持 `Track`、`List`。

先检查能力，再发命令；播放器拒绝时显示错误，别仅在 UI 内切换状态。能力开关和新字段已接入 Windows API；本轮没有改动用户播放器的随机 / 循环偏好，实际操作需要针对各播放器继续验收。

“喜欢”没有统一 GSMTC 接口，继续隐藏 / 禁用，后续通过播放器适配器处理。

## 3. 一小时降水已开放，字段采用区间降水量

读取 `state.weather.precipitationNextHour`，不是 `forecast.minutely`：

```json
{
  "windowStart":"2026-11-14T17:07:00+08:00",
  "windowEnd":"2026-11-14T18:07:00+08:00",
  "source":"Open-Meteo",
  "kind":"model-trend",
  "intervalMinutes":15,
  "precisionNote":"15 分钟模式趋势；奎文区等地区由小时预报插值",
  "complete":true,
  "intervals":[{"start":"2026-11-14T17:00:00+08:00","end":"2026-11-14T17:15:00+08:00","amountMm":0.2}]
}
```

上例省略其余区间。后端通常返回 4–5 个与未来 60 分钟窗口相交的区间；窗口每次读快照时随当前时间推进。

请修改 `LumiBarBridgeService.GetOneHourPrecipitationSlots()`：从此字段生成控件数据。将控件主值改为区间降水量 mm，保留真实时间标记。这个来源没有给出每个 15 分钟区间的降水概率，不要从降水量推算 Probability；也不要把 mm 直接当 mm/h。若需要平均强度，可计算 `amountMm / 0.25`，并明确标“区间平均强度 mm/h”，数据精度仍为 15 分钟模型预报。

`amountMm:null` 显示缺口；`complete:false` 提示部分数据缺失；整体为 null 时保留空态。奎文区标为模型插值趋势；当前不是雷达临近预报。原来的示例模式继续标“示例数据”。

## 4. 日出日落与可靠日夜标记已开放

`forecast.sunrise`、`forecast.sunset` 为当天城市当地时间 ISO 字符串，可为 null。

`forecast.isDay:boolean|null` 是当前日夜标记；`forecast.hours[].isDay` 是对应小时标记。请优先用它们选日夜图标，停止使用固定 06:00–18:00 规则。极昼 / 极夜可能没有日出日落时间，但 `isDay` 仍可用。备用源缺失时保持 null，勿用 0 点填补。

## 5. 变化通知已开放：独立连接长轮询

当前选择 `events.wait` 长轮询，保留每请求一响应的 v1 帧格式，不混入旧 SDK 无法识别的无请求推送。

`hello` 新增 `instanceId`，以及能力 `eventsLongPoll:true, eventPush:false`；`state.get` 新增 `stateVersion`、`instanceId`。

流程：

1. 先读取完整状态，记下 `stateVersion` 和 `instanceId`。
2. 使用**单独的第三条连接**，发送 `events.wait {"afterVersion":42,"timeoutMs":25000}`。
3. 收到 `{instanceId,version,topics,resyncRequired,timedOut}`；topics 可含 `media / weather / lyrics / settings / appearance / windows`。
4. 有相关变化时刷新 `state.get`，windows 主题变化刷新 `windows.hints`，然后继续等待。
5. `timedOut:true` 为正常心跳。`resyncRequired:true` 或 instanceId 改变时重新读取全部状态和几何信息。

最长等待 25 秒，最短 50 ms；保留最近 64 次变更，落后过多会要求重新同步。最多 4 条连接：建议状态 / 控制共一条，频谱一条，事件一条。

媒体服务已监听 GSMTC 的会话、属性、播放状态和进度变更事件，收到事件会提早读取；保留定时读取兜底。播放进度动画继续由前端时钟补间，频谱仍使用独立高频接口。空闲时不需要每秒拉取整个状态；定期心跳读取可用于校正位置和更新滚动降水窗口。

前端不需要修改现有 `LumiBarClient` 的消息读取器：把长轮询当作普通 `RequestAsync` 使用即可，且该连接等待期间不要复用来发送播放控制。

## 接入顺序

先更新 `Sdk/Contracts.cs`；再更新 `LumiBarBridgeService` 的降水映射、日夜图标、随机 / 循环动作和第三条事件连接。然后在预览宿主中采用后端槽位规划。实际嵌入与空间预留能力保持关闭，等待后端通知。

本批纯逻辑与协议测试 73 项通过。完整类型与示例见同目录 `contracts.d.ts`、`verify-live.mjs`；真实数据连通结果写入 `live-check.json`。
