# LumiBar 0.7.49 验证记录

2026-11-16。本轮接入 Antigravity Synthwave 的 Win2D 底层支持，优化实际频谱采样和独立 IPC 读取；双胶囊共同位置相对 0.7.48 上移 1 DIP。用户设置保留。

## 改动

- frontend PackageReference Microsoft.Graphics.Win2D 1.3.2，锁文件、完整 PRI、自包含运行组件检查。
- prepare-offline.ps1 导出 19 个包及 SHA-256；build.ps1 从本地源还原、显式 --no-restore 构建/发布，保留现有插件配置。支持缓存迁移后的新路径。
- Rendering/SynthwaveEffects.cs 提供 GaussianBlur、Screen、DestinationIn/Out、扫描线遮罩、镜像倒影和 VHS 切片；GraphicsSelfTest 使用真实 CanvasControl、RenderTarget 和像素检查。
- FFT 保留 2048 点精度，改为重叠窗口；spectrum.get 用线程安全媒体状态和音频快照，绕过 UI Dispatcher。前端用 25ms 固定节拍，UI 通知合并。
- 新曲目的封面尚未载入时清掉上一首颜色，当前封面取色直接输出 #RRGGBB；绘制降饱和独立处理。
- v6 默认模板 420×384，不打包用户 antigravity-plugin.config.json。
- 主安装脚本可选 -IncludeAntigravity -OfflineFrontend；带前端与普通包分别使用独立发布目录。

## 验证

1. 后端 341/341：work/v49-backend-tests.log，含 48k/44.1k/16k FFT 新帧率回归。
2. v6 配置 7/7：work/v49-frontend-support-tests.log，新建、旧 548 高度迁移、持久化和 v6 自定义尺寸保留。
3. 全空 NuGet 缓存，唯一数据源为本地 19 个包；离线发布、--no-restore 构建通过。源移动到包含空格的新目录后，再从另一份空缓存发布也通过。work/win2d-offline-verify-20261116-211237。
4. Win2D 8/8：publish-moved/graphics-self-test/graphics-result.json；检查实际 Alpha 挖空、模糊外扩、Screen 混色、菲涅尔 Alpha 衰减、倒影、切片与渐变文字。此轮没有操作桌面或捕获屏幕。
5. 实际 BridgeService 与独立命名管道，无声合成采样，连续 6.48 秒：37.94 回调 Hz、34.08 新数据 Hz，P95 31.74ms，最大 33.33ms。后端 UI 多次忙碌 450ms 时没有拖住频谱读取。work/v49-spectrum-proof/spectrum-result.json。
6. 第一轮 36ms 后延迟低于目标；改为 30ms 后回调 33.3Hz、真实新数据 29.3Hz。按测量增加 FFT 与消费余量后达到第 5 项结果。没有改系统音量或播放声音。
7. 最终带前端安装包 24/24：work/installer-qa-20261116-213908/results.json。安装后的 frontend.exe 实际运行图形检查；v6 模板正确，升级保留已有自定义高度/宽度，卸载保留用户数据，原快捷方式不变。
8. 已更新源码工程的 dist，全量复制运行组件，旧目录备份在 work/frontend-deploy-backup-20261116-213825；用户插件配置哈希保持，DLL 匹配包。work/v49-frontend-deployment.json。
9. 实机后端 0.7.49 PID 22852，Explorer PID 74088 未在更新中变化；settings.json 哈希保持，发布 DLL 一致。work/v49-live-verification.json。
10. 当前缩放 1.65625 下天气槽位 top 1535→1534，宽度 173 保持。共同偏移从 2 DIP 改为 1 DIP；Full/Lite 和外部槽位同步。work/v49-alignment-before.json、v49-alignment-after.json。

## 交付

- outputs/安装包/LumiBar-0.7.49-windows-x64-setup.exe
- outputs/安装包/LumiBar-0.7.49-windows-x64-with-antigravity-setup.exe
- outputs/安装包/SHA256SUMS.txt
- work/frontends/antigravity/BACKEND_SYNTHWAVE_SUPPORT-0.7.49.md
- outputs/Antigravity前端接入包/BACKEND_SYNTHWAVE_SUPPORT-0.7.49.md

前端可继续将真实效果链接进当前 Synthwave 布局；本轮交付的是渲染依赖、合成原语、构建/打包与数据通道支持。源码改动留在当前工作区，本轮没有推送 GitHub。