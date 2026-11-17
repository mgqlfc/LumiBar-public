# LumiBar 0.7.56 验证记录

2026-11-17。处理开始菜单、搜索打开时的胶囊遮挡，并统一 Shell 浮层导致测量中断时的布局连续性。

## 原因与修正

实机反馈把问题分成了两条链路：测量失效曾触发 Lite 和上移；0.7.55 保持尺寸位置后，开始菜单和搜索仍会让任务栏盖住胶囊。只验证 slots 坐标会漏掉第二种情况。

修复前的真实窗口记录显示：任务栏进入 band 6，音乐和天气 HWND 仍在 band 1。原生 XAML 订阅在数百次回调后返回 0x8000ffff，旧逻辑关闭驻留窗口，同时撤销层级维护。

- v6 原生组件遇到该订阅错误后保留窗口、心跳和层级事件钩子，报告 layer-only，继续同步任务栏 band 和同层 Z 序。
- 层级正常和空间预留兼容状态分开；layer-only 不声明占位成功，也不授予外部渲染器原生槽位租约。
- DLL 文件名、消息窗口类、CLSID 与重连消息标识一并升级。内容寻址缓存避免覆盖 Explorer 已加载的 DLL。
- 同一 Explorer、DPI 和任务栏水平尺寸下的 UIA 测量中断统一沿用已验证边界，移除依赖前台进程名称的判断；真实托盘增长继续缩减容量。
- 把 ReservationSnapshot 移到可直接测试的后端目录，检查有效租约和 layer-only 的区别。新增可选原生诊断字段并更新插件交接。

## 已完成验证

- 修复前先跑出测量中断场景的失败用例，修复后后端回归 350/350。
- v6 MSVC 编译、.NET 自包含 Release 发布、Inno Setup 安装包编译通过。
- 独立目录安装验收 21/21：验证 v6 DLL 哈希、WinUI 设置页启动、诊断导出、全量安装默认值、升级保留设置、卸载保留数据、开始菜单快捷方式保持。证据：work/installer-qa-20261117-205643/results.json。测试完成后实机 PID 57872、Explorer PID 15212 保持。
- 实机连续 140 秒，1,277 次窗口层采样全部匹配：820 次 1/1/1、457 次 6/6/6（任务栏/音乐/天气）。覆盖开始菜单和搜索打开、收起。
- 同轮 1,464 次布局采样全部 full，937 次 measured、527 次 unavailable；胶囊原生窗口 top 始终 1517，宽度分别 429/207 物理像素（含透明边距）。层级服务始终 layer-only、layerError=0，没有断连重载；记录到 2 次同层 Z 序修复。
- 用户设置文件哈希保持，偏好与强调色核对通过，实机 DLL 匹配发布包。Explorer PID 始终 15212；没有重启 Explorer。
- 静置 8 秒抽样中，LumiBar CPU 占整机约 1.02%，Explorer 约 0.45%；这是这段采样的结果。

原始记录保留在忽略的 work/v55-inspection 目录：bands-start-before.jsonl、bands-after-v56.jsonl、layout-after-v56.jsonl、v56-summary.json、v56-cpu.json、backend-v56-tests.log、release-v56-build.log。实机激活核对：work/v56-live-verification.json。

## 当前状态

本机 XAML 空间预留仍返回 0x8000ffff，层级维护独立恢复。外部前端接管继续要求真实 reserved 租约。用户按原来的鼠标方式复测开始菜单和搜索，确认“现在都正常”。

安装包：outputs/安装包/LumiBar-0.7.56-windows-x64-setup.exe。SHA-256：15359ca7632c636da8449cba5a46645d8d53c8ecddc2774a771165c3fd2be001。
