# CriScope 数据与 Agent 接口

## v3 默认接入：游戏主动回连

桌面监听 TCP `18961`。游戏桥接先连接本进程的 CRI Monitor，核验 TCP 对端 PID 与进程启动时间后才发送命令；原生帧与 SDK 事件共同回传。服务不自动发现或主动连接游戏。多机可接入；同机原生固定端口冲突时明确降级 SDK，绝不误连其他进程。

握手：ASCII `CSB3` + 4 字节大端 JSON 长度（最多 16 KiB）+ UTF-8 JSON：`version=3, clientId, captureId, name, machine, pid, platform`。clientId 为进程生命周期 GUID，captureId 为连接段 GUID。

每帧 25 字节头：offset 0 为 int32 大端 payloadLength（最多 8 MiB）；offset 4 为 channel；offset 5 为 int64 大端 transport seq；offset 13 为 int32 大端 epoch；offset 17 为 int64 大端 observedMicroseconds。随后为 payload。

- channel 1：完整原生 CRI 帧，保留原始时间。
- channel 2：SDK WireEvent UTF-8 JSON，保留 SDK 时间。
- channel 3：`{channel:1|2,status,connected}` 状态。
- channel 4：`{first,last,count,channel:1|2,epoch,reason,captureId}` 缺失说明，可描述前一连接段。

observedMicroseconds 仅作接收关联证据，不将原生与 SDK 时间强行对齐。每客户端显示一张卡片，内部通道有独立会话和事件序列。队列按完整帧有界；溢出中断段并报告缺失。native epoch 不得倒退。

事件补充字段为 `clientId/machine/captureId/channel/epoch/observedTime/baseline`。`baseline=true` 仅标注记录起点的已观测状态，保持原始 seq/time，不能解释为此刻重新发生。`remove` 是空间对象销毁墓碑；缺失后旧状态不可继续作为已知状态。

问题包 ZIP 内包含 `.criscope`、`manifest.json`、`screenshot.png` 与说明；伴随通道导出自身可用区间，不伪造同步范围。截图取自导出时应用渲染，不是历史重建截图。

以下直接连接与 JSONL 为兼容/高级入口，默认交互使用上述桥接。

## 原生采集

桌面主动连接配置的 CRI Monitor TCP 地址（默认 `127.0.0.1:2002`），发送 VERSION 与 START_LOG_RECORD，结束时尽力发送有超时限制的 STOP_LOG_RECORD。解析器支持已验证的 32/64 位大端帧布局，拒绝非法帧长度；未识别参数保留诊断，不以猜测值填充图表。接口为逆向观察得到的版本相关实现，不是官方兼容性承诺。

会话来源为 `CRI Monitor`，事件 `source` 为 `cri-native`。Cue 请求使用 `request`，原生 Voice 使用 `play`/`stop` 并带 `entity=voice`。Voice 与 Playback 对象 ID 含采集段编号，避免重复开始采集后串联旧对象。`raw` 保存被解析的原生参数和来源，诊断中的原生 Block 请求不代表实际已跳转。

## 旧版 JSONL TCP 兼容

旧版扩展连接 `127.0.0.1:18961`。UTF-8 JSONL，每行一个对象，首行示例：

```json
{"kind":"hello","session":"11111111222233334444555555555555","name":"CRI SDK","platform":"WindowsEditor","pid":1234,"value":1}
```

同一连接只允许一个会话；重连复用 GUID、名称、平台和 PID。合法实时握手表示 SDK 采集开启，`state=0` 表示关闭。通用传输保留兼容性，但默认产品接入直接从 CRI SDK 观察，不要求业务打点。

| 字段 | 含义 |
| --- | --- |
| `kind` | 事件类型 |
| `session` | 会话 ID |
| `seq` | 会话递增正整数，重传沿用序号 |
| `time` | 本来源单调秒数，不能直接跨来源比较 |
| `name` | Cue、控制、指标或事件名 |
| `objectId` / `parentId` / `entity` | 来源内的对象、父关联和实体类型 |
| `source` | 事件来源 |
| `cue` / `value` | Cue 标识及控制、指标或状态值 |
| `detail` | 口径和诊断说明 |
| `raw` | 原生解析参数的 JSON 字符串（若提供） |
| `x/y/z` | 坐标 |
| `pid/platform` | 握手中的进程与平台 |

常用类型包括 `aisac`、`selector`、`bus`、`metric`、`position`、`beat`、`sequence`、`block`、`log`、`gap`。同名事件的意义仍需结合来源与 detail；尤其 SDK Block 是轮询观测，原生 Block 是请求。

接收器回复 `{"ack":1}`，数字为已接收最大序号水位，不代表已落盘或此前序号连续。桥保存最多 8,192 条待确认事件，断线重传；溢出产生缺失证据。强制退出或超出异步收尾期限的数据可能缺失。不能把传输缺失解释成音频丢音数量。

## 录制

`.criscope` 为 UTF-8 JSONL，首行为版本 1 的 hello，之后可包含 `baseline=true` 的已观测起点状态（原始时间不变），再追加开始记录后接收的去重事件。它不含音频，不补录此前完整历史。正常停止刷新文件，异常退出可能留下不完整末行。

Live 窗口限制为 120 秒或 120,000 条；回放每文件最多 2,000,000 条。打开原生录制也保留来源字段。重复加载同 ID 来源可能导致查询歧义，需仅保留一个要查询的来源。

## HTTP

桌面 HTTP 仅绑定 `http://127.0.0.1:18962/`。带 Origin 的请求以及跨站 Fetch 请求被拒绝；不开放 CORS。POST 请求使用 `application/json`，正文最多 16 KiB。它是本机 Agent 能力，不是远程认证服务。

| 方法与路径 | 作用 |
| --- | --- |
| GET `/sessions` | 会话、Source、Endpoint、连接/采集/录制状态及计数 |
| GET `/events?session=...` | 分页事件 |
| GET `/summary?session=...` | 当前窗口类型计数和覆盖边界 |
| GET `/ui/state` | 当前工作区、筛选、Live、所选会话等语义状态 |
| GET `/screenshot` | 应用自身渲染的 PNG |
| GET `/screenshot?panel=timeline` | 时间线区域 PNG；还支持 window/workspace/sessions/inspector/details/diagnostics，隐藏面板拒绝截图 |
| POST `/ui/action` | `{"action":"workspace","value":"AISAC"}` 等视图操作 |
| POST `/native/connect` | `{"host":"127.0.0.1","port":2002}` |
| POST `/native/disconnect` | `{"session":"..."}` |

UI action 白名单为 `workspace`、`live`、`filter`、`select`、`session`、`range`、`theme`、`diagnostics`、`fit`、`control-kind`、`spatial-layer`、`navigate`、`back`、`log-search`、`log-follow`。值为字符串；workspace 使用 `Timeline`、`AISAC`、`Mixing`、`Location`、`Performance`、`Logs`；range 使用 `起点:终点` 秒数。未知 action 被拒绝。截图不接受任意输出路径，调用者自行保存响应。

事件/汇总查询参数：

| 参数 | 规则 |
| --- | --- |
| `session` | 必填，唯一匹配会话 |
| `from` / `to` | 包含边界；默认 0 至最新时间 |
| `kind` | 可选精确事件类型 |
| `watermark` | 最大序号，默认当前水位 |
| `after` | 分页排他起始序号，默认 0 |
| `limit` | 默认 200，范围 1–2,000 |

事件页返回 `events`、`hasMore`、`nextAfter`。分页固定首次 watermark/from/to，然后推进 after；Live 期间仍可能逐出旧记录。稳定完整查询应使用已停止的录制。汇总包含 `groups`、`earliest`、`evicted`、`dropped`、`coverage`。

## MCP

`CriScope.exe --mcp` 运行逐行 JSON-RPC stdio 代理，协议 `2024-11-05`，调用已运行桌面的 HTTP 服务。

工具：`list_sessions`、`query_events`、`summarize_window`、`get_ui_state`、`capture_screenshot`、`control_ui`、`connect_native`、`disconnect_native`。截图返回 MCP image 内容。它不执行任意代码、不读取任意文件，也不控制游戏音频业务。


## v0.4 播放、时间和界面补充

`lifecycle` 区分 created/allocated/stopped/released；`endReason=playback-limit` 与 `causeId` 表示原生数量限制终止及触发实例。旧 raw 函数记录仍可解释。实例停止与最终释放不混同。

录制头可包含固定 `timeOrigin/timeOriginBasis`、`clockAnchorTime/clockAnchorUtc/clockBasis`；墙钟为接收锚点估算，旧记录无锚点不编造。`receivedAtUtc` 与原生 `time` 分开。SDK 显示投影使用 `originalTime/estimatedTime`，仅为视图副本，不修改原始日志。

`/sessions` 增加 `receiveAgeMilliseconds`、`transportLagGrowthMilliseconds`、`sourceObservationLagGrowthMilliseconds`、`receiverBufferedBytes`。积压为相对本连接/段最好观测的增长，不能视为绝对端到端延迟。

`POST /ui/action` 新增 `fit`、`control-kind`（如 `aisac:false`）、`spatial-layer`（如 `source:false`、`distance-listener:true`、`listener:false`）。`live:true` 返回最新并恢复 30 秒；不影响游戏采集。`/ui/state` 增加起点、控制筛选与图层状态。
