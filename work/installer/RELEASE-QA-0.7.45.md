# LumiBar 0.7.45

2026-11-15，修复外部插件改值后内置设置页仍显示旧值，以及隐藏/离页控件回调继续改写设置的问题。用户已经手动恢复偏好，本轮保留这些当前值。

## 改动

SettingsWindow 保存当前页面的 Preferences 快照。OpenSettings 在显示前对照最新偏好，需要时重建当前页并保留滚动位置。Range/Toggle 事件增加页面 revision、窗口打开状态、控件 Loaded 和初始化同步期校验；移出页面或隐藏后的回调不提交。初始化屏蔽到 Dispatcher 低优先级工作执行时解除。用户在当前页面编辑仍即时预览、合并保存。

新增 `--inspect-settings-sync` 原生控件检查，仅允许 `--frontend-backend` 配合明确独立资料目录；在任务栏、API、媒体刷新和设备定位服务启动前检查并退出。

协议、插件租约和默认设置清单保持原样。前端协作补充见 outputs/Antigravity前端接入包/SETTINGS_LIFECYCLE_FIX-0.7.45.md，已复制到 Antigravity 工程目录，没有改其前端源码。

## 验证

- 修复前：work/settings-sync-before-data/settings-sync-result.json。外部 .57→.82 后内置仍显示 .57；音乐页外部字号/频谱响应变化后仍显示旧值；离页控件变化能改写并落盘。
- 修复后最终发布程序 9/9：work/settings-sync-final-data/settings-sync-result.json。覆盖重新打开同步、显示不回写、离页不回写、保存文件保持、正常编辑仍保存、隐藏控件不回写。
- 后端 287/287：work/v45-tests.txt。
- 安装包 20/20：work/installer-qa-20261115-204026/results.json。安装后的真实设置窗口、全部出厂默认项、升级保留配置、卸载保留数据及既有快捷方式。
- 第一次验包的 3s 管道读取窗口超时，同期冷启动日志中有 3.9s 封面读取。测试读取窗口调整为有界 8s，并补充失败时清理独立进程/安装的 finally；随后安装检查全部通过。未修改生产 RPC 超时或媒体流程。
- 实机 0.7.45 PID 74656；程序集匹配发布目录，Explorer PID 15248 未变，用户 settings.json 哈希前后完全相同。当前密度 0.5、字号 26、频谱响应约 1.2 保留。work/v45-live-verification.json。

## 交付

发布目录 work/package-0.7.45。

安装包 outputs/安装包/LumiBar-0.7.45-windows-x64-setup.exe。
