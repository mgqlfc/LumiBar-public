# LumiBar 0.7.50 验证记录

2026-11-16。音乐胶囊的歌名和歌手名共同左移 2 DIP。

- MusicCapsuleContent 的封面与文字列间距从 8 改为 6 DIP；初始化和 Lite 恢复展开共用 NameColumnSpacing，设置预览同步。封面变换、播放控件、胶囊尺寸和任务栏位置保持原值。
- 文字列的右边界固定，可用宽度增加 2 DIP。MarqueeTitle 的 SizeChanged 会重新测量；溢出阈值、滚动行程和渐隐遮罩沿用实际 ActualWidth。悬停控件继续通过 Reserved 动画预留 84 DIP，右边界与控件保持相对位置，无需额外叠加位移。
- Release 自包含发布与原生安装包编译通过，记录在 work/v50-build.log。此轮没有新增测试用例或捕获桌面图像。
- 已按 activate-release.ps1 启用实机版：设置文件哈希相同，偏好核对通过，运行 DLL 与发布包一致；Explorer PID 74088 保持，详见 work/v50-live-verification.json。

安装包：outputs/安装包/LumiBar-0.7.50-windows-x64-setup.exe。
