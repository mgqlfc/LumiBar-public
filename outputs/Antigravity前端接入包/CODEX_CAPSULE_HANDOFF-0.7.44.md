# Codex → Antigravity：胶囊不显示的直接线索

2026-11-15，LumiBar 实机 0.7.44。用户已说明你正在排查；Codex 仅做只读核对和文档交接，没有改动你的前端源码、启动/退出进程或申请/释放租约。

## 先核对这一处：slot.rect 没有 visible

19:58:35 的真实 `windows.reservation` 响应：后端 PID 86392，mode=builtin，owner=external，reservation.status=reserved；租约 phase=active，前端 PID 89412，rendererId=antigravity.lumibar-glass，expiresInMs=4844，slotsVersion=27。两个槽位均为 full。

```json
{
  "musicSlot": {
    "rect": {"left":1296,"top":1532,"right":1691,"bottom":1589},
    "mode":"full"
  },
  "weatherSlot": {
    "rect": {"left":1698,"top":1532,"right":1871,"bottom":1589},
    "mode":"full"
  },
  "state":"full"
}
```

后端 `Backend/TaskbarSpace.cs` 定义 `ScreenRect(int Left,int Top,int Right,int Bottom)`，没有 Visible。`Backend/Placement.cs` 中 CapsuleSlot.Rect 使用 ScreenRect。交接契约也明确是：

```ts
interface CapsuleSlot {
  rect: Omit<Rect, 'visible'> | null;
  mode: 'full' | 'lite' | 'hidden';
}
```

当前前端源码 `Windows/TaskbarCapsuleWindow.xaml.cs` 的 ApplySlot 隐藏条件却含 `!slot.Rect.Visible`。`Sdk/Contracts.cs` 的 RectDto.Visible 为普通 bool，未设初值。对上面这个真实 JSON 反序列化后，它自然为 false，导致两个有效槽位都走 HideImmediate，后续 PlaceTaskbarCapsule(show:true) 根本不会执行。这条判断本身已足以触发不显示。

建议把槽位矩形与 taskbar/anchor 矩形分成独立 DTO。slot 的可显示条件来自 active 租约、external 归属、featureEnabled、整体 state、slot.mode、rect 非空以及正宽高；不要要求 slot.rect.visible。taskbar.visible / musicAnchor.visible / weatherAnchor.visible 保持原有含义，不要全局强行改成 true。隐藏的内置锚点也不能当作外部胶囊可见性依据。

修改后用同一类真实响应做反序列化 + 显示判定检查；再核对实际 HWND 可见、RootGrid.IsLoaded、ActualWidth/ActualHeight、客户区尺寸、DPI 和最终屏幕绘制。active 租约证明注册/激活成功，前端显示仍由 ApplySlot 路径决定。

## 核对范围与协作方式

本次只读证据另存于 `C:\Users\ROG\Documents\Codex\2026-11-14\ban\work\renderer-handoff-audit.json`，没有 leaseToken 或歌曲/城市内容。以上是当时状态快照，后续调试请重新读取。

20:06:41 已用当前 `Sdk/Contracts.cs` 提取的 RectDto 和上述真实响应完成 C# 反序列化复现，两个槽位结果相同：hasVisibleField=false、decodedVisible=false、positiveSize=true、oldGateAllows=false、modeBasedGateAllows=true。复现脚本为 `C:\Users\ROG\Documents\Codex\2026-11-14\ban\work\verify-slot-contract.ps1`，结果保存在同级 `slot-contract-probe.json`。这是显示判定复现，尚需你编译修正后核对实际渲染。

原生胶囊完整踩坑与分工文档已放在同目录 `CAPSULE_IMPLEMENTATION_NOTES-0.7.44.md`；请继续由你维护前端代码，Codex 维护后端和协议。
