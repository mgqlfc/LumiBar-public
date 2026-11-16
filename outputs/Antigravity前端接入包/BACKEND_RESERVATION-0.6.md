# 0.6-dev：任务栏原生占位与内置天气界面

2026-11-14。本文件补充 0.5 字体接口文档；协议仍为 v1。

## 已落地

内置双胶囊由后端负责定位。自带 C++ 桥接组件通过 Windows XAML Diagnostics 找到系统托盘布局，在其左侧放置透明占位项，让 Explorer 真正缩减应用图标可用区域。运行不依赖 Windhawk、WidBar 或其他外部软件，不改系统图标缩小偏好。

顺序：完整双胶囊 → 提前留出约 48 DIP 余量后一起切 Lite → Windows 自行缩小图标或进入溢出。布局恢复带滞回，减少来回跳变。暂停保持音乐胶囊；媒体会话结束后淡出。占位组件失联时将双 Lite 临时移到任务栏上方，避免覆盖应用图标。

本机 Windows 29704.1010 已验证 StackPanel 容器。Grid 路径已实现，其他 Windows 版本和多显示器尚需实测；多个候选容器时返回 unsupported，不猜目标。

## 新增只读接口

`windows.reservation` 无参数，返回：

- `snapshot`：`status`、`error`（HRESULT）、`slot`（物理屏幕像素矩形）、`widthDip`、`explorerPid`、`at`（单调时钟毫秒）。无原生桥接的独立预览后端返回 null。
- `slots`：`musicSlot`、`weatherSlot`、`state`。无音乐会话时音乐隐藏属于正常行为；空间拥挤时两颗 Lite 均保留。
- `owner`：`builtin` 或 `preview-no-reservation`。

`windows.hints` 增加 `nativeReservation` 和 `renderingOwner`（builtin / preview）。内置模式无参数调用返回实际胶囊布局。`windows.planCapsules` 继续接受前端尺寸，返回规划结果，不申请占位。

`hello.capabilities.builtinTaskbarReservation` 表示内置桥接兼容状态。**`taskbarReservation`、`rendererReplacement`、`reservationSupported` 仍为 false**：外部前端 HWND 注册、所属进程校验和占位租约交接尚未完成。不要据内置成功状态重复绘制胶囊或覆盖系统托盘。

Antigravity 仍独立控制展开方式、浮窗大小、方向和动效；只订阅数据与位置建议。不要自行注入 Explorer 或另建占位组件。正式接管时由后端统一仲裁槽位，防止两套前端抢占。

## 安全释放与实测

后端每 200 ms 续租，超过 3 秒或检测到所属进程结束时，桥接移除自己创建的占位项。退出/崩溃清理通过实测，无需重启 Explorer。

- 固定 96 DIP 原生占位：托盘左缘从 1950 移至 1791 px，释放后回到 1950 px。
- 32 个测试应用窗口、暂停测试媒体会话：100 个采样，44 个 Lite 采样；Lite 期间 0 次隐藏胶囊、0 次与应用图标重叠。系统应用图标实际缩小并出现溢出按钮。
- 93 项后端回归测试通过，见 backend-tests.txt。
- 最新只读运行检查：reservation-live-check.json。该文件不构成视觉验收。

## 本轮内置天气调整

使用用户选定第一版立体天气图标的透明底版本，随程序打包。图标本身不叠加光晕；三层暖/冷柔光独立绘制在图标附近的玻璃背景，并随图标滚动定位。禁止整面染色。天气区块去掉卡片框，一小时降水文案已按最新要求全部删除；后端仍提供有来源与精度说明的 15 分钟模型趋势，插件可以自行决定展示方式。首屏展示四天预览、风速与 US AQI，其余天数和空气详情可展开。

本轮编译通过；Computer Use 已恢复，检查了深浅主题、首屏模块与滚动后的光源位置。已消除浮窗白边和柔光的内部矩形裁剪边，曲线节点、图标、时间和温度共用列坐标。柔光使用 Composition 慢速位移动画，关闭浮窗后停止，遵守平滑动画开关；静态抖色纹理用于减轻渐变色阶。实测截图保存在开发审查目录，未据此声称达到特定帧率。

## 打包前仍需完成

- 外部渲染器注册和互斥接管；多显示器与 Windows 版本兼容矩阵。
- 桥接 DLL 当前随 Explorer 保持加载到其退出，安装器需使用版本化文件部署并安排旧版本清理，不能直接覆盖已加载文件。
- 安装/升级/卸载、签名及安装包验收。当前是开发版，不作为朋友分发包。

.NET / Windows App SDK 已配置自包含；C++ 静态 CRT。正式用户数据默认放在 LocalAppData/LumiBar，开发数据通过 LUMIBAR_DATA_DIR 隔离，不往安装目录写配置。
