# 和风天气与预警扩展

协议仍为 v1；新增字段均可选，旧缓存仍可读取。后端已通过独立服务器代理接入和风天气，前端无需 API Key。

`forecast` 新增 `alerts`、`aqiStandard`、`aqiCategory`、`conditionText`、`attributions`、`providerNote`。类型见 contracts.d.ts。

`alerts` 是当前有效预警，按红/橙/黄/蓝排序。显示对应颜色图标和 `event + 等级颜色`，详情保留 headline、description、instruction、source、issuedAt、endsAt。多个发布方可能产生同类预警，概览可合并同类，但详情保留全部信息；过期与取消预警不显示。内置前端在天气描述后显示两类，间距约半角空格，其余用 +N 入口展开。

AQI 必须按 `aqiStandard` 标注，不能把 CN AQI 显示为 US AQI；优先使用 `aqiCategory`。天气现象优先显示 `conditionText`，图标仍用统一的 WMO 映射 `code`。

降水趋势 `kind` 新增 `nowcast`，`intervalMinutes` 可能为 5 或 15。amountMm 仍是该区间的累计毫米数；时间均为带时区的 ISO 字符串。和风数据标注“5 分钟临近预报”，原 Open-Meteo 仍标注“15 分钟模式趋势”。

设置新增 `weatherProvider: 'Auto' | 'Public'`、`weatherProxyUrl`，通过 `settings.patch` 保存。Auto 优先云端，失败自动退回免费源。前端不得放置和风 Key / 私钥输入项，服务端管理由燕栖负责。

后端每 5 分钟独立刷新预警，变化仍推送 `weather` 通知。来源归因由 `attributions` 提供，界面需要提供可读入口；`providerNote` 是可选模块的机器状态（如 `air: upstream_403`），展示时翻译成简短提示。

跨应用请求记录通过受限 SSH 的 `weather-requests` 读取，详见 `outputs/给燕栖的和风天气管理接入说明.md`，不属于前端公共天气 HTTP API。
