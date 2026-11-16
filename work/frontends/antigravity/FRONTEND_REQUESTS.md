# LumiBar 后端接口需求清单 (FRONTEND_REQUESTS)

面向后端协议 `LumiBar Frontend API v1` 的增量扩展建议（均保持向后兼容，不破坏现有 v1 客户端）：

## 1. 任务栏空间预留与正式胶囊调度 (`taskbarReservation`)
- **需求背景**：目前 `windows.hints` 仅提供 `taskbar` 外框和内置占位锚点，`reservationSupported: false`。为了避免前后端各自跑一套 `Shell_TrayWnd` 定位器造成冲突，需要后端统一提供空白区测量与布局决策。
- **建议新增接口 / 字段**：
  - `windows.registerCapsules`: 前端注册自己管理的胶囊 HWND 或期望尺寸（`musicPreferredDip`, `musicMinDip`, `weatherPreferredDip`, `weatherMinDip`）。
  - `windows.hints` 补充或推送事件 `windows.layoutChanged`：
    - `blankZoneRect`: `{ left, top, right, bottom }`（物理像素）
    - `musicSlot`: `{ left, top, right, bottom, mode: "full" | "lite" | "hidden" }`
    - `weatherSlot`: `{ left, top, right, bottom, mode: "full" | "lite" | "hidden" }`
    - `trayWidthPx`: number

## 2. 扩展媒体播放控制 (`media.controls` 增强)
- **需求背景**：歌词/播放浮窗目前受限于 v1 的 `{ toggle, previous, next, seek }`，随机播放、循环模式和喜欢状态暂不能直接操控播放器。
- **建议新增字段（`state.get -> media`）**：
  - `shuffleEnabled`: `boolean | null`
  - `repeatMode`: `"None" | "Track" | "List" | null`
  - `controls.shuffle`: `boolean`
  - `controls.repeat`: `boolean`
- **建议新增 RPC 方法**：
  - `media.setShuffle`: `{ enabled: boolean }`
  - `media.setRepeat`: `{ mode: "None" | "Track" | "List" }`

## 3. 未来 1–2 小时短时降水数据 (`forecast.minutely`)
- **需求背景**：前端已独立实现 `PrecipitationTrendControl`，目前真实后端下仅展示“短时降水暂不可用”。
- **建议新增字段（`state.get -> weather.forecast`）**：
  - `minutely`: `Array<{ time: string; probability: number; mmPerHour: number | null }>`（例如 4~8 个 15 分钟步长点）
  - `minutelySourceType`: `"radar" | "hourly-interpolated"`（便于前端诚实标注数据来源类型，不误导为雷达实况）

## 4. 天气日出日落时间 (`forecast.sunrise` / `forecast.sunset`)
- **需求背景**：WMO 天气码不区分日夜，前端目前按城市当地时间 06:00–18:00 估算晴夜图标，跨季节和高纬度地区存在误差。
- **建议新增字段（`state.get -> weather.forecast`）**：
  - `sunrise`: `string`（城市当地时间 ISO）
  - `sunset`: `string`（城市当地时间 ISO）

## 5. 事件推送 / 订阅 (`subscribe`)
- **需求背景**：v1 目前通过 1~3 秒轮询 `state.get`。如果后续支持在命名管道上推送 `state.changed` / `media.changed` 通知，前端可以进一步降低空闲 CPU 唤醒并实现切歌零延迟联动。
