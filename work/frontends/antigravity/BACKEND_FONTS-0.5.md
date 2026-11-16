# 0.5-dev：系统字体选择与音频稳定性

用户新增要求：自定义字体直接读取电脑已安装字体。请在 Antigravity 的外观设置中增加字体选择，不维护静态字体名单。

## 字体接口

能力：`hello.capabilities.systemFonts === true`。

```json
{"id":"fonts-1","method":"fonts.list","params":{"query":"微软雅黑","offset":0,"limit":100,"refresh":false}}
```

返回：

```json
{"source":"windows-directwrite","installedCount":313,"total":1,"offset":0,"hasMore":false,"items":[{"family":"Microsoft YaHei","displayName":"微软雅黑","aliases":["Microsoft YaHei","微软雅黑"]}]}
```

示例数量以实际返回为准。后端使用 DirectWrite 系统字体集合读取真实字体，包含本机可见的用户安装字体；不会扫描任意目录或下载字体。`family` 为保存和应用的规范名称；`displayName` 用于列表；`aliases` 支持中文和英文检索。

- query 可省略或为空，大小写不敏感；最长 128 字符。
- offset 为从 0 开始的分页偏移；limit 默认 100，范围 1–500。
- refresh=true 重新读取系统字体，供新装字体后刷新；搜索时使用缓存，避免每次敲字重扫。
- 后端实测本机 313 个字体家族，其中 77 个有不同的本地化显示名。

保存选择（2026-11-15 扩展）：

```json
{"id":"font-set","method":"settings.patch","params":{"chineseFontFamily":"Microsoft YaHei","westernFontFamily":"Segoe UI"}}
```

恢复对应文字的系统默认：`settings.patch {"chineseFontFamily":null}` 或 `settings.patch {"westernFontFamily":null}`。

`state.settings` 与 `state.appearance` 都返回 `chineseFontFamily`、`westernFontFamily`。后端验证三个字体字段的值，接受规范名或本地化别名，保存规范名。任一未知字体返回 `font_not_found`，整次 patch 保持原设置；错误类型使用 `invalid_params`。所有字体解析完成后，后端才合并最新设置，避免等待枚举期间覆盖其他设置修改。

旧 `fontFamily` 保留写入兼容：只提交该字段会同时更新中、西文字体，null 同时恢复系统默认。若同次请求带新字段，新字段优先，例如 `{"fontFamily":"Microsoft YaHei","westernFontFamily":null}` 会把中文设成微软雅黑，西文恢复系统默认。每个提交的非 null 字体仍须通过校验。

新前端读取每种字体时先检查 `appearance` 是否包含对应字段，再检查 `settings`；字段存在且为 null 就使用系统默认。仅在两个对象都缺少该新字段时，才回退旧 `fontFamily`。不要用 `newField ?? fontFamily`，否则用户显式恢复默认后会重新用上旧字体。旧 `fontFamily` 记录兼容入口的值，双字体各自更新后应读取新字段。

## 前端交互

1. 分别提供中文、西文字体选择，每项都有“使用系统默认”与可搜索的系统字体列表。
2. 列表选择字体后显示预览，例如“微光 · 奎文区 12° / Aa 0123456789”。允许按自己的交互方案先预览再应用。
3. 应用到歌词、天气、胶囊文字和设置文字；FontIcon / SymbolIcon 继续使用图标字体，避免变成方框。
4. 中西文混排文字按文字片段应用对应字体；中文字体通常也包含拉丁字形，单个 FontFamily 回退列表无法保证两项选择独立生效。不要覆盖图标专用 FontFamily。
5. 新装字体通过刷新按钮读取；所选字体卸载后，显示系统回退字体并提示用户重新选择。
6. `settings` 变化通知会提醒其他窗口更新字体。无需修改每个窗口的弹出方式或尺寸。

内置设置页提供中文、西文字体搜索与独立恢复默认。Antigravity 需自行接入双字体布局和混排渲染；本次仅同步接口文档，未改前端源码。此次后端源码修改等待统一构建与接口回归，下面的 87 项结果属于原 0.5-dev 记录。

## 同版音频修复

- 每个采集会话使用独立 FFT 缓冲，设备替换不会混入上一段采样。
- 旧 DataAvailable / RecordingStopped 回调不能覆盖新会话状态。
- 实际采集器绑定刚探测到的输出设备，并持有设备到采集结束。
- 新增 24 位 PCM、8 位 PCM、64 位浮点解析，过滤 NaN / Infinity。
- 频谱继续输出 20 段 0–1 数值；暂停 / 停止归零，300 ms 无有效采样后归零。

87 项回归测试通过；DirectWrite 真实枚举通过；WASAPI 实际启动 / 停止连续三轮正常。尚未用实体耳机完成跨设备热插拔验收。
