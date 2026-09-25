# Unity SDK extension

Copy `CriScopeBridge.cs`, `CriScopeDiagnostics.cs`, and `CriScopeMonitor.cs` into a Unity project that already uses CRIWARE. No project-specific audio manager hooks are required. Call `CriScope.Unity.CriScopeDiagnostics.SetCaptureEnabled(true, "127.0.0.1")` from a debug menu on Unity's main thread; call it with `false` to stop. `CaptureEnabled`, `Host`, and `Status` are available to that menu.

The implementation is enabled only in the Editor, a Development Build, or with the explicit `CRISCOPE_DIAGNOSTICS` symbol. Never add this symbol to performance-baseline or production builds. Before the first call there is no created component, Update loop, transport thread, polling or callback subscription. CRI's own In-Game Preview setting is independent: if the host initialized Monitor itself, that native overhead predates this extension.

## Monitor ownership

On Windows the extension inspects the current process's port 2002 listener without making a connection. An existing Monitor is reused and never finalized by this extension. Stopping reports that it was preserved. Pure native CriScope capture can connect to that Monitor without enabling this SDK extension.

If there is no listener, the Windows x64 adapter checks the loaded DLL SHA-256 and instruction bytes before calling the verified private Monitor wrappers. Unknown DLLs are refused with a status reason; enable IGP at initialization or supply a separately verified adapter. The adapter never patches DLL code, changes ports, or creates another Atom instance. It finalizes only the Monitor it started. Enable/disable is synchronous on the Unity main thread and may briefly stall frames/audio; do not toggle in a latency-sensitive measurement interval.

This adapter is version-specific, not an official CRI API. It does not promise zero process-memory/CPU residue after prior use. An untouched performance baseline requires IGP disabled at Atom initialization and this extension never enabled.

## Observations

- `memory.atom.bytes`: CRI Atom allocator bytes; `memory.fs.bytes`: CRI FS allocator bytes. These are separate, not a fabricated all-audio total.
- `voices.streaming.used` / `.capacity`: the **StandardStreaming** pool only.
- `beat`: `value` is BPM; detail includes bar, beat, beats per bar and offset.
- `sequence`: tag is name, embedded event ID is value; sequence position is retained in detail.
- `block`: first observed or changed block index, sampled every 500 ms. Observed playback IDs come from public CRI components' latest playback and callbacks. This does not enumerate every concurrent native playback or promise exact block-transition time.
- CRI callback-queue overflow warnings are forwarded. Existing callbacks are preserved through event `+=` / `-=`. SDK main-thread dispatch time is not the native Monitor timestamp; no false temporal alignment is implied.

The receiver hello uses `CRI SDK`, process ID, and Unity platform. The native stream remains the authoritative playback/Voice/control source. The SDK extension does not emit synthetic play/pause/stop/AISAC requests.
