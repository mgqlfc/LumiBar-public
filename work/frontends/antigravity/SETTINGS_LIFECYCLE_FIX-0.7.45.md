# 0.7.45：切换渲染器后的设置同步与回写

用户反馈：从插件切回内置后，玻璃密度、歌词字号、频谱响应的显示/数值异常。用户已手动调回，后续调试以这些当前值为准，不恢复旧备份或出厂值。

## 内置前端查到并修正的两处问题

1. SettingsWindow 在启动时创建并缓存控件；OpenSettings 只显示旧页面，没有从当前 Preferences 更新。外部修改 .57→.82 后，设置页仍显示 .57；歌词 31→29、频谱响应 1.7→2.1 也会显示旧值。
2. Range.ValueChanged / Toggle.Toggled 缺少当前页面、窗口可见性和同步期校验。已经移出页面的滑条回调仍能执行 action、排队保存，导致模型/文件遭到旧控件改写。

0.7.45 在打开设置前比较页面快照与最新 Preferences，变化时重建当前页并保留滚动位置。每页带 revision；只有当前 revision、窗口已打开、控件已 Loaded、初始化同步结束的回调才能提交。构建控件时屏蔽回写，到 UI 队列低优先级阶段再解除；离页/隐藏的旧回调直接忽略。正常编辑继续预览、合并保存。

恢复内置的 release 本身不重置偏好；不额外保存一份过时 Preferences，再在释放时覆盖当前设置。

## 对 Antigravity 的同步要求

- 从后端读到真实设置后再填控件；初始化、重连、SyncFromState、主题变化、界面显示和渲染释放都只是同步，不能自动发送 settings.patch。
- 当前源码的 suppressDepth / CanSendUserChange 已有对应保护，请保留，并核对当前编译产物确实包含它们。隐藏窗口、旧页面、mock→real 的迟到事件需要同样拦截。
- 只发送用户本次修改的字段。不要序列化整个带默认值的 SettingsDto 作为补丁；缺失字段沿用服务器当前值。
- 不在 release / shutdown / Dispose 中把本地缓存设置整体写回。用户已在内置设置调回的值优先，取消尚未发送的旧输入。
- 密度是 0.35–0.95 的比例；显示百分比除以 100。歌词字号 18–36；频谱响应 sensitivity 0.5–2.5，与窗口/频谱像素高度不是同一个字段。
- 每次 await 返回后核对请求代次和 backend instanceId，旧响应不能覆盖新 CurrentState。更新 Slider.Value、ToggleSwitch.IsOn 等控件属性期间保持回写抑制。

本轮没有改动 Antigravity 前端源码或它的本地配置。

## 验证

修复前的真实 XAML 控件检查复现旧显示与离页回写；记录在 `work/settings-sync-before-data/settings-sync-result.json`。最终发布版本完成 9/9：外部更新后重开同步、显示不改写、离页控件不改写、文件保持、当前页面正常改动仍保存、隐藏控件不能回写。记录在 `work/settings-sync-final-data/settings-sync-result.json`。后端检查 287/287。

这些检查用独立资料目录，无任务栏预留、媒体采集和设备定位；用户当前配置不参与测试。协议继续 v1，没有新增必填字段。
