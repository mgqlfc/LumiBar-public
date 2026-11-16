# 系统定位接口更新

2026-11-15，用户明确同意使用 Windows 返回的任意定位来源，覆盖早先“只接受 GPS 卫星”的要求。

- 新接口：`cities.locate`，参数 `{}`，返回解析后的 `City`，同时启用 `settings.autoLocateCity`。
- 兼容接口：`cities.locateGps` 保留为相同操作的别名。它现在也接受 Windows 的 Wi-Fi、GPS、移动网络、IP 和默认位置等来源。
- `hello.capabilities.systemLocation` 为 `true`，`hello.methods` 同时列出新旧方法。协议版本保持 1。
- 后端仍通过 Windows Geolocator 取位置，不额外接入 IP 查询服务。城市名称继续用现有逆地理编码服务解析；解析失败时保留系统坐标并显示“当前位置”，天气照常读取。
- 自动定位每 15 分钟尝试更新。点击已保存城市后暂停自动定位。权限拒绝或系统取不到位置时，`weather.locationStatus` 保留具体失败提示；成功或手动切换城市后清除。

前端按钮与设置项建议使用“自动定位城市”“正在读取系统位置”，移除“只支持 GPS”或“仅卫星来源”的限制说明。
