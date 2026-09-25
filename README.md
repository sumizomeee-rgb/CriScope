# CriScope

CriScope 是独立的 Windows CRI 音频观测工具，使用 .NET 10 与 Avalonia 构建。主数据源直接连接游戏的 **CRI 原生 Monitor TCP 端口**，不需要在播放、暂停或 AISAC 业务封装里打点。可选 Unity SDK 扩展补充内存、流式声池和回调信息。

## 运行

1. 解压发布包并启动 `CriScope.exe`，无需另装 .NET。
2. 确保目标游戏的 CRI Monitor 已开启，在左侧输入 `127.0.0.1:2002` 并连接。原生连接从连接时开始采集，不补齐此前历史。请避免同时使用其他 Profiler 占用同一 Monitor。
3. 左侧选择会话，通过播放、控制、混音、空间、资源五个工作区查看数据；详细证据和原始字段放在诊断区。
4. “录制当前会话”保存事件文件，与游戏采集开关独立。录制不包含声音，回放只浏览事件。

Live 内存最多保留 120 秒或 120,000 条事件，另保留最多 8,192 项最后已知状态供视图使用（原时间不变，不加入录制和事件分页）；冻结仅暂停视图。点击事件和 Ctrl+滚轮缩放不会自动关闭 Live。历史单文件最多加载 2,000,000 条事件，该上限不代表已验证的交互性能。

默认录制位置为 EXE 同目录 `.local/recordings`；`CRISCOPE_DATA` 可以覆盖录制目录。启动参数：`--connect 127.0.0.1:2002`、`--open <文件.criscope>`、`--light`、`--mcp`。同机只运行一个桌面接收器。

## 数据口径

| 工作区 | 内容与边界 |
| --- | --- |
| 播放 | 原生 Voice 生命周期与 Cue 播放请求；请求不等于声部已经播放 |
| 控制 | AISAC 写入、Selector/Label、Block 请求与 SDK 回调；不会猜测连接前的值 |
| 混音 | 原生 Bus 通道峰值/RMS 与响度；缺失数据表示未采集，不能当成零 |
| 空间 | 原生声源与 Listener 位置；可观察 CRI 位置，不保证对应 Unity GameObject 名称 |
| 资源 | 原生 CPU、声部、流式使用量；可选 SDK Atom/FS 内存和流式声池容量 |

原生与 SDK 会话使用各自的时钟。相同 PID 的 SDK 最新资源数值可供原生资源页参考，但不会把两个未经校准的时间轴混在一起。方向等高频重复更新会变更采样；诊断记录保留其口径，不声称是无损原始包抓取。

## 可选 Unity SDK 扩展

将 `integrations/unity` 下的三个 C# 文件复制到已安装 CRI Unity SDK 的工程。在游戏调试菜单主线程调用：

```csharp
CriScope.Unity.CriScopeDiagnostics.SetCaptureEnabled(true);
CriScope.Unity.CriScopeDiagnostics.SetCaptureEnabled(false);
```

扩展直接使用 CRI SDK，不依赖项目音频管理器。开启后连接本机 `18961`，以约 500 ms 周期采样 Atom/FS 内存、StandardStreaming 声池使用量，订阅 BeatSync 和 Sequence，并对可观察 Playback 轮询 Block。Block 轮询不是精确音频切换回调，也不覆盖所有自定义原生 Player。BeatSync 依赖 Cue 实际配置，不会从名称猜测 BPM。

关闭时移除采样组件和回调订阅，停止扩展传输。默认 Release 编译排除采集实现；Editor/Development Build 也只有显式开启后才创建采集对象。项目原有 Monitor 若已开启，关闭此扩展不会擅自关闭它，因此不能把“扩展关闭”解释成“整个 CRI Monitor 无开销”。

对于 Monitor 未开启的 Windows x64 客户端，扩展提供严格 DLL SHA-256 与指令校验的版本适配器进行热启停。它使用非公开接口，未知版本拒绝调用，不能宣称通用于所有 CRI 版本。Monitor 启停可能产生短暂停顿；不要在性能基线测量期间切换。工程应自行将 Player 默认 IGP 配置为关闭。

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
