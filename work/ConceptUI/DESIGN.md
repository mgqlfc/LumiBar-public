---
name: LumiBar 独立交互原型
description: 通透玻璃、青色控件与封面色光的 Windows 交互原型
colors:
  accent-ui-light: "#6CE7DA"
  accent-ui-mid: "#21BEC2"
  accent-control-light: "#70E8DA"
  accent-control-mid: "#23BEC2"
  accent-deep: "#118FB1"
  teal-data: "#26979E"
  rain-area-base: "#22B5BE"
  temperature-low: "#4799FF"
  temperature-mid: "#A497DA"
  temperature-high: "#FFA54F"
  light-text: "#1C2024"
  light-secondary: "#60646C"
  dark-text: "#EDEEF0"
  dark-secondary: "#B0B4BA"
  light-card: "rgba(252,252,253,0.4392)"
  dark-card: "rgba(33,34,37,0.4392)"
  light-control: "rgba(249,249,251,0.6588)"
  dark-control: "rgba(48,52,58,0.5020)"
  glass-light: "#E5EFFE"
  glass-dark: "#1E2B44"
  weather-fog-primary: "#8EB8CF"
  weather-fog-secondary: "#97BFD0"
  weather-haze-primary: "#B4A47E"
  weather-haze-secondary: "#97A2A5"
  weather-snow-primary: "#7DD3EE"
  weather-snow-secondary: "#ABB8EA"
  weather-dust-primary: "#E0A957"
  weather-dust-secondary: "#B77D45"
typography:
  display:
    fontSize: "52px"
  lyric:
    fontSize: "32px"
    fontWeight: 600
  headline:
    fontSize: "28px"
    fontWeight: 600
  title:
    fontSize: "16px"
    fontWeight: 600
  body:
    fontSize: "14px"
  label:
    fontSize: "12px"
rounded:
  control: "9px"
  icon-button: "10px"
  panel: "14px"
  rain: "16px"
  capsule: "17px"
  capsule-clip: "17px"
  hover-control: "18px"
  preview: "20px"
spacing:
  tight: "4px"
  small: "6px"
  capsule-gutter: "10px"
  weather-gap: "8px"
  regular: "10px"
  column: "12px"
  panel: "18px"
  settings: "22px"
  lyric: "32px"
components:
  button:
    backgroundColor: "{colors.light-control}"
    textColor: "{colors.light-text}"
    rounded: "{rounded.control}"
    padding: "8px 14px"
  icon-button:
    rounded: "{rounded.icon-button}"
    width: "38px"
    height: "38px"
    padding: "0px"
  navigation-selected:
    rounded: "{rounded.control}"
    height: "42px"
    padding: "0px 8px 0px 12px"
  theme-selected:
    rounded: "{rounded.panel}"
    padding: "4px 6px"
  text-field:
    rounded: "{rounded.control}"
  rain-card:
    rounded: "{rounded.rain}"
    padding: "10px 14px"
  city-chip:
    rounded: "{rounded.rain}"
    padding: "5px 10px"
  lite-hover-control:
    width: "36px"
    height: "36px"
    rounded: "{rounded.hover-control}"
    padding: "0px"
  preview-card:
    rounded: "{rounded.preview}"
    height: "82px"
    padding: "10px"
---

# Design System: LumiBar 独立交互原型

## Overview

**Creative North Star: "把光留在玻璃之间"**

沿用用户指定概念图的通透玻璃、低对比边缘和青色控件。彩色立体天气图标承担识别，音乐封面的暖色与紫色光晕营造气氛，正文与操作保持清楚。

天气紧凑可扫读，音乐把空间留给歌词，设置保留熟悉的左侧导航与右侧表单。独立原型用于调整 UI/UX；天气、降水、歌曲、歌词和频谱均使用演示内容。

**Key Characteristics:**

- 通透玻璃与柔和封面色光。
- 青色用于选中、开关、滑块和数据曲线。
- 天气紧凑、歌词突出、设置分组清晰。

本记录来自当前 C#/XAML 源码与概念图，属于实现提取。用户最新澄清：暗色切口主要来自移动光效漏边，要求撤回非必要的阴影、反光样式变化。v48 延续统一几何、10 DIP 透明留白、v13 阴影反光参数与 8 DIP 光纹理扩边，并加入独立中文/西文字体及溢出歌名滚动。此前黑角已消失。本轮 CUA 检查已确认 Wide Latin 字形实际应用，正常天气胶囊完整显示 −20° 与 100%，Lite 完整显示 −20°；暗色沙尘与音乐运动帧已留档，覆盖日志全部为 coverageSafe=True。v24 已验证中文 STKaiti 与西文 Wide Latin 独立生效、单独重置西文保持中文，以及正常/Lite 极值完整显示。v29 的 Canvas 跑马灯已完成基础滚动验证；v32 截图记录天气默认页完整显示及音乐控制分区。v41 单排天气速览头部已实机验证，音乐胶囊悬停播放控件已按用户要求移除。v45 已实测天气详情居中三栏、完整音乐专辑行及歌词同步。v47 CUA 已确认 Lite 速览可播放/暂停、下一首切歌与封面更新，鼠标停留约一分钟时浮窗保持，普通模式无控件。v48 将退场恢复限制在190ms截止时间内，源码已核对。原型仍未接入正式程序。胶囊的轻微悬浮感参考系统任务栏搜索按钮。北极星与色彩名称是基于既定方向的记录用语。

## Colors

### Primary

重点色采用从左上到右下的青绿高光渐变，停点为 0、0.46、1。Ui.AccentBrush 使用 accent-ui-light、accent-ui-mid、accent-deep，覆盖降水曲线、风速/AQI 图形及速览小图标。GlassTokens.xaml 的滑块、开关和高亮资源使用 accent-control-light、accent-control-mid、accent-deep。两组高光与中间色略有差异，按源码分别记录。teal-data 保留 Ui.Teal 的基础青色，rain-area-base 用于降水面积的透明度渐变。

温度曲线与预报温度条改用 TemperaturePalette：低温蓝、中间紫、高温橙，归一化值 0/0.5/1 分别对应 temperature-low/mid/high。Range 在区间内生成 9 个渐变停点；24 小时曲线纵向高橙低蓝，10 天温差条按该预报区间的位置取色。降水仍保留青绿。

### Secondary

天气色光由 WeatherAtmosphere 按天气状态选择双颜色：雾采用冷灰蓝，雾霾采用灰金与灰青，雪采用冰蓝与浅紫，沙尘采用暖金与棕色。对应 token 使用 weather-fog、weather-haze、weather-snow、weather-dust 前缀。晴天、雨天、阴天、雷雨与夜间继续使用源码中的独立双颜色组合。天气浮窗、速览与天气胶囊共用这套状态配色。

### Neutral

浅色正文为近黑灰，次级文字为中灰；深色正文接近白，次级文字偏冷灰。浅色与深色卡片都保留透明度。玻璃浅色偏冰蓝，深色偏蓝灰。选中导航与分段按钮都使用 Ui.AccentBrush 渐变，alpha 分别为 60/255 与 65/255。

XAML 的八位颜色采用 AARRGGBB；本文件转换成 CSS rgba，避免浏览器误读透明度。完整主题边框与按压资源以 GlassTokens.xaml 为准，未将所有平台资源复制成新 token。

## Typography

中文与西文字体分别选择、分别恢复系统默认，默认均为 WinUI 的 XamlAutoFontFamily。Typography 按 Unicode Rune 和源码区段规则将文字分组：单一组显式设置 TextBlock.FontFamily，混合组使用具有明确 FontFamily 的 Run，避免中文字体包含西文字形时覆盖西文选择。Ui.Text 创建的文本带 Managed 依赖属性标记；文字变化和 Loaded 时重新分段，Updating 标记防止递归。设置应用后遍历原生视觉树更新已挂载的标记文本，再重测布局。图标使用 Segoe Fluent Icons。没有自带品牌字体或独立字距规则。

天气温度使用 display；歌词使用 lyric，原型启动字号为 32，设置允许 18–36；设置页标题使用 headline；分组标题使用 title；正文与按钮使用 body；状态、图表标注采用 10–13 的较小字号。文字大小均按 WinUI DIP 解释；frontmatter 用 px 供设计面板展示。

音乐当前歌词不透明度为 1，其余行降至 0.44。歌手保持单行省略，歌词允许换行。共享音乐胶囊的溢出歌名按动画设置滚动或省略；完整音乐浮窗底部歌名仍单行省略。正文与歌词行高沿用平台默认。天气胶囊温度明确使用 20 的行高，降水概率使用 16 的行高，并采用 BlockLineHeight。设置滑块右侧的数值/百分比列改为 Auto，文字 MinWidth=45 且 NoWrap，随宽字体增长并保持单行。

## Layout

窗口基准：天气宽 480，音乐 420×650，设置 560×650 DIP。天气打开时以 436 宽、无限高测量默认内容，PreferredHeight 取 ceil(DesiredSize.Height+34)，使默认内容一次显示；保留 ScrollViewer 容纳展开或异常内容。没有 Web 断点；窗口坐标依屏幕宽度限制，像素尺寸按显示缩放换算。设置内容可纵向滚动。

天气内边距为左右 22、上下 16，纵向内容间隔 8。当前天气图标为 68；降水卡片左列 144、列间距 12，右列弹性容纳图表。逐时曲线位于降水下方，日预报默认显示四行，按“展开”显示其余六行；底部按湿度、风速风向、空气 AQI 三栏排列，等宽列间距 14。标题行高 19、数值行高 30，行间距 4；三栏标题与数值组全部居中，数值统一 17 号半粗。风速单位与数字同字号，风向独立为 11 号次级文字，与数值间隔 4、下移 3。日预报五列为 82/30/90/弹性/36，列间距 10，使温差条靠右并缩短。删除湿度空气的展开入口与底部说明。

音乐内边距为 22/14/22/16（左/上/右/下），页头、可滚动歌词和底部控制分三行。歌词间隔 32，歌词容器留上方 72 与下方 100 的滚动空间。封面为 58×58；歌名 19 号半粗、歌手 13 号（不透明度0.8），歌手下方新增专辑 11 号（不透明度0.65），三行间距 4，均单行省略。RenderTrack 随切歌更新专辑；空会话清空专辑，缺失专辑显示“未知专辑”。底部进度条与时间并排。

设置顶部标题栏高 54，左导航宽 170；右侧滚动容器内边距为 22/24/24/22，分组间距 22。效果预览高 82。默认打开外观页，其他导航为常规、任务栏、天气、音乐与歌词、插件。

双胶囊的可见内容高 34；默认音乐宽 238、天气最小宽 104、间距 8。音乐宽度可调至 180–340，间距可调至 4–24；Lite 音乐宽 48、天气最小宽 64。胶囊在独立位置演示。CapsuleGeometry 统一可见高度 34、半径 17、外留白 10 DIP，外窗名义尺寸为可见宽度 + 20、高 54。正常音乐外窗名义宽 258、天气外窗最小名义宽 124；Lite 音乐外窗名义宽 68、天气外窗最小名义宽 84。天气宽度依当前内容测量，可继续增宽。实现先将外窗尺寸乘显示缩放并 Math.Round，再反推 Root 的 DIP 尺寸；Root 禁用布局取整。Body、LightHost、表面与噪点层保持高 34、左右边距 10、垂直居中，并关闭各自布局取整，使玻璃、内容和栅格边缘沿用同一个中心。实际外框尺寸随物理像素取整略有变化。

## Elevation & Depth

使用 DesktopAcrylicController 的 Thin 材质、低对比边线、噪点与柔和封面色光。普通卡片维持轻薄层次。胶囊新增独立的 CapsuleEdge 栅格层，表达接近系统任务栏搜索按钮的轻微悬浮感；完整浮窗继续使用 Windows 窗口轮廓。

胶囊边缘按物理尺寸与主题缓存，每个像素采用 4×4 子采样，再平均写入预乘 BGRA。距离场来自 CapsuleGeometry，保留统一物理几何与分数 DPI 精度。边缘样式恢复 v13 参数：阴影向下偏移 1.3 DIP、sigma 2.3 DIP，浅/深色强度分别为 0.16/0.22，外缘因子为 clamp(d + 0.5, 0, 1)；反光中心位于轮廓内侧 0.7 DIP、宽度参数 0.65 DIP，浅/深色强度分别为 0.22/0.13，纵向权重为 1-y/height，内缘因子为 clamp(0.5-d, 0, 1)。10 DIP 外留白继续容纳阴影；此次修正聚焦移动光纹理覆盖，不再扩展边缘样式。sidecar 的 CSS 阴影只表达形态。

浅色玻璃 tint opacity 为 0.38，深色为 0.24。亮度透明度公式分别为 0.65 + GlassOpacity × 0.3 与 0.3 + GlassOpacity × 0.45；原型 GlassOpacity 初值 0.62，可调 0.35–0.95。浅色 fallback 为 #F7F9FD，深色为 #181B23。原型着色浓度初值 0.4，可调 0.08–0.55。

窗口进入时淡入 300ms、向上移动 14 DIP 并在 380ms 内结束，缓动为 cubic-bezier(0.16,1,0.3,1)。图标按钮按下缩至 0.97，140ms 恢复。封面与天气色光共用 SoftLightTexture：双精度计算高斯衰减 exp(-3.6 × (dx² + dy²))，以预乘 BGRA 写入带抖色的纹理。尺寸按 DPI 换算，单边上限 1800；仅在颜色、几何或 DPI 签名改变后异步重建，版本号丢弃过期结果。SoftLightTexture 使用四边 -8 DIP margin 扩展纹理。Rasterize 按宿主原宽高 fieldWidth/fieldHeight 与实际 padX/padY 计算坐标，延伸原光场，保留原来的光斑尺度；移动只作用于纹理，宿主裁剪轮廓保持固定。启用 --inspect-ui 时，覆盖日志逐轴检查实际半边余量是否至少为 abs(drift)+1 DIP；本轮留存日志的 coverageSafe 全部为 True。新旧纹理交叉淡入 800ms，850ms 后清理旧图。Composition 默认用 15 秒循环缓动把纹理移动到 (3,-2) DIP 再归零；雾与雾霾改为 22 秒、(3,0) DIP，沙尘改为 10 秒、(5,-2) DIP。缓动统一为 cubic-bezier(0.42,0,0.58,1)。柔光同时尊重应用动态光效和系统动画开关；失活时停止位移。频谱仍使用演示相位。

## Shapes

控件小圆角，内容容器中圆角，胶囊两端圆润。封面圆角分别为音乐窗 11、设置预览 9、胶囊 7。胶囊的玻璃与内容分别处理圆角：GlassBackdrop 在物理尺寸的 4 倍空间绘制白色覆盖遮罩，左右内缩 10 DIP，使用高 34 DIP、垂直居中的矩形和 17 DIP 圆角。遮罩表面刷明确使用 Stretch.Fill 和 Linear 插值，再缩回窗口尺寸；Body、LightHost 与噪点层各使用同中心的 17 DIP 几何裁剪。外层 CapsuleEdge 保留完整阴影空间。Native.RoundCapsule 清除窗口区域裁剪并关闭 DWM 圆角，透明轮廓交给组合器。

CapsuleTransparency 清理 Win32 重定向客户区，并使用 CapsuleGeometry 的同一距离场处理 WM_NCHITTEST：17 DIP 半径轮廓以外返回 HTTRANSPARENT，使透明留白和阴影区域鼠标穿透。完整浮窗仍使用系统圆角。此处记录源码策略，本轮检查覆盖当前显示设置下的暗色沙尘、音乐运动帧和宽字体数值；未记录多 DPI 实机测试或 FPS 测量。

主题切换的 Acrylic 源刷通过 BrushTarget 独立接收，再交给组合遮罩；ApplyMask 始终从独立 _source 取源，避免将已套遮罩的刷重新作为源。本轮已反馈主题往返切换测试成功；这项结果单独记录，v22 的宽字体检查已确认正常版 −20°/100% 与 Lite −20° 完整显示。

## Components

### Buttons and fields

默认按钮使用平台主题资源与统一圆角、内边距、字号。图标按钮透明底、无边框，默认尺寸 38×38，具有 tooltip 和自动化名称。完整音乐浮窗的播放按钮为 42×42 的圆形。完整音乐浮窗与 Lite 音乐速览提供播放控制；普通音乐速览与胶囊本体没有播放控件。悬停、焦点与禁用使用 WinUI 模板，源码仅添加按压缩放。

TextBox 最小高度 38；ComboBox 最小高度 36。城市搜索按 Enter 或“搜索”得到示例城市，至少输入两个字符；GPS 按钮显示原型提示。“中文字体”和“西文字体”各有独立 AutoSuggestBox 与“恢复系统默认”。按本机字体显示名或 Family 搜索，最多显示 80 个候选；选择候选即应用。Enter 按 Family 或别名精确匹配，不区分大小写，必要时刷新字体列表再匹配。成功后调用 Save、关闭候选列表并重排胶囊。重置仅清除对应字体偏好与输入框，保留另一项。Loaded 只加载字体列表，不因已有选中值自动弹出候选。底部混排示例为“微光 LumiBar · −20° · 100%”。

### Navigation and theme segments

导航高 42，选中项用低透明度青绿渐变，点击后替换右侧表单。主题分段为跟随系统、浅色、深色，点击立即改变颜色并调用 Save。启动时天气和设置为浅色，音乐单独覆写为深色以贴近概念图。Save 仅在主题偏好值变化后统一应用主题；其他设置保持启动时的混合主题。

### Weather and rain

城市胶囊打开搜索，添加按钮展开城市输入，更多菜单含固定/取消固定、设置与关闭。天气刷新重建示例。降水卡片左侧显示标题与示例提示，右侧为青色曲线和淡渐变面积，纵轴单位 mm、横轴现在/30 分钟/60 分钟；空趋势显示暂无数据。曲线按 15 分钟示例区间绘制。天气详情的最大当前温度显示 °C/°F；体感、逐时、日预报与任务栏胶囊保持 °。

### Music and capsules

播放/暂停切换演示进度；上一首与下一首均在两首示例间切换。拖动进度条或键盘调整后更新歌词。默认聚焦前一句、当前句、后一句；首尾仅显示实际存在的行。更多菜单可切换完整歌词，完整模式在布局就绪后把高亮行滚到歌词区约 36% 高度处，布局未就绪时等待下一次更新。无媒体会话时隐藏音乐胶囊、禁用音乐控制并显示空态。

音乐胶囊常态保留封面、歌曲名、歌手与背景频谱，Lite 隐藏名称与频谱。音乐胶囊与设置效果预览复用 MusicCapsuleContent；预览封面 40×40，胶囊封面 28×28。按用户要求，胶囊和共享预览已删除上一首/播放/下一首控件及悬停预留逻辑。点击音乐区开关完整音乐浮窗；播放操作可在完整浮窗或 Lite 音乐速览内完成。正常与 Lite 音乐胶囊悬停均显示音乐速览。

共享音乐胶囊（含设置效果预览）采用 MarqueeTitle：离屏 Canvas 放置带左右各 8 DIP 余量的自然宽文字，CompositionVisualSurface 捕获后通过 SurfaceBrush 与 MaskBrush 输出。遮罩停点为 0透明、5%白、95%白、100%透明，两端渐隐。输出宽度为 max(1, Width-Reserved)，文字位移由 Progress×max(0,Natural-有效宽度) 表达式驱动，外层保持固定布局并裁剪。

Progress 在溢出超过 1 DIP 时循环，首尾各停 1.8 秒，单程时间 max(1,overflow/22) 秒，位移缓动 cubic-bezier(0.25,0.12,0.75,0.88)。MarqueeTitle 源码仍保留 SetReserved 接口，但 MusicCapsuleContent 已无调用，最终胶囊使用 Reserved=0；悬停不再缩窄歌名或显示播放控制。歌手左边距 8，与歌名捕获表面的左侧余量对齐。

UpdateRowWidth 继续用实际胶囊宽减去按钮左右 Padding 设置内容宽度。短歌名静止、Lite 禁用；应用或系统关闭动画时省略。文字、容器宽度、中西文字体或动画设置变化后重算；隐藏与卸载停止 Progress 并归零，重新显示后重算。v45 完整音乐窗口与专辑行已实测；v41 天气速览单排头部已实测。v47 Lite 播放、下一首、指针停留保持与普通版无控件已完成 CUA；跨胶囊间隙的纯鼠标移动与精确650ms时序未实测。

天气胶囊在常规与 Lite 模式下都跟随天气色光。胶囊专用双光斑中心为 (0.22,0.65)/(0.86,0.3)，半径为 (0.54,1.2)/(0.48,1.1)，强度为主题基础值的 1.15/0.85；主题基础值浅色 0.3、深色 0.4。这组扁长布局让色光铺在胶囊内部，并沿用天气对应的慢位移。

天气正常胶囊最小宽 104：24 的天气图标和 15 号温度作为主组，14 的降水图标和 11 号概率组成次组；主组间隔 4、次组间隔 2，次组左边距 4。Lite 最小宽 64，只保留天气和温度，图标缩至 22、间隔 3。音乐 Lite 将名称列宽与列间距归零，使封面在 48 宽胶囊内居中。

天气胶囊通过 PreferredWeatherWidth 测量继承后的实际字体与当前数值：先 InvalidateMeasure，再以无限宽、34 高测量 _weatherRow，宽度取 max(当前模式最小宽, ceil(DesiredSize.Width + 12))。左右各 6 DIP 为图标与弧形末端留空间。温度和概率都禁止换行；字体 Save、天气选择与测试数值变化后重新布局。本轮 CUA 使用 Wide Latin 实测：正常版 −20°、100% 完整显示且字形可见为超宽字体；Lite 的 −20° 也完整显示。

### Lyric timing

完整音乐浮窗三点菜单新增“歌词同步”：提前/延后各 0.5 秒、输入偏移秒数、恢复原始时间。精确输入浮层宽 224，NumberBox 范围 −60 至 60 秒、SmallChange=0.5、LargeChange=5，即时更新；保存前取两位小数，另有“归零”。正数提前、负数延后，仅调整歌词选行计算，不跳转歌曲进度；菜单显示当前偏移。浮层打开时暂时固定音乐窗，关闭后恢复原固定状态。v45 实测偏移 30 秒时歌曲位置仍为 84 秒，高亮歌词改变；归零后恢复。

### Hover previews

天气与音乐胶囊在常规、Lite 模式下都支持速览；音乐需要当前有示例歌曲。鼠标进入胶囊后等待 320ms，显示时不激活窗口。所有速览可接收鼠标：离开胶囊后等待 650ms，移入速览则取消关闭；离开两者后再等待 650ms，随后用 180ms 淡出。淡出过程中返回胶囊或速览会恢复显示。v48 使用 Environment.TickCount64+190 的截止时间界定恢复窗口；关闭动画时截止时间为0，恢复或主动关闭时清零。超过退场窗口后重新进入仍等待320ms，避免隐藏后永久跳过首次延时。按下胶囊可主动关闭速览。模式切换时已打开速览按新模式重新渲染尺寸。速览位于胶囊外窗上方 8 DIP（距离可见胶囊顶边约 18 DIP），横向位置限制在屏幕范围内。胶囊原生 Tooltip 已移除，避免与速览叠加。

天气速览为 264×160 DIP，普通音乐速览为 300×124 DIP，Lite 音乐速览为 300×164 DIP，内边距均为 16。速览继承对应胶囊主题。天气顶部高 40、列间距 8，单排依次为天气图标 30、城市 15 号半粗、状况 11 号次级文字、当前温度 24 号半粗；整排居中，城市最多单行、最大宽 68。当前温度明确显示 °C/°F，体感为 22 号半粗且仅保留 °。

下方“体感 / 降水 / 空气”三列等宽，列间距 10；各列标题、数字、注释全部居中，统一行高 18/31/17，行间距 3。数字 22 号半粗、LineHeight=29、NoWrap；注释 10 号、最多一行并可省略，降水注明“未来 1 小时”，空气注明 AQI。这些均为原型示例数据；当前晴天概率为 35%，雨雪等代码分支为 85%，没有天气服务请求。

两种音乐速览共用信息区：封面 62×62、圆角 12，文字组垂直居中，歌名 16 号半粗、最多两行，歌手 12 号、最多一行，专辑 11 号、最多一行；没有专辑时显示“未知专辑”。不显示播放状态行。普通版保持无控件；Lite 版在底部居中增加上一首、播放/暂停、下一首，控件均为 36×36、圆角 18，间隔 12，信息与控件行间距 8。播放键使用淡紫底，图标与自动化名称随播放状态更新；这组按钮不显示原生 Tooltip。天气速览跟随天气色光与动态效果；音乐速览复用封面氛围色光，跟随封面着色和动态光效设置。

点击打开的完整浮窗默认不固定，失活后等待 80ms 检查前台窗口；外部点击触发 180ms 淡出，190ms 后隐藏。关闭动画时直接隐藏。新的显示请求递增可见性版本并恢复不透明度，取消尚未结束的退场；天气菜单仍可切换固定浮窗。

### Prototype launcher

控制台尺寸 660×150 DIP，内边距 12、行间距 8，两行按钮内间距 6。内容保留操作按钮与天气下拉选择；标题栏注明“交互原型控制台 · 示例数据”。第一行打开天气、音乐、设置或切换天气、双 Lite、关闭/恢复音乐；第二行提供“天气悬停速览”“音乐悬停速览”与宽 116 的天气 ComboBox。天气列表为晴、多云、阴、雨、雾、雾霾、雪、沙尘暴、雷雨、夜间；“切换天气”按相同顺序循环。雾霾与沙尘暴使用原型内部代码 -1/-2，不使用 WMO 编码；示例 AQI 分别为 168/235，其余场景为 83。所有选择只更新原型示例。第二行另有“−20° / 100%”测试按钮，在温度 −20°、体感 −27°、降水 100% 与温度 12°、体感 11°、降水 35% 间切换，同时刷新浮窗、胶囊、速览和布局。另有“长歌名”按钮，在“微光”和“微光漫过漫长的夜晚 — A Little Light Across the Endless Night”之间切换，用于检查共享音乐胶囊与设置预览的溢出表现。点击速览演示立即显示，5 秒后检查指针：鼠标不在胶囊或速览内才收起，保留当前常规/Lite 模式。控制台已移除播放控件测试入口。关闭控制台退出整个原型。

### Recorded checks

本轮实际 CUA 验证记录：Wide Latin 显式应用于温度与降水文字，正常版和 Lite 都完成宽字体极值检查；暗色沙尘与音乐各保留两帧运动截图，覆盖日志全部为 coverageSafe=True。证据文件名为 .impeccable/review/weather-widefont-v22.png、 .impeccable/review/weather-widefont-lite-v22.png、 .impeccable/review/weather-motion-v22-a.png、 .impeccable/review/weather-motion-v22-b.png、 .impeccable/review/music-motion-v22-a.png、 .impeccable/review/music-motion-v22-b.png。上述文件位于 .impeccable/review/。v24 另有 .impeccable/review/fonts-v24.png、fonts-reset-v24.png、weather-fonts-v24.png、weather-fonts-lite-v24.png：前两张记录 STKaiti/Wide Latin 独立应用及单独重置西文，后两张记录正常/Lite 极值完整显示。v25 修正已选字体在 Loaded 后自动展开候选；v29 修正 Canvas 承载、固定视口与按钮内容宽度后通过视觉检查，证据为 .impeccable/review/marquee-v29-a.png、marquee-v29-b.png、marquee-v29-off.png、marquee-v29-short.png，分别显示起点、英文尾部、关闭动态后的省略号及短中文歌名。v26–v28 的空白问题未通过检查；本次跑马灯通过记录对应 v29。v30 隐藏停滚按源码核对。v32 的 .impeccable/review/weather-v32.png 记录默认天气全内容显示，.impeccable/review/marquee-controls-v32.png 记录歌名与控制分区。v38 的 .impeccable/review/weather-hover-v38.png 与 music-hover-full-v38.png 已查看，记录三列居中与正常音乐速览；天气顶部随后改成单排，.impeccable/review/weather-hover-v41.png 已实机验证并查看。v45 的 .impeccable/review/weather-v45.png、music-v45.png、lyrics-offset-v45.png 与 music-hover-lite-v45.png 已实测并查看：前三张记录天气完整页、音乐专辑行和偏移30秒时进度仍为1:24；最后一张是旧版无控件 Lite 速览。v45 归零恢复由本轮 CUA 记录确认。v47 的 .impeccable/review/music-hover-lite-v47.png 与 music-hover-normal-v47.png 已查看；本轮 CUA 实际点击 Lite 播放后变为暂停，鼠标停留约一分钟浮窗保持，点击下一首成功更换歌曲与封面且浮窗保持，切普通模式后无控件。工具不支持从胶囊跨越8 DIP缝隙的纯鼠标移动，650ms宽限仅按源码记录，未作精确时序测试。v48仅将RecoverableFade改为TickCount64截止时间，防止关闭后永久跳过320ms入场延时；源码已核对，新构建流程另行记录。v32 控制分区属于旧行为，用户已取消胶囊播放控件。验证范围为当前显示设置；未执行实际多 DPI 测试，也未测量 FPS。

### Settings preview and switches

玻璃密度、封面着色、动态光效、着色浓度、字体等控件即时更新原型。效果预览内边距 10、列间距 6，容器按圆角 20 裁剪；音乐内容与封面各自裁剪。预览包含共用音乐胶囊、演示频谱与天气按钮，音乐区保留渐隐歌名与点击开关音乐窗，天气按钮可打开天气窗。频谱初始随暂停状态静止，播放后演示动画更新。登录启动、关闭留托盘与插件项用于界面演示；设置只留在原型进程。

Sidecar 的 HTML/CSS 是原生组件的独立面板示意。它保留形状、配色与状态意图；浏览器不能复现 Windows 桌面 Acrylic 与 WinUI 模板的全部细节。sidecar 中合成的 tonalRamp 仅用于色阶展示，源码不使用这些色阶。

## Do's and Don'ts

### Do

- 沿用 concept-reference.png 的组成与比例。
- 保持未来一小时降水的左右布局，并保留示例数据提示。
- 保持歌曲名与歌手可见；溢出歌名依动画设置滚动或省略，歌手保持单行省略。
- 正常与 Lite 音乐胶囊都提供速览；普通速览无控件，Lite速览与完整音乐浮窗提供播放操作。
- 沿用原生控件的焦点、禁用和键盘行为。

### Don't

- 不要把演示天气、歌词或频谱写成实时数据。
- 不要把主工程的后端能力写成原型已接入的能力。
- 不要用新增卡片边距挤走天气首屏信息。
- 不要为每个信息组增加厚重外框或投影。
- 不要用面板 HTML 示意代替原生窗口的视觉验收。
