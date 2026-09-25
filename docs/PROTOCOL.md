# CriScope 数据与 Agent 接口

## 原生采集

桌面主动连接配置的 CRI Monitor TCP 地址（默认 `127.0.0.1:2002`），发送 VERSION 与 START_LOG_RECORD，结束时尽力发送有超时限制的 STOP_LOG_RECORD。解析器支持已验证的 32/64 位大端帧布局，拒绝非法帧长度；未识别参数保留诊断，不以猜测值填充图表。接口为逆向观察得到的版本相关实现，不是官方兼容性承诺。

会话来源为 `CRI Monitor`，事件 `source` 为 `cri-native`。Cue 请求使用 `request`，原生 Voice 使用 `play`/`stop` 并带 `entity=voice`。Voice 与 Playback 对象 ID 含采集段编号，避免重复开始采集后串联旧对象。`raw` 保存被解析的原生参数和来源，诊断中的原生 Block 请求不代表实际已跳转。

## 可选 SDK TCP

Unity SDK 扩展连接 `127.0.0.1:18961`。UTF-8 JSONL，每行一个对象，首行示例：

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

`.criscope` 为 UTF-8 JSONL，首行为版本 1 的 hello，之后是开始录制后接收的去重事件。它不含音频，不补录此前 Live 缓存。正常停止刷新文件，异常退出可能留下不完整末行。

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

UI action 白名单为 `workspace`、`live`、`filter`、`select`、`session`、`range`、`theme`、`diagnostics`。值为字符串；workspace 使用 `Timeline`、`AISAC`、`Mixing`、`Location`、`Performance`；range 使用 `起点:终点` 秒数。未知 action 被拒绝。截图不接受任意输出路径，调用者自行保存响应。

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
