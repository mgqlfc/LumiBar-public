# LumiBar 0.7.38

2026-11-15，Windows 11 x64。

## 修复

玻璃密度原先会改动七个 DesktopAcrylicController 的材质参数，触发系统材质过渡。新版保持原生磨砂材质稳定，在同一圆角和淡出层内调整中性密度覆盖层。密度滑条只刷新玻璃，不再联动歌曲信息、光效纹理和任务栏布局。取色浓度、饱和度和颜色选择规则保持原样。

独立测试中连续 27 次密度变化对应的材质替换、蒙版重建、纹理光栅化次数全部为 0。用户实测确认松手后不再闪烁。明暗切换、回到原来的 System / 60% 密度、关闭设置页均已实测。

本次还捕捉到一次 Explorer 的 Windows.UI.Xaml 崩溃。使用匹配的微软公开符号解析现有转储，调用链包含 `XamlDiagnostics::GiveRootsToCallback` 和 `Diagnostics::GetRuntimeElement`。新的 v5 原生任务栏组件在 Explorer 内保留诊断订阅，应用重启只续接映射和布局租约，避免每次重新枚举根节点。没有改变任务栏空间计算和窗口分层规则。

驻留组件在应用退出或进程异常结束时归还任务栏空间和窗口分层，保留无活动租约的轻量连接供下次续接；运行组件使用已校验的用户数据缓存副本，安装目录文件可正常更新与卸载。

## 验证

- 后端 248 / 248：`work/v38-tests.txt`。
- 四轮正常退出 / 重启：每轮 reserved、error=0，Explorer PID 一直为 15248。`work/broker-v38-results.json`。
- 一次测试实例异常结束：租约自动释放，正式目录实例 PID 30828 随后接管，Explorer 仍为 15248。`work/v38-crash-recovery.json`。
- 安装 / 启动 / 诊断导出 / 升级 / 卸载 / 数据保留：13 / 13。`work/installer-qa-20261115-141743/results.json`。
- 空闲采样期间私有内存、句柄保持稳定；保留 `work/v38-idle-samples.json` 供后续性能对比。
- 当前实际运行路径为 `outputs/LumiBar-实机版/LumiBar.exe`，使用正常启动参数，没有开启测试统计。

## 交付

`outputs/安装包/LumiBar-0.7.38-windows-x64-setup.exe`，131052516 字节。
继续使用未签名安装包。校验值记录在同目录 `SHA256SUMS.txt`。
本地转储、符号文件、测试配置和诊断统计均不在发布包内。
