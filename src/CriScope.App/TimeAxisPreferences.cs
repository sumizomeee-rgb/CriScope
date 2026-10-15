using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

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

    public bool LoadLightTheme()
    {
        try
        {
            if (!File.Exists(_path)) return true;
            var document=JsonNode.Parse(File.ReadAllText(_path));
            return document?["theme"]?.GetValue<string>()!="dark";
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        { return true; }
    }

    public void Save(bool wallTime) => SaveValue("timeAxis",wallTime?"wall":"relative");
    public void SaveLightTheme(bool light) => SaveValue("theme",light?"light":"dark");

    private void SaveValue(string key,string value)
    {
        try
        {
            JsonObject values;
            try { values=File.Exists(_path)?JsonNode.Parse(File.ReadAllText(_path)) as JsonObject??new():new(); }
            catch(JsonException) { values=new(); }
            values[key]=value;
            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
            var temporaryPath = _path + ".tmp";
            File.WriteAllText(temporaryPath, values.ToJsonString());
            File.Move(temporaryPath, _path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 无法写入偏好时，本次界面选择仍然生效。
        }
    }
}
