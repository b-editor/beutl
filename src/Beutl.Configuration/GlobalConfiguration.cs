using System.Text.Json.Nodes;

namespace Beutl.Configuration;

public sealed class GlobalConfiguration
{
    public static readonly GlobalConfiguration Instance = new();
    private string? _filePath;
    private JsonObject _json = new();

    public static string DefaultFilePath
    {
        get
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".beutl", "settings.json");
        }
    }

    private GlobalConfiguration()
    {
        AddHandlers();
    }

    public event EventHandler<ConfigurationBase>? ConfigurationChanged;

    public GraphicsConfig GraphicsConfig { get; } = new();

    public FontConfig FontConfig { get; } = new();

    public ViewConfig ViewConfig { get; } = new();

    public ExtensionConfig ExtensionConfig { get; } = new();

    public BackupConfig BackupConfig { get; } = new();

    public void Save(string file)
    {
        try
        {
            _filePath = file;
            RemoveHandlers();
            string dir = Path.GetDirectoryName(file)!;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var fontNode = new JsonObject();
            FontConfig.WriteToJson(fontNode);
            _json["font"] = fontNode;

            var viewNode = new JsonObject();
            ViewConfig.WriteToJson(viewNode);
            _json["view"] = viewNode;

            var extensionNode = new JsonObject();
            ExtensionConfig.WriteToJson(extensionNode);
            _json["extension"] = extensionNode;

            var backupNode = new JsonObject();
            BackupConfig.WriteToJson(backupNode);
            _json["backup"] = backupNode;

            _json.JsonSave(file);
        }
        finally
        {
            AddHandlers();
        }
    }

    public void Restore(string file)
    {
        try
        {
            RemoveHandlers();
            if (JsonHelper.JsonRestore(file) is JsonObject json)
            {
                FontConfig.ReadFromJson((JsonObject)json["font"]!);
                ViewConfig.ReadFromJson((JsonObject)json["view"]!);
                ExtensionConfig.ReadFromJson((JsonObject)json["extension"]!);
                BackupConfig.ReadFromJson((JsonObject)json["backup"]!);

                _json = json;
            }
        }
        finally
        {
            AddHandlers();
        }
    }

    private void AddHandlers()
    {
        GraphicsConfig.ConfigurationChanged += OnConfigurationChanged;
        FontConfig.ConfigurationChanged += OnConfigurationChanged;
        ViewConfig.ConfigurationChanged += OnConfigurationChanged;
        ExtensionConfig.ConfigurationChanged += OnConfigurationChanged;
    }

    private void RemoveHandlers()
    {
        GraphicsConfig.ConfigurationChanged -= OnConfigurationChanged;
        FontConfig.ConfigurationChanged -= OnConfigurationChanged;
        ViewConfig.ConfigurationChanged -= OnConfigurationChanged;
        ExtensionConfig.ConfigurationChanged -= OnConfigurationChanged;
    }

    private void OnConfigurationChanged(object? sender, EventArgs e)
    {
        if (sender is ConfigurationBase config)
        {
            ConfigurationChanged?.Invoke(this, config);
        }

        Save(_filePath ?? DefaultFilePath);
    }
}
