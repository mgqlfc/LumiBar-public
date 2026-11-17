
# LumiBar 前端插件交接包 · 0.7.56

最新更新：2026-11-17。协议保持 v1。

- [0.7.56 任务栏层级恢复与 layer-only 状态](./TASKBAR_LAYER_RECOVERY-0.7.56.md)：内置胶囊的层级维护独立于 XAML 空间预留；插件接管仍要求真实 reserved 租约。旧交接中的 active 记录是当时的实测，排查当前显示状态时先读本次文档与实时接口。

- [插件自定义菜单](./PLUGIN_MENUS-0.7.46.md)：托盘菜单和设置左侧入口，含注册/动态更新、点击回调、确认与自动清理。附 [C# 示例](./PluginMenusExample.cs)。胶囊右键不加菜单。

- [设置同步与回写修复](./SETTINGS_LIFECYCLE_FIX-0.7.45.md)：切回内置时读取当前偏好，拦截隐藏/离页控件回调；前端回写要求与验证结果。

- **插件不显示优先看** [当前排查交接](./CODEX_CAPSULE_HANDOFF-0.7.44.md)：真实 active 租约 + slot.rect 无 visible，与前端 bool 默认值冲突；含 C# 复现结果。
- [胶囊实现踩坑笔记](./CAPSULE_IMPLEMENTATION_NOTES-0.7.44.md)：接口分工、层级、DPI、透明圆角、光效色带、玻璃刷新、显隐竞态与验收场景。

- [接入总览](./README-交给Antigravity.md)：分工、连接方式和现有接口。
- [九色强调色](./ACCENT_COLORS-0.7.44.md)：包括奶油黄；渐变色值、读取字段、修改请求和过渡规则。
- [正式渲染接管](./BACKEND_RENDERER-0.7.md)：注册、激活、续租、真实槽位与清单启动。
- [玻璃设置](./GLASS_SETTINGS_UPDATE-0.7.41.md)：更新密度时避免全量刷新和原生材质重建。
- [定位](./BACKEND_LOCATION-2026-11-15.md)、[字体与逐曲歌词](./BACKEND_LYRICS_FONTS-2026-11-15.md)、[和风天气](./BACKEND_QWEATHER-2026-11-15.md)。

`contracts.d.ts` 已补齐强调色类型，`mock-state.json` 已补齐示例三色数组。旧后端缺少 accentColor 时回退 blue；音乐继续使用 media.accent / secondaryAccent。

## 0.7 正式渲染接管

参阅 [BACKEND_RENDERER-0.7.md](./BACKEND_RENDERER-0.7.md)。正常后端已提供注册/确认接管/续租/释放、真实槽位事件及前端清单发现/启动。正式接入使用 `%LOCALAPPDATA%\LumiBar\frontend-endpoint.json`；独立预览端点不支持真实任务栏接管。

最新界面协作：[城市栏与音乐胶囊微调](./UI_POLISH-0.7.47.md)。播放控件按用户最终选择恢复细线。

[0.7.48 胶囊整体下移](./CAPSULE_ALIGNMENT-0.7.48.md)：槽位已包含 2 DIP 偏移，前端不要重复添加。

[0.7.49 Synthwave 底层支持](./BACKEND_SYNTHWAVE_SUPPORT-0.7.49.md)：Win2D、离线编译、40Hz 频谱及 v6 配置。
