using CriScope.Core;
namespace CriScope.App;

public static class ClientCardPresentation
{
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
