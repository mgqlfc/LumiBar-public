# 天气新增字段：气压与紫外线

实机版 v33 起，`state.get -> weather.forecast` 新增以下可选字段，原协议版本保持为 1。

| 字段 | 类型 | 含义 |
| --- | --- | --- |
| `pressureHpa` | `number \| null` | 海平面气压，单位 hPa。Open-Meteo 使用 `current.pressure_msl`，MET Norway 备用源使用 `instant.details.air_pressure_at_sea_level`。 |
| `uvIndex` | `number \| null` | 当前紫外线指数，无量纲，显示标记 UVI。来自 Open-Meteo Air Quality 的 `current.uv_index`。0 是有效值，夜间也正常显示。 |

原有 `humidity` 为相对湿度，单位 %；`wind` 仍以 km/h 输出，前端若选择 m/s，自行除以 3.6。`windDirection` 仍为风向角度。

新字段缺失或为 null 时显示“—”。旧缓存不会补成 0；天气刷新成功后会带上新字段。当前紫外线指数与每日最大紫外线指数不同，请保持“当前”口径。

内置天气详情底部顺序：湿度、风速、空气、气压、紫外线。标题示例为“湿度 · 相对”“风速 · km/h”“空气 · US AQI”“气压 · hPa”“紫外线 · UVI”，单位和口径放标题后面。
