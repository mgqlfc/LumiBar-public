# LumiBar for Mac

LumiBar 的 macOS 27 客户端。音乐和天气在系统菜单栏用一组相邻的紧凑圆角胶囊。音乐显示小封面和频谱，悬停展开播放控制，点击在入口下方打开歌词详情。天气显示彩色天气图标和温度。天气详情沿用原版彩色天气图标、局部柔光、逐时温度曲线和逐日温差条。天气与歌词浮窗采用 SwiftUI 的 Liquid Glass。原版图集和 Geist 从仓库内复用，中文默认跟随 macOS 系统字体。天气保留三列速览和完整预报，歌词页保留上下渐隐、封面柔光、底部频谱和播放控件。

## 构建

使用带 macOS 27 SDK 的 Xcode 或 Command Line Tools。从仓库根目录运行：

```sh
bash work/LumiBarMac/build.sh
open work/LumiBarMac/build/LumiBar.app
bash work/LumiBarMac/package.sh
```

构建脚本按当前 Mac 架构生成应用，并沿用仓库中的应用图标和天气图集。也可以给构建、打包脚本传入输出目录，以便保留正在使用的版本。默认使用临时签名，适合本机开发。可通过 `LUMIBAR_SIGN_IDENTITY` 指定签名身份。DMG 保存于 `work/LumiBarMac/build`，公开分发前需要 Developer ID 签名及 Apple 公证。

## 使用

- 菜单栏宽度固定。播放音乐时，音乐和天气各占一半，天气显示图标和温度。暂停或停止时，天气铺满并在右侧显示降水概率。天气入口悬停显示速览，点击打开详情，再点击收起。首次使用时搜索城市或点击定位图标。右键可打开设置或退出。
- 天气优先使用原版的和风天气代理，失败时依次使用 Open-Meteo 和 MET Norway。定位按区县解析名称并继续用原坐标查询实况。预警取决于代理数据。
- 网易云音乐优先。右键菜单栏天气图标，打开「设置 → 音乐与歌词 → 连接网易云音乐」，允许 macOS 辅助功能后，在网易云主窗口播放一首歌。LumiBar 优先通过辅助功能读网易云播放栏和调用播放控件。播放列表和历史记录用于只读匹配封面、歌手与曲目 ID。播放栏暂时读不到进度时，用 Core Audio 的应用输出状态辅助识别，进度会标为估算。支持网易云和 LRCLIB 同步歌词、UTF-8 LRC 导入和逐曲偏移。
- Apple Music 和 Spotify 通过 macOS 自动化权限连接。悬停可调出播放控件，点击可打开歌词。
- 需要频谱时，在设置启用「实时频谱」，并授权系统音频录制。频谱只分析选中播放器的音频，不保留录音。
- QQ 音乐和浏览器媒体会话暂未接入。多显示器、全屏及菜单栏自动隐藏还需要专项实测。

## 视觉对照

预览使用 [Windows 还原包](../../outputs/Mac前端还原包/README.md) 的同一份样例与封面。每个窗口以逻辑尺寸绘制，浅色底板为 `#F3F5FA`，深色底板为 `#171922`。预览使用隔离的设置，播放按钮不会联系真实播放器。

打开菜单栏的交互样例：

```sh
open -n work/LumiBarMac/build/LumiBar.app --args --preview --weather --lyrics
open -n work/LumiBarMac/build/LumiBar.app --args --preview --weather-hover
```

逐窗对照时可指定 `weather`、`weather-hover`、`weather-capsule`、`weather-lite`、`weather-wide`、`weather-alerts`、`music` 或 `settings`。默认浅色，带 `--dark` 时显示深色。

```sh
open -n work/LumiBarMac/build/LumiBar.app --args --reference-case music --dark
```
