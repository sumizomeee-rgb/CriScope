# CriScope Unity SDK

本目录是 Unity SDK 的唯一源码。SDK 版本由 `CriScopeSdk.Version` 定义，独立于桌面发布版本；SDK 版本见该常量及包内 `SDK_MANIFEST.json`；当前传输协议为 CSB3 版本 3。
游戏工程中的 SDK 文件是分发副本：先在本仓库维护、验证和出包，再同步到宿主工程。不要在游戏副本中维护第二套采集实现。

## 分发与接入

`CriScope-UnitySDK-<version>.zip` 包含三种完整源码配置、稳定的 Unity `.meta`、SHA-256 清单和仅依赖 Python 3.9+ 标准库的 `install.py`。

| 配置 | 用途 |
| --- | --- |
| `runtime` | 运行时或 Play 模式；GM 菜单调用 `CriScope.Unity.CriScopeDiagnostics.Connect(ip)` / `Disconnect()` |
| `editor` | 独立编辑器预览；放在 `Editor` 目录，默认提供 `Tools/Audio/CriScope` 窗口 |
| `mirror` | 仅供需要序列化脚本镜像的资源工程；保持类型身份，采集接口为空壳，不启动任何传输 |

解压后，在接入包目录运行；目标是工程内专用于 CriScope 的脚本目录：

```powershell
python install.py sync --profile runtime --target "<Unity工程>/Assets/CriScope"
python install.py check --profile runtime --target "<Unity工程>/Assets/CriScope"
python install.py sync --profile editor --target "<Unity工程>/Assets/Editor/CriScope"
```

同步会保留已有 `.meta` GUID、相同源码的 BOM/换行及目录中的游戏入口。隐藏清单 `.criscope-sdk.json` 记录 SDK 版本、配置、命名空间和副本哈希。
遇到接入副本的本地源码改动，正常同步会拒绝覆盖；先把需要的修改回收到本仓库。首次接管旧副本时显式使用 `--adopt-existing`，被替换文件会备份到系统临时目录，或由 `--backup-root` 指定位置。
`--namespace` 支持保留已有命名空间；`editor --no-window` 可保留游戏自己的菜单窗口，只安装 SDK 实现。
`--config <本地配置.json>` 可批量维护多个工程：

```json
{"targets":[
  {"profile":"runtime","target":"<客户端>/Assets/CriScope"},
  {"profile":"editor","target":"<资源工程>/Assets/Editor/CriScope","namespace":"YourGame.Editor","includeWindow":false},
  {"profile":"mirror","target":"<资源工程>/Assets/RuntimeMirrors/CriScope"}
]}
```

本机项目路径配置应放在 `.local` 等忽略目录，不能写进通用 SDK。

## SDK 已连接但没有播放轨道

跨机接收端按客户端身份隔离连接，同一 IP 可接入多个进程。原生采集另需游戏电脑自己的 CRI Monitor，使用本机固定端口 2002/2003。
Atom Viewer 等试听工具可能在首次点击 CriScope 连接之前就启动原生端口。SDK 1.0.2 起会在启动前检查冲突，在接收端显示游戏电脑名、冲突程序名和 PID。
释放该工具的原生预览端口后，SDK 在 Unity 主线程按五秒间隔重试 Monitor 初始化，成功后同一桥接连接自动尝试原生订阅；游戏侧不必手动关开采集。
正在工作的原生连接不重新初始化 Monitor，错误进程不会收到 VERSION/START/STOP。未知 DLL 不反复调用私有初始化入口；项目自行启用 IGP 后仍可恢复。
SDK 不会自动关闭其他程序。同机多个应用同时使用 CRI 固定端口的任意实例路由仍不受保证；已有连接逐条验证实际服务端 PID。

## 宿主职责

`CriScopeCaptureSession.cs` 是 Runtime 与 Editor 共用的采样、目录、回调和资源释放实现；传输与 Monitor 适配也只维护一份源文件。
编辑器配置由出包工具生成命名空间隔离的副本，避免与资源工程的运行时镜像冲突。

宿主负责先初始化 CRI，再从主线程开启采集。运行时入口保持不变：

```csharp
CriScope.Unity.CriScopeDiagnostics.Connect("127.0.0.1");
CriScope.Unity.CriScopeDiagnostics.Disconnect();
```

独立编辑器可在自己的 `[InitializeOnLoad]` 适配中设置 `CriScopeEditorCapture.InitializeAudio`，用于准备项目已有的音频预览环境；
`ClientName` 可设置进程显示名。SDK 仅在显式连接且 CRI 尚未初始化时调用初始化委托。项目音频管理器、Lua GM 和菜单入口留在宿主，不进入 SDK。
窗口关闭、重编译、退出编辑器或切换 Play 模式会断开编辑器采集；SDK 不负责销毁宿主创建的 CRI 库。

## 在本仓库开发

```powershell
python -m unittest discover -s tests/CriScope.UnitySdkTests
python tools/unity-sdk.py pack --output artifacts
python tools/unity-sdk.py check --config .local/integrations/unity-sdk-targets.json
python tools/unity-sdk.py sync --config .local/integrations/unity-sdk-targets.json
```

同一 SDK 版本不允许生成内容不同的发行包；发布修改前提升 `CriScopeSdk.Version`。桌面发布脚本会同时生成独立 SDK ZIP，并附带在桌面包的 `UnitySDK/` 中。
SDK 发送端在握手与开启记录中携带 SDK 版本；原生数据协议仍为版本 3。Pan Type、衰减距离和忽略 Player 参数来自 Cue 配置，不冒充运行时最终增益。

实现只在 Unity Editor、Development Build 或显式 `CRISCOPE_DIAGNOSTICS` 下启用；正式构建保留无采集能力的 API 外壳。
本仓库不分发 CRI 专有 SDK；宿主需已有对应 CRI API。SDK 关闭后移除自身采样与回调，保留宿主原有 Monitor/预览环境。

## Monitor ownership

On Windows the extension first inspects the current process's port 2002 listener. An existing Monitor is reused and never finalized by this extension. The relay connects to local port 2002 and verifies the accepted server connection's reversed four-tuple, PID and process start time before sending any native command. A mismatch only closes the socket: no VERSION, START or STOP is sent to another process. SDK observations still reach the desktop with a clear native-unavailable status. Multiple machines can each relay their local process to one desktop service. Multiple simultaneous native instances on one Windows machine are not guaranteed: CRI can reuse the same port, and connect can repeatedly reach the first process. This implementation fails closed rather than silently showing another game's data.

If there is no listener, the Windows x64 adapter checks the loaded DLL SHA-256 and instruction bytes before calling the verified private Monitor wrappers. Unknown DLLs are refused with a status reason; enable IGP at initialization or supply a separately verified adapter. The adapter never patches DLL code, changes ports, or creates another Atom instance. It finalizes only the Monitor it started. Enable/disable is synchronous on the Unity main thread and may briefly stall frames/audio; do not toggle in a latency-sensitive measurement interval.

This adapter is version-specific, not an official CRI API. It does not promise zero process-memory/CPU residue after prior use. An untouched performance baseline requires IGP disabled at Atom initialization and this extension never enabled.

## Observations

- `memory.atom.bytes`: CRI Atom allocator bytes; `memory.fs.bytes`: CRI FS allocator bytes. These are separate, not a fabricated all-audio total.
- `voices.streaming.used` / `.capacity`: the **StandardStreaming** pool only.
- `beat`: `value` is BPM; detail includes bar, beat, beats per bar and offset.
- `sequence`: tag is name, embedded event ID is value; sequence position is retained in detail.
- `cue-info`: optional Cue authored length in milliseconds for the latest active playback exposed by a public CriAtomSource; not the waveform length or a complete enumeration.
- `acb-catalog` / `cue-catalog`: authored Cue names, lengths and Categories from loaded `CriAtom.cueSheets`, including Cues that were playing before connection. The main thread discovers bank load/unload every second and reads at most 16 Cue indices / 2 ms per frame, emitting small batches once per bank generation. Reconnect resends the directory; unloading/replacing an ACB invalidates its old generation. The desktop matches **native ACB handle + Cue name within the same client/capture**, never Cue name alone or the registered alias. These are configuration labels, not runtime Category overrides; native runtime observations take precedence. Directly loaded ACBs outside `CriAtom.cueSheets` are not covered by this directory. The active directory survives the two-minute event window and is included in recording segment baselines. Production builds keep the same inert public shell.
- `block`: first observed or changed block index, sampled every 500 ms. Observed playback IDs come from public CRI components' latest playback and callbacks. This does not enumerate every concurrent native playback or promise exact block-transition time.
- CRI callback-queue overflow warnings are forwarded. Existing callbacks are preserved through event `+=` / `-=`. SDK main-thread dispatch time is not the native Monitor timestamp. The desktop may project callbacks for display using nearby bridge/native clock observations, labels them as estimates, and retains the original channel timestamps.

The native stream remains the authoritative playback/Voice/control source. The SDK extension does not emit synthetic play/pause/stop/AISAC requests.

## CSB3 transport

One outbound TCP connection carries both channels. Handshake: ASCII `CSB3`, 4-byte big-endian JSON length (at most 16 KiB), then UTF-8 hello with `version=3`, `clientId`, `captureId`, `name`, `machine`, `pid`, `platform`. Name defaults to Unity Editor or Unity Player. Client ID is lazily initialized in the process environment and survives toggle/reconnect/domain reload; each network reconnect creates a new capture ID.

Each packet has a 25-byte header: payload length int32 BE, channel byte, transport sequence int64 BE, epoch int32 BE, observed microseconds int64 BE. Payload maximum is 8 MiB. Channel 1 is one complete unmodified CRI frame; 2 is SDK WireEvent JSON; 3 is status JSON `{channel,connected,status}`; 4 is gap JSON `{first,last,count,channel,epoch,captureId,reason}`. SDK event time, native time, and bridge observed time remain distinct.

The sender/native-reader threads share an 8 MiB/8192-frame bounded queue. Overflow discards whole frames, ends the capture segment and carries explicit loss evidence into the next hello; partial native bytes are never forwarded. Lost observations before any capture use first/last/epoch zero and an empty captureId. Native reconnect after prior success ends the whole capture and changes its ID. Native initial failure leaves SDK connected and retries every five seconds. Remote failure stops/closes native subscription before retrying with bounded backoff. There is no replay acknowledgement: a failed in-flight write is reported as delivery uncertain. Gap sequence first/last are bounds, not a claim that every intervening mixed-channel sequence was lost.

Disable removes SDK subscriptions once, closes remote transport, sends best-effort native STOP only for a verified subscription, joins workers with bounded waits, and finalizes only the owned Monitor. It creates no continuing component, polling or worker after shutdown. Exact native hot-toggle resource residue is still version-dependent; baseline claims must distinguish never-enabled from enabled-then-disabled.
