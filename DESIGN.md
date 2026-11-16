---
name: LumiBar
description: Windows 11 原生任务栏双胶囊与通透天气浮窗
colors:
  teal: "#26979e"
  accent-default: "#6ce7da → #21bec2 → #118fb1"
  curve-light: "#128991"
  curve-dark: "#5ccdcf"
  glass-light: "#f7f9fd"
  glass-dark: "#181b23"
  selected-city: "rgba(92,151,160,0.0863)"
  divider: "rgba(128,128,128,0.0784)"
  capsule-light: "rgba(255,255,255,0.3137)"
  capsule-dark: "rgba(196,207,234,0.1373)"
  sun-primary: "#ffca5b"
  sun-secondary: "#ffad4e"
  night-primary: "#557df4"
  night-secondary: "#938beb"
  mixed-primary: "#faca71"
  mixed-secondary: "#97cae1"
  cloud-primary: "#99bacf"
  cloud-secondary: "#83a3c1"
  rain-primary: "#4fb5eb"
  rain-secondary: "#407acd"
  snow-primary: "#96d3ec"
  snow-secondary: "#b8bfea"
  fog-primary: "#a1bac5"
  fog-secondary: "#a9bdc5"
  storm-primary: "#9577dc"
  storm-secondary: "#5b7dcb"
typography:
  display:
    fontFamily: "FontFamily.XamlAutoFontFamily / Prefs.FontFamily"
    fontSize: "52 DIP"
    fontWeight: 400
  headline:
    fontFamily: "FontFamily.XamlAutoFontFamily / Prefs.FontFamily"
    fontSize: "24 DIP"
    fontWeight: 600
  title:
    fontFamily: "FontFamily.XamlAutoFontFamily / Prefs.FontFamily"
    fontSize: "16 DIP"
    fontWeight: 600
  body:
    fontFamily: "FontFamily.XamlAutoFontFamily / Prefs.FontFamily"
    fontSize: "14 DIP"
    fontWeight: 400
  label:
    fontFamily: "FontFamily.XamlAutoFontFamily / Prefs.FontFamily"
    fontSize: "12 DIP"
    fontWeight: 400
  icon:
    fontFamily: "Segoe Fluent Icons"
    fontSize: "18 DIP"
rounded:
  temperature-bar: "3 DIP"
  cover: "7 DIP"
  icon-button: "10 DIP"
  city-chip: "16 DIP"
  capsule-surface: "18 DIP"
spacing:
  compact: "4 DIP"
  small: "8 DIP"
  medium: "12 DIP"
  section: "14 DIP"
  summary-columns: "20 DIP"
  weather-inset: "22 DIP"
components:
  icon-button:
    backgroundColor: "transparent"
    rounded: "{rounded.icon-button}"
    width: "38 DIP"
    height: "38 DIP"
    padding: "0 DIP"
  selected-city:
    backgroundColor: "{colors.selected-city}"
    typography: "{typography.label}"
    rounded: "{rounded.city-chip}"
    padding: "5 DIP 10 DIP"
  capsule-light:
    backgroundColor: "{colors.capsule-light}"
    rounded: "{rounded.capsule-surface}"
    height: "34 DIP"
  capsule-dark:
    backgroundColor: "{colors.capsule-dark}"
    rounded: "{rounded.capsule-surface}"
    height: "34 DIP"
---

# Design System: LumiBar

## Overview

**Creative North Star: "通透玻璃，局部柔光"**

沿用用户选定的 `outputs/concepts/LumiBar-玻璃界面概念-v2.png`。任务栏上的音乐与天气各占一颗胶囊；点开天气后，信息紧凑地排在同一块玻璃上。彩色立体天气图标保留第一版造型，柔光从图标附近的背景层扩散。

本文记录 2026-11-14 内置 WinUI 3 天气与胶囊的源码状态、已确认方向和交互约束。最新天气深浅首屏与滚动截图已完成独立审查，结论 ship，范围为本轮局部柔光与相关布局；帧率和其他 DPI 等状态另行实测。最新天气要求以 `work/native-ui-brief.md`、`work/figma-weather-reference.md` 及本次修订为准，PRODUCT.md 已同步删除降水提示。

**Key Characteristics:**
- 原生薄亚克力，跟随系统明暗主题。
- 文字使用系统字体或用户已安装的字体，功能图标独立使用 Segoe Fluent Icons。
- 天气区块靠留白和轻分隔组织，不加板块框或窗口白描边。
- 局部多层柔光、低速 GPU 合成动效、微量静态抖色。

## Colors

青绿强调信息，天气色只服务图标附近的气氛；大面积底色保持通透、中性。

### Primary

`teal` 用于每日温差条、风速与 AQI 图标。逐时曲线按主题使用 `curve-light` 与 `curve-dark`，保持玻璃上的辨识度。

### Secondary

天气背景取成对色组：晴天暖金橙、晴夜蓝紫、多云日间暖金与浅蓝、阴天灰蓝、雨天蓝、雪天冰蓝淡紫、雾天灰青、雷雨紫蓝。天气代码的色组优先于普通昼夜色；夜间雷雨仍用雷雨色组。三层柔光依次取主色、副色、主色，不把这些色块当作整页底色。

### Neutral

`glass-light` 与 `glass-dark` 同时作为亚克力 tint 和 fallback。正文继承 WinUI 主题前景色，不固定成黑色或白色。次要内容通过 TextBlock 的 Opacity 分层，状态与来源说明保持可读。

**The Local Light Rule.** 天气光源跟随当前天气图标的位置，不铺满界面，不写进图标 PNG。

## Typography

默认字体来自 `FontFamily.XamlAutoFontFamily`，用户选字体后由 SurfaceWindow 的 ContentControl 统一继承 `Prefs.FontFamily`。只引用电脑已安装的字体，不添加网络字体。功能图标保留 Segoe Fluent Icons；天气图标来自独立位图图集。

frontmatter 中尺寸全部采用 DIP，字体名称保留原生来源表达。普通文字没有统一的自定义行高或字距，继续使用 WinUI 默认值。当前温度使用 display；城市用 headline；预报标题用 title；日期、温度与明细用 body；状态和标签用 label。天气文字另用 18 DIP，风速和 AQI 数值用 19 DIP / Semibold，来源说明用 11 DIP。胶囊歌名为 13 DIP / Semibold，歌手为 10 DIP / 0.82 opacity，天气温度为 15 DIP / Semibold。

## Layout

这是原生窗口布局，DIP 随显示器 DPI 转成物理像素；不套用网页断点。天气窗口目标为 480 × 720 DIP，高度按任务栏上方空间收缩，内容纵向滚动。当前窗口高度逻辑至少留 340 DIP，较小工作区与多显示器仍需实机核验。

天气正文四周使用 weather-inset；顶层 StackPanel 使用 section 间距。当前天气区三列依次为 68 DIP 图标、自动宽度温度和剩余空间说明，列距为 medium。逐时曲线与下方图标、时间、温度共用等宽列，最多抽取 8 个点覆盖传入的未来 24 小时；曲线画布高 84 DIP，图标 30 DIP。

首屏顺序是城市与状态、收藏城市、当前天气、24 小时预报、四天天气预览、风速与 US AQI。日预报默认 4 行，展开显示剩余天数；实际天数取接口结果。湿度和空气详情独立展开。删除一小时降水提示和当前降水概率。首屏完整呈现需在实际 DPI、字体和工作区高度下继续截图核验。

胶囊顺序固定为音乐左、天气右。标准天气宽 104 DIP；音乐默认宽 238 DIP，可设为 180–340 DIP。高度目标 34 DIP，任务栏过矮时随可用高度压缩。拥挤时两颗一起变为 Lite，音乐宽 48 DIP、天气宽 64 DIP；不通过默认隐藏其中一颗解决拥挤。

## Elevation & Depth

SurfaceWindow 使用 `DesktopAcrylicKind.Thin`。浅色 tint opacity 为 0.12，深色为 0.18；luminosity opacity 分别为 `0.65 + GlassOpacity × 0.30` 和 `0.30 + GlassOpacity × 0.45`。原生亚克力提供背景层次，天气区块没有自定义投影。

Root 从底到顶是独立 LightHost、GlassDither、Body。天气局部容器为 300 × 220 DIP，定位到当前天气图标中心；三层径向光尺寸分别为 150 × 132、220 × 170、190 × 160 DIP。每层通过六个渐变点衰减到透明。浅色三层 opacity 为 0.23 / 0.11 / 0.05，深色为 0.30 / 0.13 / 0.07。

柔光使用 Windows Composition，13 / 16 / 19 秒循环，每层只移动几个 DIP，颜色变化持续 1300 毫秒。浮窗关闭或系统、用户关闭动画时停止漂移。抖色保持静止，透明通道值为 3/255、图层 opacity 为 0.65，仅物理尺寸变化时重建，缓解渐变色阶。

音乐胶囊沿用封面色柔光，天气保持自己的局部色组。两者共用玻璃材质，不共用整页天气染色。

## Shapes

胶囊外形使用原生圆角区域裁剪，圆头随实际高度变化；内层表面采用 capsule-surface。收藏城市使用 city-chip，普通图标按钮使用 icon-button，封面使用 cover，温差条使用 temperature-bar。

原生窗口使用系统圆角偏好，不给未指定的 DWM 圆角捏造固定半径。Native.cs 隐藏标题栏并设置 DWM border color 为 NONE。天气 section 只保留顶部 1 DIP、低透明度灰色分隔，内侧顶部留 section 空间；不画四周框线。

## Components

`.impeccable/design.json` 的 HTML/CSS 只供文档面板预览，尺寸按 1 DIP = 1 CSS px 映射。原生 WinUI 状态与 Desktop Acrylic 仍以源码为准；sidecar 的八阶色带为浏览配色生成，未新增产品配色 token。

### Weather glyph

保留用户选定的第一版彩色立体造型。`WeatherGlyph.cs` 从 `Assets/weather-icons-clean.png` 的 4 × 4 图集中裁切：当前天气 68 DIP、逐时 30 DIP、日预报 24 DIP、胶囊 26 DIP。素材不加外发光。素材来源记录在 `work/LumiBar/Assets/weather-icons-provenance.md`，图像来源审计另行核对。

### Icon buttons and city chips

图标按钮透明、无描边，尺寸见 frontmatter，名称与 tooltip 同步提供。按下缩放到 0.97，140 毫秒回弹；禁用动画时保持静态。天气刷新按钮在更新中禁用。城市 chip 用浅青底表示当前城市，最多显示 6 个其他收藏城市，横向滚动承载溢出。输入框、菜单、搜索和 GPS 按钮沿用 WinUI 原生控件的 hover、focus、disabled 样式，不另造主题色。

### Hourly forecast

曲线节点、数据标签和下方分时单元中心对齐。曲线宽 1.8 DIP，节点直径 5 DIP、描边 1.4 DIP，曲线下方从低透明度青绿衰减到透明。空数据不绘图；单点只画节点和标签。

### Daily forecast and air summary

日期、图标、最低温、温差条、最高温依序排成五列；固定列宽为 70 / 24 / 44 / 44 DIP，中间温差条填充剩余宽度。风速和空气摘要各占一半，之间留 20 DIP 并放轻竖分隔。AQI 明确标注 US AQI，展开区标明美国 EPA AQI；缺数据时显示“暂无数据”，来源和时区置底。

### Capsules

音乐胶囊同时显示歌名和歌手，频谱融入信息背景。悬停或键盘焦点进入时出现上一首、播放 / 暂停、下一首，文字让出 80 DIP，胶囊外宽不变。Lite 隐藏音乐文字但保留封面与状态，天气保留图标和温度。控制按钮按播放器能力启用；暂停保留入口，媒体会话结束后淡出。

## Do's and Don'ts

### Do:
- **Do** 以玻璃概念 v2、最新天气 brief 和本机 WinUI 截图共同核对实现。
- **Do** 使用 DIP、系统主题和用户已安装字体，保留功能图标专用字体。
- **Do** 让天气柔光跟随图标位置，并检查滚动、明暗主题和关闭动画状态。
- **Do** 在实际 DPI 与可用高度下核对四天预览、风速和 US AQI 的首屏位置。

### Don't:
- **Don't** 添加一小时降水文案、当前降水概率、天气板块框或窗口白描边。
- **Don't** 给天气图标素材加晕光、用硬边色块或整页染色替代局部柔光。
- **Don't** 用隐藏一颗胶囊作为默认拥挤策略，或让悬停控制撑大胶囊。
- **Don't** 把文档抽取完成写成视觉审查通过或全软件商用验收完成。

## Candy accents — 0.7.44

外观页主题下方放一排九个渐变色块：冰糖蓝、薄荷绿、青柠糖、奶油黄、奶杏橙、蜜桃粉、莓果红、薰衣紫、绣球蓝。色块 28 DIP，外层选中框 34 DIP；单选勾、可读名称和系统键盘焦点共同标识选择。默认冰糖蓝，用户选色与明暗主题分别保存。

各色用三色渐变，位置 0 / 0.46 / 1；一般选中态、滑条、开关和降水图沿用同一组色值。颜色过渡 220ms EaseOut；关闭动画时直接落到目标色。小字和焦点色各有浅色、深色适配。音乐封面、天气氛围、预警级别与冷暖温度条继续使用各自颜色。

只更新现有画刷，不触发原生亚克力重建、字体遍历、音频设备重建或任务栏重新定位。WinRT 可能给同一个原生画刷建立多个托管包装；动画目标用原生依赖属性代次去重，避免重复注册同一颜色属性。

色值和插件字段详见 `outputs/Antigravity前端接入包/ACCENT_COLORS-0.7.44.md`。
