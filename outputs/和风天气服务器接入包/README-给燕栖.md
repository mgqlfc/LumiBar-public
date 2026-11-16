# 和风天气管理接入

2026-11-15。LumiBar 的天气代理已部署到燕栖使用的同一台 Ubuntu 服务器。燕栖现有服务保持原进程运行，天气服务独立启动、独立重启。

## 文件与服务

| 项目 | 值 |
|---|---|
| 对外 HTTPS 地址 | `https://124.156.224.125/lumibar-weather/` |
| 本机监听 | `127.0.0.1:8767` |
| systemd 单元 | `lumibar-weather.service` |
| 配置文件 | `/etc/lumibar-weather.env`，root:root，0600 |
| 服务代码 | `/opt/lumibar-weather/` |
| 受限管理程序 | `/usr/local/sbin/lumibar-weather-manage` |
| Nginx 路由 | `/etc/nginx/snippets/lumibar-weather.conf` |
| 当日上游请求计数 | `/var/lib/lumibar-weather/quota.json` |

配置使用 `QWEATHER_API_HOST`、`QWEATHER_API_KEY`、`UPSTREAM_DAILY_LIMIT`。Host 已设置为用户提供的地址。Key 只留在服务器配置文件和天气进程内，状态接口、日志、LumiBar 安装包均不返回 Key。

## 燕栖管理连接

沿用燕栖原有 `yanqi-manager` SSH 连接及 `management.key`，不需要管理员 PEM。服务端的 forced-command 入口已增加以下固定命令：

| SSH 原始命令 | 用途 |
|---|---|
| `weather-status` | JSON 状态、API Host、keyConfigured、额度上限、当天请求数、PID、内存、健康检查 |
| `weather-configure` | 从标准输入接收 JSON，原子保存配置，然后只重启天气服务 |
| `weather-test` | 用武清附近坐标实测天气；返回 source、hourCount、dayCount、aqiStandard、alertCount、notes |
| `weather-restart` | 只重启天气服务 |
| `weather-logs` | 最近 80 行天气服务日志；服务不记录 Key、请求头或精确坐标 |
| `weather-requests` | 最近 200 条跨应用请求明细；最多保留 1000 条 / 7 天 |

保存配置的标准输入格式：

```json
{
  "apiHost": "用户的 API Host",
  "apiKey": "用户在密码输入框填写的新 Key",
  "dailyLimit": 1000
}
```

不要把 JSON 拼到 SSH 命令行。用 `execFile/spawn` 的 stdin 传入，禁止记录原始 stdin。`apiKey` 省略或留空会保留现有 Key；只返回 `saved` 和 `keyConfigured`，不会回显内容。接口只接收以上三个字段。

建议界面放：服务状态、API Host、新 Key 密码框、当日请求数/上限、保存并测试、测试连接、重启服务。已有 Key 只显示“已配置”，密码框留空。错误提示应显示接口结果，不读取整个环境文件或进程环境。

## LumiBar 数据接口

- `GET /healthz`：返回版本和 configured，不消耗和风请求额度。
- `GET /v1/forecast?lat=39.36&lon=117.07`：返回 `{ "forecast": ... }`，包含当前天气、24 小时、10 天、空气、5 分钟降水、预警。
- `GET /v1/alerts?lat=39.36&lon=117.07`：返回当前有效预警与来源，供客户端每 5 分钟刷新。

上述路径都相对于 `/lumibar-weather/`。API Key 通过服务端的 `X-QW-Api-Key` 请求头发送给和风，不经过客户端。

### 跨应用请求明细

请求时设置 `X-LumiBar-Client: Yanqi`。LumiBar 自动设置 `LumiBar`；未标识的客户端显示 `Unknown`。应用名称是客户端标识，不用于认证。来源 IP 由 Nginx 根据连接覆盖，忽略客户端伪造的 X-Real-IP / X-Forwarded-For。

`weather-requests` 返回：

```json
{"scope":"weather-proxy","retentionDays":7,"maxRecords":1000,"total":1,"items":[{"id":"请求 UUID","at":"UTC ISO 时间","client":"LumiBar","sourceIp":"来源 IP","endpoint":"/v1/forecast","status":200,"durationMs":120,"cacheHits":6,"coalesced":0,"upstreamRequests":0}]}
```

items 按时间倒序，最多返回最近 200 条，total 是保留范围内总数。`upstreamRequests` 只统计本次请求真正发起的上游调用，合并等待记到 `coalesced`，不重复计数。明细覆盖所有经过天气代理的应用。存储文件为 `/var/lib/lumibar-weather/requests.json`，0600，不提供公开 HTTP 明细接口。

实时/预警/分钟降水缓存 5 分钟，逐时缓存 1 小时，逐日缓存 3 小时，空气缓存 15 分钟。相同地点的并发请求合并；IP 限流每分钟 20 次；当日上游请求上限默认 1000，计数落盘，重启不清零。上游错误短暂缓存，避免反复请求。

AQI 优先选择中国标准并标注 CN AQI，缺失时明确标注实际返回的 US AQI / QAQI。预警按真实颜色展示；更新、取消与过期预警会过滤。缺少权限的可选模块不会拖垮当前天气，具体模块状态在 notes 中返回。

## 部署与隔离

复用现有有效的 IP HTTPS 证书，无需域名。天气进程只监听回环地址，不新增公网监听端口。安装只平滑 reload Nginx，不重启 `yanqi-tavern.service`。初次安装前后燕栖 PID 均为 3649875。

首次部署备份：`/var/backups/lumibar-weather/20261115-120835`。部署源与测试包随后随 LumiBar 输出提供。密钥输入完成后，用 `weather-test` 核对各模块的实际权限；`keyConfigured: true` 只表示已保存，实测成功另看 `ok`。

已验证：Key 已保存，真实接口返回 24 小时、10 天、CN AQI、分钟降水和当前预警，notes 为空。服务端 13 项隔离回归通过。新增请求明细版本备份：`/var/backups/lumibar-weather/20261115-123342`。
