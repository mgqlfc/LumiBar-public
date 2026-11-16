# LumiBar 0.7.44 强调色

设置页位置：外观 → 主题下方 → 强调色。默认保留冰糖蓝。选择后保存到 `Preferences.AccentColor`，切换明暗主题不会重置。

| ID | 名称 | 渐变三色，顺序为 0 / 0.46 / 1 |
| --- | --- | --- |
| blue | 冰糖蓝 | #6CE7DA / #21BEC2 / #118FB1 |
| mint | 薄荷绿 | #BAF0D6 / #6BCDAA / #399F8C |
| lime | 青柠糖 | #E0EEB3 / #B4CF82 / #7DA56C |
| butter | 奶油黄 | #FFF1B6 / #F0D47A / #D0B365 |
| apricot | 奶杏橙 | #FFE0BB / #F2BA91 / #D99579 |
| peach | 蜜桃粉 | #FFD3DF / #F0ACC9 / #D48EB6 |
| berry | 莓果红 | #FFD0CE / #EF9EA9 / #D17F9C |
| lavender | 薰衣紫 | #E5D5FF / #C6ACEF / #A58BDA |
| periwinkle | 绣球蓝 | #D2E3FF / #A9C4F3 / #86A5DE |

协议保持 v1，新增兼容字段：`state.get.data.settings.accentColor`、`appearance.accentColor`、`appearance.accentPalette`。后者包含 id、name、colors（三个 #RRGGBB 字符串）、first/middle/last（RGB 整数）、lightInk/darkInk（适合对应主题的小字与焦点色）。

更改：`settings.patch` 参数 `{"accentColor":"butter"}`。未知 ID 返回 `invalid_params`；大小写与首尾空白会标准化。事件流会通知 `appearance` / `settings`，不增加额外管道或改变任务栏租约。

建议颜色过渡 220ms，EaseOut；关闭动画时直接切换。强调色控制一般按钮、选中态、滑条、开关、降水图等；媒体封面取色继续读取 `media.accent` / `media.secondaryAccent`。天气预警级别、冷暖温度条和天气氛围保持各自语义色。
