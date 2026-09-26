# CriScope

CriScope 是独立的 Windows CRI 音频观测工具，使用 .NET 10 与 Avalonia 构建。游戏侧通用桥接连接本进程的 **CRI 原生 Monitor TCP 端口**，并主动回连桌面服务，不需要在播放、暂停或 AISAC 业务封装里打点。可选 Unity SDK 扩展补充内存、流式声池和回调信息。

## 运行

1. 解压发布包并启动 `CriScope.exe`，无需另装 .NET。
2. 游戏音频调试区末尾填写 CriScope 服务地址，默认 `127.0.0.1`；打开唯一采集开关即可自动接入。远程填写运行 CriScope 的电脑 IP，确保该电脑 TCP `18961` 可达。
3. 桌面左侧每个进程实例一张卡片；同一卡片整合原生与 SDK 通道状态，原始日志各自保留独立时钟。通过声音时间线、控制、混音、空间、资源查看数据。事件日志支持实时搜索播放、结束、参数与回调，双击定位轨道；播放与空间支持双向定位和返回。详见 [v0.6 操作与验证说明](docs/V06_DELIVERY.md)。
4. “开始录制”开始保存，“停止并保存”结束，界面显示保存路径。记录不包含声音。“导出问题包”包含选区（没有选区则为可见范围）、伴随通道可用记录、当前界面截图、问题描述及缺失说明。

支持多台电脑回连。同机多个游戏的 SDK 通道可接入，但当前 CRI 固定原生端口不能保证每个进程同时采集；桥接验证端口所属 PID，错误进程明确拒绝，绝不把别的游戏数据显示到当前卡片。

Live 内存每通道最多保留 120 秒或 120,000 条事件，另保留最多 8,192 项已观测状态（保留原始时间）。日志开始时保存明确标记的状态基线，之后追加新事件。冻结只暂停视图，点击事件不会自动冻结。历史单文件最多加载 2,000,000 条事件，该上限不代表已验证的交互性能。

默认日志位置为 EXE 同目录 `.local/recordings`；`CRISCOPE_DATA` 可覆盖。高级兼容启动参数保留 `--connect 127.0.0.1:2002`，正常使用无需它。另有 `--open <文件.criscope>`、`--light`、`--mcp`。同机只运行一个桌面接收器。

## 数据口径

| 工作区 | 内容与边界 |
| --- | --- |
| 声音时间线 | 按 Playback 分组显示 Cue 播放实例，可展开其 Voice；同名 Cue 不合并，父子关系不是 Cue 嵌套 |
| 控制 | AISAC 写入、Selector/Label、Block 请求与 SDK 回调；不会猜测连接前的值 |
| 混音 | 原生 Bus 通道峰值/RMS 与响度；缺失数据表示未采集，不能当成零 |
| 空间 | 原生音源、Listener 及已知字段推导的衰减参考点；通过真实播放关联标注 Cue，不猜测 GameObject 名称或未知位置 |
| 资源 | 原生 CPU、声部、流式使用量；可选 SDK Atom/FS 内存和流式声池容量 |

原生与 SDK 保留各自时钟；同次采集的 SDK 回调按邻近桥观测锚点估算显示位置，并标注“SDK 约时”。只有原始 Playback ID 与时间区间唯一匹配时才关联实例。录制保留解析事件与原生参数，不声称是无损网络包抓取。缺失与断线会保留明确记录；未知值不当成零。

## 查看播放与控制

- 时间线按 Cue 播放实例组织，静音 Voice 正常显示；数量限制终止显示原因和触发实例。
- 详情显示开始、结束、播放历时与可选 Cue 标注时长。播放历时按 Voice 分配至释放计算，可能包含暂停和间隔，不等于音频素材长度。
- 时间轴从固定采集起点计时；钟表时间标注为接收锚点估算。返回实时恢复最近 30 秒；“全览”只改变视窗，不停止采集。
- 控制页支持 AISAC、Selector、Block、BeatSync、Sequence 多选。AISAC/Selector 按参数名展开各 Player（或 Category）的设置；父行只统计数量，不混合不同对象的值。回调按播放实例展开事件类型，Sequence 标签在具体记录中显示；未关联回调独立保留。Block 请求与位置采样分开。点击子行在详情查看记录；历史播放不会套用之后的设置值。
- 详情抽屉自身提供关闭按钮和 Esc 关闭；实时刷新保留折叠状态、滚动和选择。热接入前的未知开始时间放在详情中，不影响有 Voice 证据的“播放中”状态。
- 空间页独立切换音源、衰减监听点图层，监听点优先显示和点击。
- 每个客户端只有一张卡，显示 IP 与原生/SDK 通道状态。诊断抽屉提供相对积压增长、接收新鲜度和待处理字节，不将其称作绝对延迟。

## Unity 通用桥接

将 `integrations/unity` 中的 C# 文件复制到已安装 CRI Unity SDK 的工程。在游戏调试菜单主线程调用：

```csharp
CriScope.Unity.CriScopeDiagnostics.Connect("127.0.0.1");
CriScope.Unity.CriScopeDiagnostics.Disconnect();
```

桥接不依赖业务音频管理器，不给播放、暂停、AISAC 业务方法添加采集点。开启后连接桌面 `18961`，转发原生 Monitor 帧；约 500 ms 采样 Atom/FS 内存、StandardStreaming 声池，订阅 BeatSync/Sequence，并对 SDK 可观察 Playback 轮询 Block。Block 轮询不是精确切换回调，不覆盖所有自定义原生 Player；BeatSync 依赖 Cue 实际配置。

关闭会移除采样组件与回调，停止网络、线程及重试，并停止本桥接原生订阅。普通 Release 默认编译排除采集实现；Editor/Development Build 未开启时不创建采集对象。项目原本就打开的 Monitor 仍归项目所有，其原有开销不能算作 CriScope 新增开销；性能基线需结合项目本身 IGP 配置。

Monitor 未开启的 Windows x64 客户端可使用严格 DLL SHA-256 与指令校验的热启停适配器。它使用非公开接口，未知版本拒绝调用，不能宣称通用于所有 CRI 版本。Monitor 启停允许短暂停顿。不要为了接入改写项目原有 IGP 默认配置。

## Agent 接口

桌面提供本机 `http://127.0.0.1:18962`：查询会话、事件与汇总，控制视图、连接原生端口，以及直接渲染应用 PNG。截图来自应用自身，不依赖操作系统桌面截图，即使远程桌面截图不可用也有后备接口。

`CriScope.exe --mcp` 提供标准输入输出 MCP 代理，需要桌面接收器已经运行。接口和工具见 [协议说明](docs/PROTOCOL.md)。这不是游戏操作 API；界面操作不会调用游戏播放或暂停业务。

## 构建与验证

```powershell
./tools/setup.ps1
./tools/dotnet.ps1 run --project ./tests/CriScope.Tests/CriScope.Tests.csproj
./tools/dotnet.ps1 run --project ./tests/CriScope.NativeTests/CriScope.NativeTests.csproj
./tools/dotnet.ps1 run --project ./tests/CriScope.AgentTests/CriScope.AgentTests.csproj
./tools/dotnet.ps1 run --project ./tests/CriScope.ReleaseGuardTests/CriScope.ReleaseGuardTests.csproj
./tools/dotnet.ps1 run --project ./tests/CriScope.UiSmoke/CriScope.UiSmoke.csproj --artifacts-path .local/ui-smoke-build
./tools/publish.ps1
```

SDK 与依赖缓存位于 `.local`；发布目录为 `artifacts/CriScope-win-x64`，压缩包为 `artifacts/CriScope-win-x64.zip`。自包含多文件部署避免启动时临时解压运行时。

测试覆盖传输、录制、原生分片解析、取消 STOP、断线、采集段隔离、Agent API 和 UI 生命周期。原生真实录制夹具仅在本地存在时验证，不包含在公共仓库。

本仓库不包含 CRI 专有 SDK、游戏资源、私有工程源码、凭据或实际采集记录。CriScope 为独立工具，尚未覆盖官方 Profiler 的全部能力。
