# LumiBar 0.7.51 验证记录

2026-11-16。音乐胶囊左侧新增两条频谱，歌名与歌手名相对 0.7.50 再左移 2 DIP。

- SpectrumBackdrop 支持 extraLeftBars，胶囊和设置预览传 2，创建 42 个独立圆角柱几何体；详情页继续采用默认 40 个。输入协议仍为 20 段实时音频，绘制时平滑插值。
- 胶囊在 SizeChanged 和 Lite 切换时重算左边距：原绘制宽度乘 2/40 为新增空间，右边距仍为 6 DIP。扩展后的宽度除以 42，得到原有柱距；原来的 40 个位置与右边界保持。继续使用有界 ShapeVisual 和非负圆角几何体。
- NameColumnSpacing 从 6 改为 4 DIP，右边界固定，可用文字宽度再增 2 DIP。MarqueeTitle 的实际布局宽度变化会触发滚动阈值、行程、渐隐及控件预留范围重算。封面和控件的位置不变。
- Release 自包含发布与原生安装包编译通过：work/v51-build.log。此轮没有新增测试用例或捕获桌面图像。
- 实机版本核对为 0.7.51，设置文件哈希和偏好核对通过，运行 DLL 匹配发布包，Explorer PID 74088 保持：work/v51-live-verification.json。

安装包：outputs/安装包/LumiBar-0.7.51-windows-x64-setup.exe。
