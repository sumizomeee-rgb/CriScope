# Unity outbound capture bridge

Copy `CriScopeBridge.cs`, `CriScopeNativeRelay.cs`, `CriScopeDiagnostics.cs`, and `CriScopeMonitor.cs` into a Unity project that already uses CRIWARE. No project-specific audio manager hooks are required. Call `CriScope.Unity.CriScopeDiagnostics.Connect("127.0.0.1")` from a debug menu on Unity's main thread; call `Disconnect()` to stop. The previous `SetCaptureEnabled` entry remains compatible. The address is the desktop service, TCP port 18961. `CaptureEnabled`, `Host`, and `Status` are available to that menu. Edit the service address while disabled; enabled capture automatically connects/retries and locks its destination.

The implementation is enabled only in the Editor, a Development Build, or with the explicit `CRISCOPE_DIAGNOSTICS` symbol. Never add this symbol to performance-baseline or production builds. Before the first call there is no created component, Update loop, transport thread, polling or callback subscription. CRI's own In-Game Preview setting is independent: if the host initialized Monitor itself, that native overhead predates this extension.

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
- `block`: first observed or changed block index, sampled every 500 ms. Observed playback IDs come from public CRI components' latest playback and callbacks. This does not enumerate every concurrent native playback or promise exact block-transition time.
- CRI callback-queue overflow warnings are forwarded. Existing callbacks are preserved through event `+=` / `-=`. SDK main-thread dispatch time is not the native Monitor timestamp. The desktop may project callbacks for display using nearby bridge/native clock observations, labels them as estimates, and retains the original channel timestamps.

The native stream remains the authoritative playback/Voice/control source. The SDK extension does not emit synthetic play/pause/stop/AISAC requests.

## CSB3 transport

One outbound TCP connection carries both channels. Handshake: ASCII `CSB3`, 4-byte big-endian JSON length (at most 16 KiB), then UTF-8 hello with `version=3`, `clientId`, `captureId`, `name`, `machine`, `pid`, `platform`. Name defaults to Unity Editor or Unity Player. Client ID is lazily initialized in the process environment and survives toggle/reconnect/domain reload; each network reconnect creates a new capture ID.

Each packet has a 25-byte header: payload length int32 BE, channel byte, transport sequence int64 BE, epoch int32 BE, observed microseconds int64 BE. Payload maximum is 8 MiB. Channel 1 is one complete unmodified CRI frame; 2 is SDK WireEvent JSON; 3 is status JSON `{channel,connected,status}`; 4 is gap JSON `{first,last,count,channel,epoch,captureId,reason}`. SDK event time, native time, and bridge observed time remain distinct.

The sender/native-reader threads share an 8 MiB/8192-frame bounded queue. Overflow discards whole frames, ends the capture segment and carries explicit loss evidence into the next hello; partial native bytes are never forwarded. Lost observations before any capture use first/last/epoch zero and an empty captureId. Native reconnect after prior success ends the whole capture and changes its ID. Native initial failure leaves SDK connected and retries every five seconds. Remote failure stops/closes native subscription before retrying with bounded backoff. There is no replay acknowledgement: a failed in-flight write is reported as delivery uncertain. Gap sequence first/last are bounds, not a claim that every intervening mixed-channel sequence was lost.

Disable removes SDK subscriptions once, closes remote transport, sends best-effort native STOP only for a verified subscription, joins workers with bounded waits, and finalizes only the owned Monitor. It creates no continuing component, polling or worker after shutdown. Exact native hot-toggle resource residue is still version-dependent; baseline claims must distinguish never-enabled from enabled-then-disabled.
