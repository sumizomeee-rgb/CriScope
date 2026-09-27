CriScope · 游戏接入与日志位置
========================

1. 运行本目录的 CriScope.exe。软件监听 TCP 18961，游戏可在同机或局域网中连接。
2. Unity 工程须已接入 CRIWARE。将 CriScopeBridge.cs、CriScopeNativeRelay.cs、
   CriScopeDiagnostics.cs、CriScopeMonitor.cs 及各自 .meta 放进客户端工程。
   完整实现与版本说明见 CriScope 源码仓库 integrations/unity/README.md。
3. 仅在 Unity Editor、Development Build 或明确启用 CRISCOPE_DIAGNOSTICS 的
   调试构建中，等 CRI Atom 初始化后从主线程调用：

       CriScope.Unity.CriScopeDiagnostics.Connect("127.0.0.1");

   跨机器连接时改为运行 CriScope 的电脑地址。关闭时调用 Disconnect()。
   可读取 CaptureEnabled、Host、Status 查看连接状态。

可以把 Connect()/Disconnect() 接到游戏已有的调试菜单；无需给播放、暂停、
AISAC 等业务接口增设采集点。采集实现应放在实际运行的客户端工程中。

原生 Monitor 使用游戏进程本机 TCP 2002。若端口被别的进程占用，软件会显示
原生通道不可用状态，仍可接收 SDK 补充信息。不要把 2002 映射成远端共享端口。

连接建立后，CriScope 自动按通道创建物理日志：

       <本目录>\.local\recordings\

菜单“打开日志目录”可直达此处；“导入日志…”以只读历史会话打开 .criscope 文件。
可通过环境变量 CRISCOPE_DATA 指定其他日志目录。实时视图只保留近期内存窗口，
更早的数据从物理日志导入查看。Bus 性能快照仅由 Cue、Voice 或控制事件触发
写盘，导入后的资源曲线属于稀疏采样。

发布时请分发整个 CriScope-win-x64 文件夹或 ZIP，不要只复制 EXE。
