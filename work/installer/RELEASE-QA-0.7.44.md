# LumiBar 0.7.44

2026-11-15。外观页新增九款糖果渐变强调色，位于主题下方；包含追加的奶油黄，默认保留冰糖蓝。

## 实现

`Backend/AccentPalettes.cs` 统一色值、名称、合法 ID 与浅深主题文字色。设置按 `Preferences.AccentColor` 保存；缺失或无效值回退 blue，旧资料中的其它显式偏好保持优先。出厂清单新增 AccentColor，共 27 项。

`AccentTheme` 就地更新已有画刷并用 220ms EaseOut 过渡。关闭动画时立即更新。画刷的原生附加属性保留角色，遍历现有窗口和资源字典恢复 WinRT 托管包装；原生动画代次去重，解决压力测试抓到的重复动画目标错误。纯强调色更改不调用全量 Save、字体刷新、任务栏布局、音频设备或原生玻璃配置。

设置中九个单选色块有名称、提示、勾选标记、焦点和过渡。天气预警、温度冷暖、天气氛围和音乐封面配色保留各自语义。协议 v1 增补 appearance 与 settings 字段，见前端接入包的 ACCENT_COLORS-0.7.44.md。

## 检查

- 后端 287/287：`work/accent-tests.txt`。含九色完整性、标准化、旧蓝色保持、浅深文字对比度、设置变更分类和旧资料/保存加载。
- 独立真实窗口实测：主题下显示九个色块，奶油黄与蜜桃粉同步改变导航选中态、主题选中态、滑条和开关；浅深模式均可用。
- 管道压力检查 24/24：`work/accent-ui-qa2/accent-results.json`。逐色生效和保存，45 次快速切色、拒绝非法 ID、重复无变化请求，以及媒体/玻璃/布局偏好保持。最终日志没有 accent-transition 错误。
- 压力切色期间 glass-metrics 的 refresh/source/config/mask/size/preview/raster/completed 均为 0，见同目录 diagnostics.log 的 19:11:42–19:11:45 片段。
- 最终发布程序重启检查 4/4：`work/accent-ui-qa2/accent-restart-result.json`。恢复选择及明暗主题，协议提供三色数组，正常退出。
- 安装包 20/20：`work/installer-qa-20261115-191541/results.json`。真实安装 UI、全部 27 项默认值、带奶油黄旧资料覆盖升级、卸载保留数据和既有快捷方式。
- 实机 0.7.44 PID 86392，Explorer PID 15248 未变，程序集与发布目录一致；原有外观和任务栏偏好保留，当前强调色为 blue。收藏和逐曲歌词微调与备份相同。配置文件哈希在启动后变化，自动定位会更新 City，故不将文件字节不变作为偏好保持结论。`work/v44-live-verification.json`。
- 完成实际截图核对后提交 Computer Use completion_summary 并重置会话。

## 交付

发布目录：`work/package-0.7.44`。

安装包：`outputs/安装包/LumiBar-0.7.44-windows-x64-setup.exe`。

九色概念对照图：`outputs/配色预览/LumiBar-九色糖果强调色.png`。
