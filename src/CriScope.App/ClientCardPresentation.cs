using CriScope.Core;
namespace CriScope.App;

public static class ClientCardPresentation
{
    // Unity's RuntimePlatform is authoritative; the generic transport name is not a product name.
    public static string PlatformKey(Session session)
    {
        if (session.IsReplay) return "replay";
        var platform = session.Platform?.Trim().ToLowerInvariant() ?? "";
        if (platform.EndsWith("editor")) return "unity";
        return platform switch {
            "windowsplayer" or "windows" => "windows",
            "android" => "android",
            "iphoneplayer" or "ios" => "apple",
            "osxplayer" or "macos" or "osx" => "mac",
            "linuxplayer" or "linux" => "linux",
            "webglplayer" or "webgl" => "web",
            _ => "unknown"
        };
    }
    public static string PlatformLabel(Session session) => PlatformKey(session) switch {
        "replay" => "本地回放", "unity" => "Unity Editor", "windows" => "Windows 包",
        "android" => "Android", "apple" => "iOS", "mac" => "macOS 包",
        "linux" => "Linux 包", "web" => "WebGL",
        _ => string.IsNullOrWhiteSpace(session.Platform) ? "未知平台" : session.Platform.Trim()
    };
    public static string Title(Session session)
    {
        var name = session.Name?.Trim() ?? "";
        return name.Length == 0 || name.Equals("Unity Player", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Unity Editor", StringComparison.OrdinalIgnoreCase)
            ? PlatformLabel(session) : name;
    }
    public static string Key(Session session) => !session.IsReplay && !string.IsNullOrEmpty(session.ClientId) ? "client:" + session.ClientId : "session:" + session.Id;
    public static string Channel(Session session) => !string.IsNullOrEmpty(session.Channel) ? session.Channel : (session.Source.Contains("native", StringComparison.OrdinalIgnoreCase) || session.Source == "CRI Monitor") ? "native" : "sdk";
    public static Session[][] Group(IEnumerable<Session> sessions) => sessions.GroupBy(Key).Select(g => g.ToArray()).ToArray();
    public static Session Default(Session[] sessions) => sessions.OrderByDescending(s => s.Connected).ThenByDescending(s => Channel(s) == "native").ThenByDescending(s => Array.IndexOf(sessions, s)).First();
    public static string Address(Session session)
    {
        if(session.IsReplay)return "本地日志";
        var endpoint=session.Endpoint;
        if(System.Net.IPEndPoint.TryParse(endpoint,out var address))return address.Address.ToString();
        return string.IsNullOrWhiteSpace(endpoint)?"IP 未提供":endpoint;
    }
    public static string Status(Session[] sessions) => sessions.All(s => s.IsReplay) ? "日志" : sessions.Any(s => s.Recording) ? "记录中" : sessions.Any(s => s.Connected) ? "在线" : "已断开";
}
