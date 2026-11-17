# LumiBar 0.7.52 验证记录

2026-11-16。撤销左侧新增的两条频谱；音乐胶囊展开时，封面、歌名和歌手名相对 0.7.51 共同右移 1 DIP。

- SpectrumBackdrop 恢复固定 40 条，移除 extraLeftBars 和 LeftExtension；胶囊恢复原来的固定频谱边距。该文件与新增频谱前的版本一致。
- 音乐内容左内边距从 8 改为 9 DIP，设置预览从 4 改为 5 DIP；初始化及 Lite 恢复展开同步。NameColumnSpacing 保持 4，封面和文字的相对间距保留。
- 文字列可用宽度减 1 DIP，MarqueeTitle 按实际布局宽度重算滚动阈值、行程、渐隐和控件预留范围。胶囊窗口、控件位置和 Lite 封面的居中位置保持原值。
- 自包含 Release 发布通过，实机运行 DLL 匹配发布包。设置文件哈希和偏好核对通过，Explorer PID 74088 保持：work/v52-live-verification.json。
- 此轮没有新增测试用例或捕获桌面图像。构建日志：work/v52-build.log。

安装包：outputs/安装包/LumiBar-0.7.52-windows-x64-setup.exe。
