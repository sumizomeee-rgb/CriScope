using System;
using System.IO;
using System.Text.Json;

namespace CriScope.App;

internal sealed class TimeAxisPreferences
{
    private readonly string _path;

    public TimeAxisPreferences(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CriScope", "preferences.json");
    }

    public bool LoadWallTime()
    {
        try
        {
            if (!File.Exists(_path)) return true;
            using var document = JsonDocument.Parse(File.ReadAllText(_path));
            return !document.RootElement.TryGetProperty("timeAxis", out var axis)
                || axis.GetString() != "relative";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return true;
        }
    }

    public void Save(bool wallTime)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
            var temporaryPath = _path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new { timeAxis = wallTime ? "wall" : "relative" }));
            File.Move(temporaryPath, _path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The current choice remains active even when preferences cannot be written.
        }
    }
}
