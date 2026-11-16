# LumiBar 0.7.47 交付检查

2026-11-15。合并此前尚未实机启用的 0.7.46 插件菜单能力，以及本轮天气城市栏和音乐封面调整。

## 最终改动

- 插件注册自定义托盘菜单和设置左侧入口。注册/全量更新/续期/撤销、点击事件读取和确认；子菜单、禁用、复选及分组。没有胶囊右键菜单。真实客户端 PID 校验，15 秒租期，进程退出/租期到期清理。插件保持运行且续期时，恢复内置胶囊不会撤掉插件菜单。
- 设置菜单点击与托盘点击都经过当前 revision 和 enabled/location 复核；不执行任意命令，不修改用户偏好。诊断剔除菜单 token。
- 天气刷新图形 15 DIP，保留 38 DIP 点击区域。删除右侧独立定位按钮；首个定位入口切换 Windows 定位城市，不加入收藏。收藏末尾紧接加号。新增只读 weather.locatedCity 会话缓存，诊断中剔除。
- 展开状态歌曲封面右移 2 DIP，Lite 保持原位。实心图标试看后，按用户最终意见恢复原有细线播放/暂停/切歌图标。
- 插件协作文档、contracts.d.ts 与菜单 SDK 示例更新；仅向 Antigravity 工作目录复制说明和 .cs.txt 示例，没有改其 C#/XAML 源码。

## 验证与证据

- 后端及真实命名管道 SDK：338/338，work/v47-tests.txt。覆盖菜单注册边界、权限、续期、事件顺序与确认、并发事件连接、慢操作中持续心跳、取消后用新连接撤销。
- 0.7.46 同轮菜单实机 CUA 检查：设置侧栏操作、复选更新、托盘嵌套/禁用状态、收到三次点击以及撤销后移除。work/plugin-menu-ui-data/menu-probe-final.json，received=3、unregistered=true、settingsUnchanged=true。
- 最终 0.7.47 原生设置同步 9/9：work/v47-native-sync-data/settings-sync-result.json。隐藏/离页控件不回写，重新打开读取当前参数，正常编辑保存。
- CUA 渲染核对：天气顶部布局、定位和添加入口；最终细线图标与展开胶囊封面。独立资料目录 work/v47-weather-visual 和 work/v47-outline-visual。图标最初的共享 Geometry 导致 WinUI 单所有者异常，仅出现在隔离预览；最终实心实现已删除，采用原有 FontIcon。所有预览均已关闭，捕获会话已释放。
- 安装 20/20：work/installer-qa-20261115-225332/results.json。全新默认值、主题资源、诊断导出、升级保留 JSON、卸载保留数据、既有快捷方式均通过。
- 实机启用：work/v47-live-verification.json。PID 61000，Explorer PID 15248；settings.json 哈希完全相同，偏好保留，程序 DLL 与发布目录一致。
- 实机 hello 验证 pluginMenus=true，pluginMenuLocations=tray,settings；menus.list 可读。

## 交付

outputs/安装包/LumiBar-0.7.47-windows-x64-setup.exe。
outputs/安装包/SHA256SUMS.txt。
outputs/Antigravity前端接入包/PLUGIN_MENUS-0.7.46.md。
outputs/Antigravity前端接入包/UI_POLISH-0.7.47.md。