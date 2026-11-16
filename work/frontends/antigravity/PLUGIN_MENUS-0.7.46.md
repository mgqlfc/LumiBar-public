# LumiBar 0.7.46 插件自定义菜单

协议保持 Frontend API v1。`hello.capabilities.pluginMenus=true`，`pluginMenuLocations=["tray","settings"]`。用户明确不要胶囊右键菜单，接口也拒绝 music / weather / capsule 等位置。

## 1. 用户看到什么

- **托盘右键**：保留 LumiBar 原有操作，按插件名称增加子菜单，支持操作、分隔线、子菜单、禁用、勾选。
- **设置左侧**：每个有 settings 操作的插件出现一个入口；选中后，右侧按定义展示操作列表和分组。插件可响应操作后打开自有设置窗/工作台；展开方式、大小及界面仍归插件控制。
- 只有 tray 的插件不增加设置入口；只有 settings 的插件不增加托盘项。无需接管双胶囊即可注册，恢复内置渲染也不会自动删除仍在运行的插件菜单。

注册仅在内存里存在，不向 Preferences 写入任何值。菜单动作只下发 id 与状态，不接收 shell/脚本命令，也不由后端加载插件类库。

## 2. 注册

每条管道先 hello。前端可沿用正常端点；独立预览后端也能测试设置入口，但没有系统托盘图标。

```json
{"id":"menu-register","method":"menus.register","params":{
  "pluginId":"antigravity.lumibar-glass",
  "displayName":"Antigravity",
  "navigationTitle":"Antigravity",
  "navigationDescription":"界面、歌词和扩展操作。",
  "items":[
    {"id":"open-settings","label":"前端设置","locations":["tray","settings"]},
    {"id":"pin-lyrics","label":"固定歌词","checked":false},
    {"kind":"separator"},
    {"id":"tools","label":"更多操作","kind":"submenu","children":[
      {"id":"show-workbench","label":"打开预览工作台"},
      {"id":"unavailable","label":"暂不可用","enabled":false}
    ]}
  ]
}}
```

成功的 data：

```json
{"registrationId":"…","registrationToken":"…","leaseDurationMs":15000,"renewAfterMs":5000,"expiresInMs":15000,
 "menu":{"registrationId":"…","revision":1,"pluginId":"antigravity.lumibar-glass","displayName":"Antigravity","navigationTitle":"Antigravity","navigationDescription":"…","items":[],"expiresInMs":15000}}
```

上例 menu.items 为节省篇幅留空；真实响应返回标准化后的完整定义。

`registrationToken` 只在注册响应提供，后续返回 null；不写日志、清单或设置文件。后端用 Named Pipe 的实际进程 PID 校验，凭证可跨同进程的 State/Control/Events 连接使用。凭证与任务栏 renderer 的 leaseId/leaseToken 分开，不能混用。

规则：同一 pluginId 同时只注册一份；全后端最多 8 份，每份最多 32 个节点、最多两级子菜单。每个 action/submenu 的 id 在当前插件内唯一。label 最多 80 字符，displayName/navigationTitle 最多 48，描述最多 200；不接受控制字符。空或纯分隔线菜单无效。

`kind` 默认 action；`enabled` 默认 true；`checked` 缺省/null 表示普通操作，false/true 表示带勾选状态。`locations` 顶层默认两个位置，子节点默认继承父级，可缩小范围，不能越出父级。navigationTitle 缺省用 displayName。

## 3. 更新、续租、注销

```json
{"id":"menu-renew","method":"menus.renew","params":{"registrationId":"…","registrationToken":"…"}}
```

每约 5 秒续租，超时 15 秒失效。进程退出/启动身份改变时后端约 1 秒内移除；单条管道断开不会误删另一条管道仍使用的菜单。后端重启后重新注册。上述时间使用单调时钟。

```json
{"id":"menu-update","method":"menus.update","params":{"registrationId":"…","registrationToken":"…","definition":{
  "pluginId":"antigravity.lumibar-glass","displayName":"Antigravity","navigationTitle":"Antigravity",
  "items":[{"id":"pin-lyrics","label":"固定歌词","checked":true}]
}}}
```

update **完整替换定义**，不可更换 pluginId；一次校验失败保留旧菜单。revision 递增并更新两处入口。已打开的旧版原生菜单不能调用新版动作。需要保留的项目必须在新定义中继续提供。

```json
{"id":"menu-remove","method":"menus.unregister","params":{"registrationId":"…","registrationToken":"…"}}
```

退出前尽量注销；取消了请求读取后，旧管道可能仍有未消费响应，可用同进程新连接注销。后台仍有进程退出和超时清理。`menus.list {}` 返回 `{items:[…]}`，仅有公开定义与状态，没有密钥。

## 4. 点击事件与确认

保持现有事件连接，`events.wait` 会给出 topics：

- `pluginMenus`：注册、定义更新、注销或失效；设置入口会自动更新，前端需要时可读 menus.list。
- `pluginMenuActions`：有插件操作进入队列；调用下列接口读取自己的操作。

```json
{"id":"menu-read","method":"menus.actions.read","params":{"registrationId":"…","registrationToken":"…","afterSequence":0,"limit":32}}
```

data：

```json
{"registrationId":"…","actions":[
  {"sequence":1,"actionId":"pin-lyrics","location":"settings","menuRevision":1,"proposedChecked":true,"createdAt":"2026-11-15T14:00:00+00:00"}
],"nextSequence":1,"acknowledgedSequence":0,"hasMore":false,"resyncRequired":false}
```

`location` 是 tray/settings。按 `(registrationId,sequence)` 去重，处理成功后确认：

```json
{"id":"menu-ack","method":"menus.actions.ack","params":{"registrationId":"…","registrationToken":"…","throughSequence":1}}
```

未确认的操作可重读，队列最多 64 个；满时拒绝新点击，保留未处理项。不能确认未来序号。limit 范围 1–64；hasMore=true 时继续取。afterSequence 早于已确认位置时 resyncRequired=true，不要再次执行已确认的动作。

勾选动作的 proposedChecked 是目标状态，直接按它设置，再用 menus.update 提交实际结果；不要再次取反。后端不替插件保存业务设置。menuRevision 标明点击时的定义版本，前端可按版本和动作 id 核对。

**续租独立于事件长轮询及慢操作。** 不要把 renew 排到 events.wait 后面，也不要在耗时窗口/网络操作完成前暂停心跳。UI 操作切回自己的 DispatcherQueue；不要在状态同步、勾选项初始化或退出时提交整份默认设置。

## 5. 可直接编译的 C# 示例

同目录 [PluginMenusExample.cs](./PluginMenusExample.cs) 与 LumiBarClient.cs 一起编译。已用真实 Named Pipe 检查注册、跨连接事件、慢处理期间续租、动态勾选、确认与取消注销。

```csharp
await PluginMenusExample.RunAsync(endpointFile, async (click, ct) => {
    // 切到前端 UI Dispatcher 后，按 actionId 打开自己的页面或改变业务状态。
    switch (click.ActionId) {
        case "open-settings": /* ShowSettingsWindow(); */ break;
        case "show-workbench": /* ShowWorkbenchWindow(); */ break;
        case "pin-lyrics": /* SetPinned(click.ProposedChecked == true); */ break;
    }
    await Task.CompletedTask;
}, lifetimeToken);
```

需要替换示例动作处理；观察返回的 Task，在前端退出时取消并 await。示例自带两条连接，已有 State/Events/Spectrum/RendererControl 四连接时合计六条，低于后端 16 条上限。也可把相同流程整合进现有 Control / Events 服务。

## 6. 已验证与错误码

实测：设置左侧入口、操作响应、勾选更新、禁用与分组；同一原生托盘菜单中的插件子菜单和嵌套点击；注销后入口移除。测试配置文件哈希保持不变。胶囊右键未增加处理器。

常见错误：invalid_params、peer_mismatch、menu_already_registered、menu_limit、menu_registration_invalid、menu_registration_expired。失效后停止旧会话，前端按自身生命周期决定是否重新注册。
