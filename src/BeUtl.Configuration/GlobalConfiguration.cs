using System.Text.Json.Nodes;

namespace BeUtl.Configuration;

public sealed class GlobalConfiguration
{
    public static readonly GlobalConfiguration Instance = new();
    private JsonObject _json = new();

    public static string DefaultFilePath
    {
        get
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".beutl", "settings.json");
        }
    }

    public GraphicsConfig GraphicsConfig { get; } = new();

    public FontConfig FontConfig { get; } = new();

    public ViewConfig ViewConfig { get; } = new();
    
    public ExtensionConfig ExtensionConfig { get; } = new();

    public void Save(string file)
    {
        string dir = Path.GetDirectoryName(file)!;
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _json["font"] = FontConfig.ToJson();
        _json["view"] = ViewConfig.ToJson();
        _json["extension"] = ExtensionConfig.ToJson();

        _json.JsonSave(file);
    }

    public void Restore(string file)
    {
        if (JsonHelper.JsonRestore(file) is JsonObject json)
        {
            FontConfig.FromJson(json["font"]!);
            ViewConfig.FromJson(json["view"]!);
            ExtensionConfig.FromJson(json["extension"]!);

            _json = json;
        }
    }
}
