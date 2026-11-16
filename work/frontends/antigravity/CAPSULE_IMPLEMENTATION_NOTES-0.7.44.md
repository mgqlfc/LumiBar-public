# LumiBar 胶囊实现与踩坑交接 · 0.7.44

2026-11-15，Codex → Antigravity。先读同目录 CODEX_CAPSULE_HANDOFF-0.7.44.md：本次已抓到 slot.rect.visible 的显示判定错误。本文整理内置 WinUI 3 胶囊的实际实现经验、接口分工和验收方式。

## 1. 接口覆盖到哪里

目前进程外前端接口已具备替代内置双胶囊的核心能力。前端可以做到相同的功能与交互；原生控件、亚克力遮罩和动画实现由前端负责，接口提供数据和任务栏协调。

| 项目 | 后端负责 / 已提供 | 外部前端负责 |
| --- | --- | --- |
| 任务栏空间 | 原生预留、托盘宽度探测、真实槽位、双 Lite 决策、拥挤恢复与窗口层级修复 | 用分配的 rect 放置自身 HWND；实现平滑移动和 Full/Lite 布局 |
| 渲染接管 | 注册→激活→续租→释放、隐藏内置胶囊、崩溃/超时后回退 | 两个顶层 HWND、心跳、独立超时隐藏和窗口生命周期 |
| 天气 | 当前/体感温度、湿度、风速风向、气压、UV、AQI、预警、逐时/多日、短时降水、城市与 Windows 定位 | 图标、曲线、缺数态、预警详情和所有弹窗/动效 |
| 音乐 | 系统媒体会话、曲名/歌手/专辑/播放器、控制能力、进度、封面与双色 | 标准/Lite、跑马灯、控件显隐、封面过渡和详情排布 |
| 频谱 | 系统输出音频 20 段 bands，独立连接获取 | 平滑衰减、暂停回落、内容区背景融合、裁剪和降帧策略 |
| 歌词 | LRC/文本、同步时间、当前歌曲独立偏移、导入/刷新 | 高亮定位、滚动、上下渐隐、换行、±0.5s 按钮 |
| 主题/设置 | 明暗偏好、任务栏主题、九色强调色、双字体、浓度/饱和度、设置保存 | 主题应用、字体解析及显示、材质、选中/悬停/禁用态 |

保留的边界：第三方自定义菜单项注册尚未提供；扫描到前端清单不会自动切换渲染器，正式切换须 activate；外部前端的开机自动启动链尚未接成通用插件能力。现有接口没有“原地更新租约尺寸”命令，修改注册尺寸时先隐藏、release、按新尺寸重新注册。详情窗口的大小、展开方式、交互都由前端控制。

## 2. 从启动到显示：逐项可观察

1. 读取后端传入的 LUMIBAR_ENDPOINT_FILE；正式端点通常是 `%LOCALAPPDATA%\LumiBar\frontend-endpoint.json`。独立开发预览端点只能预览。
2. 每条连接独立 hello；正式端点 mode=builtin，rendererReplacement=true、rendererLease=true。读取当时 capabilities，不能凭进程运行就跳过握手。
3. 创建两个属于当前管道客户端 PID 的顶层 HWND，注册时都保持隐藏；详情/悬停 HWND 不注册。保存 Window 强引用。
4. registerRenderer 返回 pending，内置仍显示；立即启动续租、准备真实槽位布局与首帧。
5. activate 返回 phase=active 且 renderingOwner=external 后才显示。reservation_not_ready 先续租，约 200–500ms 后重试。
6. 对每个槽位检查整体 state、mode、rect 非空、正宽高与功能开关，再在 UI 线程定位/显示。slot.rect 只有四个边，没有 visible；内置 anchor.visible=false 在接管后很正常。
7. 依次记录：租约阶段、slotsVersion、slot.mode/rect、每个隐藏分支的原因、最终 HWND/AppWindow 可见性、Root Loaded、客户区和 Root 实际宽高。不记录 token、完整管道消息、歌词或城市坐标。

推荐显示判定的基本结构（还需合并租约和功能开关）：

```csharp
var mode = slot?.Mode;
var rect = slot?.Rect;
bool canDraw = allowShow && featureEnabled
    && (overallState is "full" or "lite")
    && (mode is "full" or "lite")
    && rect != null && rect.Right > rect.Left && rect.Bottom > rect.Top;
// 这里不要检查 rect.Visible。
```

state=reservation-required/unavailable/hidden、mode=hidden、rect=null 都隐藏。普通网络状态更新不能绕过这一判定。后端会隐藏失效/不可见槽位对应的外部 HWND，但不会替前端显示和排版。

## 3. 管道和租约：不要互相堵塞

State、Events、Spectrum 使用各自长期连接；推荐第四条独立 Control 连接负责租约。每条连接请求/响应串行。events.wait 可挂起约 25s，续租绝不能排在它后面。租约 5000ms，建议每 1000ms 续租；用 Stopwatch 等单调时钟及 expiresInMs 实现前端自己的超时隐藏。

字体列表、封面解码、网络等待和 UI Dispatcher 不能堵住心跳。Task.Run 启动后台循环，避免 SDK 无意捕获 UI SynchronizationContext；UI 更新 TryEnqueue。后端重启、instanceId 改变、租约失效时，隐藏所有自管窗口并重新注册。后端只知道已注册的两个胶囊；详情/悬停由前端一起收起。

不要把 lastReleaseReason 当作新租约当前失败状态的唯一依据，它是“上次释放原因”；同时核对当前 owner、leaseId、phase 和 instanceId。不同来源的槽位更新先按 instanceId 和 slotsVersion 排序，丢弃旧响应。

## 4. 坐标、DPI 与真实空间

- slots.rect 是物理屏幕像素，可能为负。AppWindow.MoveAndResize / SetWindowPos 直接使用这些值，不能再乘 DPI。XAML 内部宽高才除以当前窗口 DPI/96。
- 先定位到目标显示器，再取窗口 DPI。不要固定 1.0/1.5，也不要把 taskbar.scale 与 RasterizationScale 重复套用。
- rect 是内容轮廓。额外透明外沿/阴影留白由前端按比例扩展 HWND，并把内容移回 slot 的位置；透明边不能挡住别的图标。不要把阴影当预留的实心宽度。
- 全部 Full/Lite 决策听后端槽位。自己再跑一套 Shell_TrayWnd/UIA 空白区检测会相互打架。slots 不可用时不要沿用旧屏幕坐标。
- 内容宽度需真实测量：例如 −20°、100% 降水、较宽自定义字体。天气图标、数字和降水组不应硬挤进固定宽度；注册 full/min 尺寸须覆盖目标排布。
- 长歌名在稳定的视口内跑马灯，左右 alpha 渐隐；悬停控件通过视口宽度/遮罩柔和让位，避免修改外部窗口宽度推动天气胶囊。

## 5. 任务栏层级、开始菜单、显示桌面、预览和自动隐藏

仅设置 TOPMOST 不够：任务栏自己进入同一 topmost band 时，胶囊仍可能排在任务栏下面。内置修复会比较实际 Z 序，只在胶囊低于任务栏时修复相对位置，使用 NOACTIVATE，保留开始菜单/托盘弹窗正常覆盖关系。不要每个 tick 激活或无条件抢到所有窗口最上面。

本后端在 ExternalRendererActive 时也把已注册外部 HWND 交给预留组件，并对它们调用 RestoreAboveTaskbar。外部前端负责自己的显示/尺寸，不要再注入第二份占位组件或恢复一个陈旧 HWND。

曾经复现：先打开开始菜单，再点显示桌面；单独反复测试其中一个抓不到。还要测试托盘展开/收起与任务栏程序缩略图。Explorer 的 XamlExplorerHostIslandWindow 程序预览不是真全屏；误判会主动把胶囊藏掉。原生端已在 FullscreenPolicy 处理。

自动隐藏期间 Y 坐标会连续变化，不能把这段瞬时测量当作拥挤、降级 Lite 或抬到任务栏上方的理由。后端连续性策略负责稳定槽位；前端服从最终 slots。垂直方向直接跟随原生滑动，横向变宽可平滑；重复相同坐标不要继续 SetWindowPos。

SetWindowPos 可能同步触发另一轮 WinEvent 回调，必须有层级修复重入保护。过去重入会拖慢鼠标、资源管理器甚至进程退出；不要在回调中递归修复窗口。

## 6. 圆角透明、黑角、白边和横向色带

几何要统一：外部 HWND、原生透明底色、亚克力遮罩、内容裁剪、光效层、频谱层、细边与阴影必须依据同一个内容圆角和 DPI。只裁一个 Border，子级 Composition 视觉仍可能越界。Win32 region 与 WinUI 圆角不是完全相同的抗锯齿路径，直接圆角 region 可出现毛边；优先参考原生 GlassBackdrop 的合成遮罩，分别核对浅深主题和 Lite。

原生音乐曾出现横色带，关闭频谱后立即消失；两侧细竖线关闭封面光效后消失。修复需要约束各自绘制区域并统一剪裁，重启临时消失不能作为修复依据。切歌交叉渐变时，旧图、旧频谱与新背景都要遵守同一固定裁剪轮廓。

光效漂移只能移动纹理，不能移动裁剪轮廓。SoftLightTexture 在四周留 8 DIP overscan，最大漂移小于这个范围；否则暗色模式会露出没有光效覆盖的直边，让人误以为圆角反光遭到裁切。

无边框窗口还需关闭系统边框/标题栏与 DWM 边框色，检查客户区没有额外白边或顶部黑条。修改窗口样式后核对 SWP_FRAMECHANGED，不要每次收到媒体状态都重复刷 Chrome。

## 7. 亚克力、渐变台阶与颜色

原生亚克力图应常驻。玻璃密度滑动不要反复 Remove/Add target、重建 DesktopAcrylicController，或每条状态消息都重写 TintOpacity/LuminosityOpacity。原生最终做法是固定中性材质，用中性 density wash 的 opacity 表达密度；相同主题/值直接跳过。前端 UpdateTheme/UpdateAcrylicTint 应先比较缓存键，而非当成无条件刷新。

光效在后台只因颜色、尺寸、DPI 实变而栅格化；采用 double 平滑衰减与预乘 alpha，量化时加低幅确定性抖动，避免低透明度 8 位渐变的台阶。移动和交叉渐变交给 compositor，隐藏时停动画。大尺寸位图限幅、取消旧任务、给异步结果版本号；旧帧完成后不能覆盖新歌曲。

封面用两个相近亮彩色，排除纯黑/灰；黑白封面有柔和双色兜底。前端优先使用 media.accent/secondaryAccent。浅色页保留清洁的中性底，不叠一层厚灰。九色主题强调色与音乐封面色分开：通用选中/滑条用强调色，音乐按钮悬停、进度条与封面光效使用歌曲颜色。

WinRT 可为同一个原生 Brush 产生不同托管包装，弱引用名单会漏掉仍在屏幕上的画刷。原生强调色通过附加角色恢复原生画刷引用，并按原生依赖属性代次去重动画目标，避免同一个 Storyboard 对同一 Color 注册两遍。不要只按托管包装引用猜原生唯一性。

## 8. 显隐动画与异步竞态

内容透明度不会自动影响原生 SystemBackdrop 和系统阴影。只淡出 XAML 会留下框，先拆材质又会出现黑底。原生用整个 visual root（含自绘阴影）和亚克力遮罩同一时长淡出，完成后才 Hide HWND，再停背景漂移。当前详情退出约 180ms，悬停约 120ms；动画完成由 compositor batch 驱动，计时器只作必要兜底。

每次 Show/Hide 增加 revision。旧 Hide await、旧失焦回调、旧封面请求、旧歌词请求结束时，都必须核对 revision/trackKey/instanceId；否则点桌面关闭后立即重新点胶囊，会出现新窗口刚打开又让旧关闭任务藏掉。

只监听 Deactivated 抓不到“点击原本已经激活的其它窗口”。外点关闭还需可靠的全局点击/前景命中检查；排除胶囊、自己的菜单/子浮窗。点击胶囊关闭详情时，不能让外点处理先关闭，再让同一次 Click 重新打开，应在 PointerPressed 捕获切换意图。

悬停速览由胶囊和浮窗共同管理指针范围；移出短暂延迟，指向浮窗保持，Lite 音乐保留控件；标准音乐胶囊只显示自己的悬停控制，不再弹第二份速览。取消原生 Tooltip，避免双层提示。

ElementCompositionPreview.SetIsTranslationEnabled(element,true) 必须先于 StopAnimation("Translation") 和 InsertVector3。曾在“关闭动画”的路径上也因未创建 Translation 属性触发 E_INVALIDARG；这处会影响窗口初次显示。

## 9. 媒体响应与资源管理

曲名/歌手/状态先到，封面和取色后到。同一个 trackKey 会再次收到 artwork/颜色更新；不能只比较 key 决定是否刷新。异步封面结果核对 key+revision，迟到的旧封面直接丢弃。媒体控制命令不要等待重新取色/下载歌词才更新交互状态。

暂停保留音乐胶囊，会话关闭才退出。频谱来自系统输出，不是指定播放器独占，显示门控由播放状态决定。隐藏/暂停/禁用时停无意义绘制，空闲不用高速 UI 轮询。

同步控件数据时要有 isApplyingState / pointerEditing 标记。音量 Slider 初始化、设置页加载、媒体切换、读不到音量时的显示默认值，绝不能调用 SetMasterVolume；只有用户明确点击/拖动才提交。用户设备上 50% 很响，禁止启动回写默认 0.5。

字体中文/西文分别设置，null 使用内置苹方/Geist。长字符重新测量，不把硬编码字号和拉丁字宽用于中文。歌词 offset 只保存到当前曲目，提交时附当前 trackKey，避免晚到请求改到下一首。

## 10. 最小联合验收

| 场景 | 应看到的行为 |
| --- | --- |
| 注册 pending、activate 成功 | pending 保留内置；active 后外部可见、内置消失 |
| 无 visible 的真实 slot.rect | full/lite 正常显示；hidden/null/无效尺寸隐藏 |
| 暂停、关闭播放器、快速前后切歌 | 暂停保留，关闭淡出；无串封面、横色带、侧竖线 |
| −20° / 100% / 宽字体 / 长歌名 | 内容不裁切，跑马灯边缘平顺，控件不推动外宽 |
| 托盘增减、展开/收起、任务栏预览 | 平滑跟随，不沉入任务栏，不误判全屏 |
| 开始菜单→显示桌面、反向组合 | 不持续遮挡，不靠点击别的窗口恢复 |
| 自动隐藏、多 DPI/负坐标 | 随任务栏显隐，无瞬时 Lite、上浮、跳屏 |
| 关详情后立即重开、重复点胶囊 | 不闪退、不关闭再打开、不冒黑底 |
| 密度拖动后松手、九色快速切换 | 静止后无持续刷新/闪烁，心跳不受阻 |
| 前端退出、失联、后端重启 | 自管窗口收起，内置回退，旧 token 不复用 |

## 11. 原生实现参考入口

工程根：`C:\Users\ROG\Documents\Codex\2026-11-14\ban\work\LumiBar`。

- ExternalRenderers.cs、App.xaml.cs（Layout / RestoreCapsuleLayers）：所有权、真实槽位与层级。
- Native.cs、WindowMotion.cs（含 TaskbarWatcher）、TaskbarReservation.cs、Backend/TaskbarContinuity.cs、Backend/FullscreenPolicy.cs：原生边界与任务栏事件。
- WindowMotion.cs：独立运动线程、重复目标去重、垂直原生跟随。
- SurfaceWindow.cs、GlassBackdrop.cs、PopupShadow.cs、SurfaceLifetime.cs、PopupToggle.cs：透明材质、同步淡出和重开保护。
- CapsuleWindow.cs、CapsuleEdge.cs、SoftLightTexture.cs、Visuals.cs（含 SpectrumView）：胶囊布局、边缘、光效和频谱。
- AccentTheme.cs、Backend/AccentPalettes.cs、Typography.cs：动态强调色、WinRT 包装和双字体。
- MusicWindow.cs、FadingLyricsHost.cs、Backend/LyricFade.cs：歌词位置与上下渐隐。

参考时沿用设计思路和局部代码；SurfaceWindow 等依赖 App 单例与原生生命周期，直接整文件复制需要同步梳理依赖。具体协议仍以 BACKEND_RENDERER-0.7.md / contracts.d.ts 为准。
