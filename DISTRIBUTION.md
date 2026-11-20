# 对外分发前检查

## 字体

- Windows 默认中文字体为 Adobe 思源黑体简体版（Source Han Sans SC VF 2.005R）。构建从固定的 Adobe 上游提交获取字体、验证 SHA-256，并把字体与 `work/LumiBar/Assets/Fonts/SourceHanSans-OFL.txt` 的版权和 SIL Open Font License 1.1 文本一起放入安装包。
- macOS 的中文默认使用系统字体回退。西文使用随应用提供的 Geist，两端都保留 `work/LumiBar/Assets/Fonts/Geist-OFL.txt` 的版权和许可全文。

## 仓库公开

旧提交和旧 Windows 安装包曾包含六个字重的苹方。公开仓库前需清理可访问的历史副本，并检查以前的安装包和工作流产物。

安装包、私有 API 配置、用户设置、日志和转储均不随源码同步。
