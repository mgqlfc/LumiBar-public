# 外观接口增量 · 2026-11-15

当前 0.7.44 新增九色强调色，见 [ACCENT_COLORS-0.7.44.md](./ACCENT_COLORS-0.7.44.md)。当前默认封面饱和度为 0.8；字体 null 使用内置苹方 / Geist。自动定位已接受 Windows 返回的来源，最新规则见 [BACKEND_LOCATION-2026-11-15.md](./BACKEND_LOCATION-2026-11-15.md)。以下保留早期接口演进记录。

协议继续使用 Frontend API v1。下文最初对应 v23 后端源码；当前接入新增字段请使用最新后端。

| 字段 | 读取位置 | 修改入口 | 含义与兼容 |
| --- | --- | --- | --- |
| `chineseFontFamily` | `state.appearance`、`state.settings` | `settings.patch` | 已安装字体规范名或 null，中文字体 |
| `westernFontFamily` | `state.appearance`、`state.settings` | `settings.patch` | 已安装字体规范名或 null，西文字体 |
| `fontFamily` | `state.appearance`、`state.settings` | `settings.patch`，兼容入口 | 同时设置双字体；同次请求的新字段优先 |
| `coverSaturation` | `state.appearance`、`state.settings` | `settings.patch` | 0–1，默认 0.35；调整封面着色饱和度 |
| `secondaryAccent` | `state.media` | 只读 | 第二封面色，`#RRGGBB` 或 null；缺失/null 时沿用 `media.accent` |

`coverSaturation` 与 `glow` 各管一项：前者控制色彩饱和度，后者控制着色浓度。保持 `coverGlow` 总开关和 `animations` 动效开关的现有含义。旧后端没有 `coverSaturation` 时，前端先用 0.35 展示；若 patch 返回 `invalid_params`，提示该后端版本暂不支持这项设置。

所有提交的非 null 字体都须通过系统字体解析；中文别名会转成规范名。任一字段解析失败时，整次 patch 不保存。null 只恢复指定脚本的系统默认，字段缺失才沿用现有设置。详见 [BACKEND_FONTS-0.5.md](./BACKEND_FONTS-0.5.md)。

示例（字体须已安装）：

```json
{"id":"appearance-1","method":"settings.patch","params":{"chineseFontFamily":"Microsoft YaHei","westernFontFamily":"Segoe UI","coverSaturation":0.35}}
```

```json
{"id":"legacy-1","method":"settings.patch","params":{"fontFamily":"Microsoft YaHei","westernFontFamily":null}}
```

第二个请求把中文设成微软雅黑、西文恢复默认。新前端按字段是否存在决定旧版回退；显式 null 不应回退旧 `fontFamily`。

契约以同目录 `contracts.d.ts` 为准。接入时更新前端 DTO、设置页与混排渲染；本次交接仅提供文档和 TypeScript 契约副本，前端现有 C# DTO 保持原样。

## GPS 状态增量

- `state.weather.locationBusy?: boolean`、`locationStatus?: string`：提供定位忙碌和提示文字，缺字段按 false / 空字符串兼容。
- `state.settings.autoLocateCity?: boolean`：只读状态，不接受 `settings.patch`。
- `cities.locateGps {}` 开启自动定位，立即请求并在之后每 15 分钟检查；成功返回 City。失败或旧请求遭新选择取代时返回 `location_unavailable`。
- UI/API 共用定位请求。手动 `cities.select`、关闭自动定位或发起新定位会取消旧请求，旧结果不会覆盖新选择。定位只接受卫星来源，反向地理编码仅为卫星坐标查询地名。
- `cities.select` 参数仍是顶层 `{name, latitude, longitude, region?, timeZone?}`，没有外层 `city` 对象。

## 2026-11-15 v27 光色行为补充

- `media.accent` / `secondaryAccent` 继续使用相同字段，新的主程序优先提取亮彩区域，排除黑灰；无彩色封面使用相近蓝紫双色，中暗彩色保留色相后提亮。
- `appearance.glow` 的范围不变（0.08–0.55），内置前端现在按比例控制整层光效强度，避免固定亮度淹没滑条差异。
- 内置浅色背景使用明亮浅色光，进度条/拖动圆点使用同组封面颜色的清晰渐变。透明玻璃的阴影仅画外沿，避免内部黑影污染颜色。
- 独立后端进程本轮未重启；新取色规则需使用更新后的后端构建。
