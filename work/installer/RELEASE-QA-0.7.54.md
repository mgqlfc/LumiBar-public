# LumiBar 0.7.54 验证记录

2026-11-17。预警徽章只显示事件名称；修复开始菜单打开时胶囊误变 Lite、向右跳并升到任务栏上方。

## 原因与改动

实机 0.7.52 中，开始菜单打开后 UIA 测量变为 unavailable，同时原生预留连接处于 failed/0x8000ffff。ReservationLayoutPolicy 虽保留过应用右边界，仍仅在 geometryVerified 为 true 时承认空白区可用；Native.ReadTaskbar 的备用右边界又大于此前测量值。App 因此走 Elevated 分支，强制两颗 Lite 并上移。复现记录：work/v53-inspection/start-before.jsonl。

- 开始、搜索等 Shell 浮层显示期间，沿用同一 Explorer、DPI 和任务栏尺寸下已测量的左右边界；关闭后短暂等待 UIA 恢复。
- 同时检查已实际观察到的 Shell 窗口可见性，避免只按前台焦点判断。0.7.53 第一轮只检查前台状态，焦点变化后仍会误降级；0.7.54 补齐可见性检查。
- 原生托盘边界真正缩小时，继续收紧空间并进入 Lite；Explorer 或任务栏尺寸改变时不套用旧坐标；没有历史测量时不捏造空白区。
- windows.hints 的 geometryStatus 仍如实返回 unavailable，实际 slots 和内置胶囊沿用可信的临时位置。没有把旧坐标声明为新的原生预留租约。
- 天气预警可见短标签只显示“大风”“海上大风”等事件名称，等级保留在图标颜色、读屏名称及天气源的完整预警内容中。

## 验证

- 后端回归 348/348，包含 Shell 测量失效、托盘增长、Explorer 更换、尺寸变化和未知几何的回归；预警短标签同时覆盖普通与海上大风。
- 实机操作三轮开始菜单开合；65 秒连续记录得到 679 次采样，其中 380 次 measured/full、299 次 unavailable/full，未出现 Lite。天气 top 始终 1534、right 始终 1921；音乐宽度始终 395 物理像素。第三轮另有后续 taskbar-layout 日志，仍为 full、elevated=False。记录位于 work/v53-inspection/start-final-v54.jsonl、start-final-result.json。
- 原生预留连接在测试时仍为 failed/0x8000ffff；本次验证覆盖该异常下的 Shell 切换连续性，没有修改原生 DLL 或重启 Explorer。独立连接错误留作后续诊断。
- 最终 0.7.54 自包含 Release 与安装包编译通过。运行 DLL 匹配发布包，用户设置文件哈希保持，偏好核对通过，Explorer PID 15212 保持。

安装包：outputs/安装包/LumiBar-0.7.54-windows-x64-setup.exe。运行核对：work/v54-live-verification.json。
