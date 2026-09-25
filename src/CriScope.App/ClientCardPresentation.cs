using CriScope.Core;
namespace CriScope.App;

public static class ClientCardPresentation
{
    public static string Key(Session session) => !session.IsReplay && !string.IsNullOrEmpty(session.ClientId) ? "client:" + session.ClientId : "session:" + session.Id;
    public static string Channel(Session session) => !string.IsNullOrEmpty(session.Channel) ? session.Channel : (session.Source.Contains("native", StringComparison.OrdinalIgnoreCase) || session.Source == "CRI Monitor") ? "native" : "sdk";
    public static Session[][] Group(IEnumerable<Session> sessions) => sessions.GroupBy(Key).Select(g => g.ToArray()).ToArray();
    public static Session Default(Session[] sessions) => sessions.OrderByDescending(s => s.Connected).ThenByDescending(s => Channel(s) == "native").ThenByDescending(s => Array.IndexOf(sessions, s)).First();
}
