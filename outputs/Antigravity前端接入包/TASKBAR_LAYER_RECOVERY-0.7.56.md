# 0.7.56：任务栏层级跟随与空间预留分开维护

2026-11-17。IPC 协议仍为 v1，新增状态和可选诊断字段。

## 本次实机问题

开始菜单、搜索打开后，任务栏由普通桌面层 1 切到层 6。旧版原生组件遇到 `AdviseVisualTreeChange` 返回 `0x8000ffff` 就退出，胶囊仍在层 1；即便尺寸和坐标正确，任务栏也会盖住它们。0.7.56 保留驻留组件的层级钩子和租约心跳，XAML 订阅报错后进入 `layer-only`，继续跟随任务栏层级；应用退出仍释放租约、恢复窗口层级。

组件文件、消息窗口类和 CLSID 都升级为 v6。Explorer 已加载的旧文件保持原样，新版使用内容寻址的独立 DLL 路径，升级无需重启 Explorer。

## 插件要如何判断

- `windows.hints.nativeReservation.status`、`windows.reservation.snapshot.status` 可能返回 `layer-only`。
- 此状态的 `slot` 为 null、`widthDip` 为 0；`error` 保留 XAML 连接错误。`layerError=0` 表示本轮层级跟随没有报错，`taskbarBand` 表示真实任务栏层；`layerRepairs` 累计同层 Z 序修复次数。
- `layer-only` 不授予原生空间租约。`taskbarReservation` / `builtinTaskbarReservation` / `reservationSupported` 继续返回 false；外部前端激活仍需真实 `reserved` 响应和有效槽位，不能按内置胶囊可见推断允许接管。
- 新增可选诊断字段 `callbackCount`、`candidateCount`、`discoveryError`、`containerKind`，仅供排查。旧客户端可以忽略。

同一 Explorer、DPI 和任务栏水平尺寸下，UIA 测量短暂不可用时，内置布局沿用已验证边界。任务栏和 DPI 改变、Explorer 更换会使历史测量失效；真实托盘增长和应用图标占满仍会触发正常 Lite 规划。`geometryStatus` 如实保留 unavailable。

## 前端仍需遵循

按真实 `slot.rect` 渲染，DIP/物理像素只转换一次。由宿主维护任务栏层级；前端不用另建 Explorer 注入组件，也不用加周期性抢焦点或置顶循环。大小位置正确但显示变淡、沉到任务栏下面时，同时记录 band 和 Z 序；只看坐标会漏掉本次问题。
