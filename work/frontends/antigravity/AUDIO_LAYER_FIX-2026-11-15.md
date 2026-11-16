# 启动音量与任务栏层级修复

2026-11-15，实机后端版本 0.7.35。

## 音量修复已应用到前端源码和 dist

根因：`LyricsFlyoutWindow.xaml` 给系统音量滑块设了 `Value="50"`，同时连接 `ValueChanged`。`InitializeComponent()` 会在构造窗口时触发事件；当时 `_suppressVolumeEvent` 默认为 false，事件直接调用 `SetMasterVolume(0.5)`。此外，读取失败还返回 0.5，容易引入同样的问题。

已修改：

- 删除 XAML 音量初始值与 XAML 事件挂接；默认禁用滑块。
- 完成第一次实际音量读取后才挂接 `ValueChanged`。
- 所有系统读取 / 界面同步都使用抑制状态，并通过 `finally` 恢复。
- `VolumeWriteGuard` 要求读取成功、控件已加载且启用、没有界面同步、存在用户指针或键盘操作、数值有效，才允许写系统音量。
- 读取失败不再返回 50%，控件保持禁用；不向系统写入回退值。
- `AudioVolumeHelper` 拒绝非有限值和越界值。保留 `LUMIBAR_AUDIO_DRY_RUN=1` 诊断模式：会记录并阻断所有音量和静音写入，仅供隔离验证使用。

相关文件：`Windows/LyricsFlyoutWindow.xaml`、同名 `.xaml.cs`、`Services/AudioVolumeHelper.cs`、`Services/VolumeWriteGuard.cs`。

请在后续 UI 合并时保留这些约束。启动、切歌、展开浮窗、还原界面和读取设备状态均不应写入系统音量。系统静音只从明确点击静音按钮进入，音量修改只从用户操作滑块进入。

10 项防护测试通过。隔离启动完成构造，音量写入尝试为 0；随后调用正式 `frontends.launch` 验证，启动前后音量均为 `0.13876165449619293`（约 13.88%），没有变成 50%。用户前端配置没有用测试配置覆盖。

## 后端负责层级恢复

已捕获“开始菜单 → 显示桌面”的持续遮挡：胶囊与任务栏都在分层 6，胶囊没有隐藏、最小化或 DWM cloak，但其中心命中的是任务栏。此时普通 `GetWindow` 遍历无法关联两个窗口，旧版仅同步分层的处理不会修复实际遮挡。

新的 `LumiBar.TaskbarBridge.v4.dll` 在 Explorer 端检查胶囊与任务栏重叠区域的真实命中，确认任务栏盖住胶囊后，以不激活、异步的方式恢复顺序。前台变化和重排事件触发快速检查，定时检查继续作兜底。只处理后端登记的两个胶囊窗口，并继续校验进程、租约与允许的分层。

`windows.reservation` / `windows.hints.nativeReservation` 增加可选诊断字段 `layerError`、`taskbarBand`、`layerRepairs`。对外协议仍为 1，内部共享内存仍为版本 2、144 字节。

用户按原来的顺序复测后确认没有持续遮挡。120 秒记录共 3799 次采样，最终两颗胶囊都正常命中自身；出现的短暂任务栏覆盖记录最长 43 毫秒，均自动恢复。截图和命中采样不用于宣称屏幕帧率。
