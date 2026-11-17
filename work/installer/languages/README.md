# 简体中文安装语言

`ChineseSimplified.isl` 是本项目原先随本机 Inno Setup 工具使用的简体中文翻译文件，本次将同一份文件纳入源码，让全新构建机也能生成中文安装向导。

- 文件头声明兼容 Inno Setup 6.5.0+。
- 维护者：Zhenghan Yang (Kira)，原始署名保留在文件内。
- 上游：https://github.com/kira-96/Inno-Setup-Chinese-Simplified-Translation
- Inno Setup 翻译入口：https://jrsoftware.org/files/istrans/

`LumiBar.iss` 从此目录读取语言文件，构建机无需另行下载翻译。
