# CriScope

CriScope 是 Windows 本机音频事件观测工具，使用 .NET 10、Avalonia 12.1.3 构建。它接收游戏端桥接器发出的事件，提供多客户端会话、时间轴、AISAC、性能采样、位置与日志视图，以及录制、历史浏览和只读查询。

当前版本观察业务播放请求和显式采样。它尚未实现原生 CRI Monitor 协议，播放请求不等于实际 Voice 分配，输出电平尚不可用。没有声称覆盖官方 Profiler 的完整功能，也没有百万事件性能验证结论。

## 运行

1. 解压发布 ZIP 到可写目录，保留完整目录结构，启动 `CriScope.exe`。自包含 Windows x64 发布包无需另装 .NET。
2. 在已接入桥接器的游戏中打开采集开关。客户端会出现在左侧会话列表，选择后查看事件。
3. 点击“录制当前会话”开始写盘，再次点击停止。游戏采集开关与桌面录制相互独立；切换会话不会停止其他会话的录制。
4. 退出前先关闭游戏采集开关，再停止桌面录制并关闭 EXE。关闭 EXE 会停止接收，游戏端不会因此自动关闭采集。

监听地址为 `127.0.0.1:18961`，只读查询地址为 `http://127.0.0.1:18962/`。同机只启动一个接收器实例。断线期间桥接器保留有界待确认队列，重连后重传；队列溢出或进程退出可能造成数据丢失。

游戏采集与桌面进程可以独立重连：保持游戏采集开启并重新启动桌面接收器后，桥接器会自动连接，握手恢复采集中状态。桌面重启不会恢复未录制的旧事件，也不保证补齐断线期间超出队列容量的数据；正常退出仍建议先关闭游戏采集。

默认录制目录是 EXE 所在目录下的 `.local/recordings`。环境变量 `CRISCOPE_DATA` 可直接指定录制目录。录制包含事件数据，不包含音频波形。

## 浏览与边界

- 支持浅色、暗色主题，事件筛选、选中证据和原始 JSON 复制。
- Live 内存窗口最多保留最近 120 秒、120,000 条事件，先达到的限制生效。冻结浏览不暂停游戏采集或桌面录制。
- 录制从点击开始之后写入，没有预录历史。持续录制可保存超出 Live 窗口的后续事件。
- “打开录制”读取 `.criscope` 文件，单文件回放最多 2,000,000 条事件。较长采集应分段录制；该上限不代表已验证的交互性能。
- 回放推进的是事件时间轴，不会重放声音。
- 支持运行中热开启，但不会补回开启前的事件。当前对象状态是否可重建，取决于游戏接入代码提供的状态快照。
- `Ctrl+F` 聚焦筛选，`Home` 全览，空格切换 Live/冻结或历史播放状态，`Esc` 清空筛选。

## 接入自己的 Unity 项目

公共仓库提供 `integrations/unity/CriScopeBridge.cs`，不依赖 CRI SDK。将它复制到 Unity 项目，在主线程创建桥接器并调用 `Emit`，在停止采集时发送关闭状态并调用 `Dispose`。游戏 GM 界面、音频业务钩子及初始状态扫描需要按项目接入；公共仓库不包含私有游戏工程或其 GM 实现。

```csharp
var bridge = new CriScope.Unity.CriScopeBridge(
    "127.0.0.1", "My Game", UnityEngine.Application.platform.ToString(),
    System.Diagnostics.Process.GetCurrentProcess().Id);
bridge.Emit(new CriScope.Unity.WireEvent {
    kind = "state", name = "Capture", value = 1
});
// 在实际业务发生的位置发出事件，避免把请求描述为底层成功播放。
bridge.Emit(new CriScope.Unity.WireEvent {
    kind = "play", name = "ExampleCue", objectId = "player-1", cue = 123,
    detail = "业务播放请求"
});
// 关闭采集时：
bridge.Emit(new CriScope.Unity.WireEvent {
    kind = "state", name = "Capture", value = 0
});
bridge.Dispose();
```

桥接器使用后台线程发送数据，`Emit` 必须在 Unity 主线程调用。`Dispose` 不阻塞主线程，提供有限的异步发送收尾时间，不保证进程立即退出时所有事件已确认。协议与只读查询见 [协议说明](docs/PROTOCOL.md)。

## 构建与验证

在仓库根目录的 PowerShell 执行：

```powershell
./tools/setup.ps1
./tools/dotnet.ps1 run --project ./tests/CriScope.Tests/CriScope.Tests.csproj
./tools/dotnet.ps1 run --project ./tests/CriScope.UiSmoke/CriScope.UiSmoke.csproj --artifacts-path .local/ui-smoke-build
./tools/publish.ps1
```

SDK 与依赖缓存放在项目 `.local`。发布目录为 `artifacts/CriScope-win-x64`，压缩包为 `artifacts/CriScope-win-x64.zip`。发布使用自包含、多文件模式，避免单文件程序启动时向系统临时目录解压运行时。

集成测试覆盖真实 TCP ACK、重传去重、会话隔离、断线重连、来源校验、分片协议、损坏输入、录制一致性及 Live 窗口边界。游戏实际接入、视觉交互和产品性能需分别验证。

UI 冒烟在真实 Avalonia 生命周期内驱动按钮事件，检查会话选择、筛选恢复、冻结快照、主题切换、独立录制及文件回放。它不替代操作系统鼠标交互和高 DPI 实机验收。

启动参数：`--open <录制文件>` 打开历史，`--light` 使用浅色主题；`--mcp` 运行标准输入输出 MCP 代理，需要已有桌面接收器运行。代理提供 `list_sessions`、`query_events`、`summarize_window`。

本仓库不包含 CRI 专有 SDK、游戏资源、私有工程源码、凭据或实际采集记录。
