using System.Text.Json;
using System.Text.Json.Nodes;

namespace AF.DecalFinder;

internal sealed record ImageHistory(string? LastImagePath, IReadOnlyList<string> RecentImages);

internal sealed class AppSettingsStore(string? path = null)
{
    internal string FilePath { get; } = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AF-DecalFinder", "Settings.json");
    private JsonObject ReadDocument() => File.Exists(FilePath)
        ? JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject ?? throw new InvalidDataException("設定ファイルはJSONオブジェクトである必要があります。")
        : new JsonObject();
    private static string? Absolute(string? value) => !string.IsNullOrWhiteSpace(value) && Path.IsPathFullyQualified(value) ? Path.GetFullPath(value) : null;
    private static ImageHistory ReadHistory(JsonObject data)
    {
        var last = Absolute(data["LastImagePath"]?.GetValue<string>());
        var recent = data["RecentImages"] is null ? [] : data["RecentImages"]!.AsArray()
            .Select(n => Absolute(n?.GetValue<string>())).Where(p => p != null).Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray();
        return new(last, recent);
    }
    internal ImageHistory Load() => ReadHistory(ReadDocument());
    internal bool LoadDeleteConfirmation() => ReadDocument()["ConfirmDelete"] is not JsonValue v || !v.TryGetValue<bool>(out var enabled) || enabled;
    internal void SaveDeleteConfirmation(bool enabled) { var data=ReadDocument();data["ConfirmDelete"]=enabled;WriteDocument(data); }
    internal string[] LoadSearchFolders() => ReadDocument()["ImageSearchFolders"] is JsonArray a
        ? a.Select(n=>Absolute(n?.GetValue<string>())).Where(s=>s!=null).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : [];
    internal void SaveSearchFolders(IEnumerable<string> paths)
    {
        var data=ReadDocument();data["ImageSearchFolders"]=new JsonArray(paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Select(s=>(JsonNode?)JsonValue.Create(s)).ToArray());WriteDocument(data);
    }
    internal JsonObject ExportSettings() => ReadDocument();
    internal void ImportPortableSettings(JsonObject source)
    {
        var data=ReadDocument();
        if(source["FrameWidthPx"] is JsonValue v && v.TryGetValue<int>(out var w) && w is >=1 and <=4) data["FrameWidthPx"]=w;
        if(source["ConfirmDelete"] is JsonValue b && b.TryGetValue<bool>(out var enabled)) data["ConfirmDelete"]=enabled;
        WriteDocument(data);
    }
    internal int LoadFrameWidth() => ReadDocument()["FrameWidthPx"] is JsonValue value &&
        value.TryGetValue<int>(out var width) && width is >= 1 and <= 4 ? width : 2;
    internal void SaveFrameWidth(int width)
    {
        if (width is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(width));
        var data = ReadDocument();
        data["FrameWidthPx"] = width;
        WriteDocument(data);
    }
    internal ImageHistory RecordOpened(string imagePath)
    {
        var absolute = Path.GetFullPath(imagePath);
        // Read again to preserve unrelated existing fields; malformed settings are never overwritten.
        var data = ReadDocument();
        var recent = new[] { absolute }.Concat(ReadHistory(data).RecentImages)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray();
        data["LastImagePath"] = absolute;
        data["RecentImages"] = new JsonArray(recent.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray());
        WriteDocument(data);
        return new(absolute, recent);
    }
    internal WindowPlacement? LoadWindow() => ReadDocument()["Window"]?.Deserialize<WindowPlacement>();
    internal void SaveWindow(WindowPlacement placement)
    {
        var data = ReadDocument();
        data["Window"] = JsonSerializer.SerializeToNode(placement);
        WriteDocument(data);
    }
    private void WriteDocument(JsonObject data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, data, new JsonSerializerOptions { WriteIndented = true }); stream.Flush(true); }
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
            else File.Move(temp, FilePath);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
