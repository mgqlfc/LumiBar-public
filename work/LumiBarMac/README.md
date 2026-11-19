# LumiBar for Mac

LumiBar 的 macOS 27 客户端。音乐岛使用 AppKit 浮层和屏幕安全区定位，天气使用系统菜单栏入口。天气详情沿用原版彩色天气图标、局部柔光、逐时温度曲线和逐日温差条。天气与歌词浮窗采用 SwiftUI 的 Liquid Glass。歌词页沿用原版位置，上方为同步控制，中间为歌词，底部为封面、曲目信息和播放控制。

## 构建

使用带 macOS 27 SDK 的 Xcode 或 Command Line Tools。从仓库根目录运行：

```sh
bash work/LumiBarMac/build.sh
open work/LumiBarMac/build/LumiBar.app
bash work/LumiBarMac/package.sh
```

构建脚本按当前 Mac 架构生成应用，并沿用仓库中的应用图标和天气图集。默认使用临时签名，适合本机开发。可通过 `LUMIBAR_SIGN_IDENTITY` 指定签名身份。DMG 保存于 `work/LumiBarMac/build`，公开分发前需要 Developer ID 签名及 Apple 公证。

## 使用

- 菜单栏天气入口悬停显示速览，点击打开详情。首次使用时搜索城市或点击定位图标。右键可打开设置或退出。
- 天气优先使用仓库中的和风天气代理，失败时依次使用 Open-Meteo 和 MET Norway。官方天气预警取决于代理数据。
- 播放 Apple Music 或 Spotify 时显示音乐岛。首次连接时在 macOS 的「隐私与安全性 → 自动化」允许授权。悬停可播放、暂停或切歌，点击可打开专辑与同步歌词。歌词支持 LRCLIB、UTF-8 LRC 文件和逐曲偏移。
- 需要频谱时，在设置启用「实时频谱」，并授权系统音频录制。频谱只分析选中播放器的音频，不保留录音。
- 网易云音乐、QQ 音乐和浏览器的媒体会话暂未接入。多显示器、全屏及菜单栏自动隐藏还需要专项实测。

使用示例天气与歌曲检查外观：

```sh
open -n work/LumiBarMac/build/LumiBar.app --args --preview --weather --lyrics
```
