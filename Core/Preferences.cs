using System.Text.Json;
using System.Text.Json.Nodes;

namespace AntCloud.Core;

/// Small key-value store in %APPDATA%\AntCloud\settings.json; the Windows counterpart of UserDefaults.
static class Preferences
{
    private static readonly string FilePath = Path.Combine(Config.DataDir, "settings.json");
    private static readonly object Gate = new();
    private static JsonObject? cache;

    private static JsonObject Root
    {
        get
        {
            if (cache != null) return cache;
            try { cache = JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject; }
            catch (Exception e) when (e is IOException or JsonException) { }
            return cache ??= new JsonObject();
        }
    }

    public static T? Get<T>(string key)
    {
        lock (Gate)
        {
            try { return Root[key] is { } node ? node.Deserialize<T>() : default; }
            catch (Exception e) when (e is JsonException or InvalidOperationException) { return default; }
        }
    }

    public static void Set<T>(string key, T value)
    {
        lock (Gate)
        {
            Root[key] = JsonSerializer.SerializeToNode(value);
            try { File.WriteAllText(FilePath, Root.ToJsonString(new JsonSerializerOptions { WriteIndented = true })); }
            catch (IOException e) { Log.Info($"[prefs] couldn't save: {e.Message}"); }
        }
    }
}
