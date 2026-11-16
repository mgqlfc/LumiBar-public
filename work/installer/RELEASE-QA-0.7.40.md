# LumiBar 0.7.40

2026-11-15，Windows 11 x64。

## 本轮原因与修复

0.7.39 在自动隐藏任务栏首次展开时调用 `Ui.Enter(..., false)`，先对尚未创建的 `Translation` 属性调用 `StopAnimation`。隔离测试第一次展开即抛出 `E_INVALIDARG`，日志明确给出 `The specified property was not found or cannot be animated. Context: Translation`。0.7.40 先启用 WinUI 位移属性，再停止和复位动画。基线证据：`work/v39-surface-probe-data/diagnostics.log`。

自动隐藏时，相同窗口坐标不再重复提交，已经隐藏的胶囊不再重复改透明度或发送隐藏通知；窗口层级修复增加重入保护，几何事件合并到一个待执行任务中。UIA 内部元素位置事件不会再逐个触发胶囊布局。

任务栏程序预览原先触发全屏判定，导致胶囊隐藏。现在只对 Explorer 自己的任务栏预览和托盘浮层豁免全屏判定，真实全屏窗口仍照常处理。自动隐藏的垂直位移直接跟随任务栏，不叠加窗口入场位移；横向空间测量跨垂直动画保留，短暂 native ready 状态复用有效槽位。

发布构建强制包含合并后的 `LumiBar.pri`。安装验证实际打开设置页，覆盖仅启动后台服务时不会触发的主题资源加载。测试数据目录使用独立实例锁，不干扰其他前端的后台进程。

开始菜单快捷方式显式引用 EXE 内的 LumiBar 图标。安装测试使用显式禁用快捷方式参数，并核对用户现有快捷方式的内容哈希。早期测试改动的分组快捷方式已补回；根目录兼容入口保留。

## 验证

- 后端 260/260：`work/v39-final-tests.txt`。
- 修复前的隔离测试在第一次显示时出现 Translation 参数错误；修复后两个胶囊共 24 次展开，覆盖明暗、Full/Lite 和重复隐藏，正常退出：`work/v40-surface-probe-data/diagnostics.log`。
- 正常任务栏启动/退出测试：退出码 0，Explorer PID 一直是 15248：`work/v40-live-startup.json`。
- 完整资源同步后的正常运行监测：40.9 秒，全部 UI 探测响应，末段私有内存约 400.7 MB，Explorer PID 15248：`work/v40-live-watch.json`。CPU 数值采用一个逻辑核心为 1 的口径。
- 安装、实际设置界面、诊断、升级、卸载、用户数据和快捷方式保留：16/16，`work/installer-qa-20261115-153044/results.json`。
- 本轮没有重放完整的程序预览/自动隐藏鼠标组合操作；Computer Use 返回物理 Esc 停止，已结束画面控制。当前修正版保留正常运行，等待下一轮实机交互核对。

## 交付

安装包：`outputs/安装包/LumiBar-0.7.40-windows-x64-setup.exe`。
发布目录：`work/package-0.7.40-final`。
实机目录：`outputs/LumiBar-实机版`。
安装包保持未签名，诊断转储、日志、测试配置与密钥不进入安装包。
