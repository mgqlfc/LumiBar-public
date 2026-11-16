# LumiBar 0.7.48 垂直位置微调

2026-11-16。两个胶囊在任务栏内统一下移 2 DIP，Full 和 Lite 共用。SlotPlanner 在任务栏可用高度内夹取偏移，临时上方保护槽位保持原位。windows.hints/windows.planCapsules 与真实槽位使用相同偏移，外部渲染器不必自行叠加。

没有修改 CapsuleWindow 内部图文布局，保留 0.7.47 封面独立右移 2 DIP、标题与歌手名原位的方案。

验证：已有后端 338/338，work/v48-tests.txt。实机缩放 1.65625 下，天气 slot top 从 1532 到 1535，HWND top 从 1515 到 1518；左右位置、宽高完全相同，图形仍在任务栏内。音乐当前无会话，没有主动打开播放器；音乐和天气经过同一 SlotPlanner top 计算。

work/v48-alignment-before.json、v48-alignment-after.json、v48-alignment-verification.json 保存前后坐标。work/v48-live-verification.json 验证实机版本 0.7.48 PID 89400，偏好与配置文件哈希保持，Explorer PID 74088 未在更新期间变化，程序匹配发布目录。

已生成 outputs/安装包/LumiBar-0.7.48-windows-x64-setup.exe，SHA256SUMS.txt 已更新。此轮只改坐标，未重复上一版的完整安装/升级/卸载检查。没有新增测试用例。

CUA 仅列出当前可用窗口；任务栏未出现在可选列表中，本轮采用程序提供的真实槽位与 HWND 坐标核对，没有声称完成搜索框截图对比。捕获会话已结束并 reset。