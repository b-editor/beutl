using System.Text.Json.Nodes;

namespace Beutl.Configuration;

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
            RemoveHandlers();
            string dir = Path.GetDirectoryName(file)!;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            JsonNode fontNode = new JsonObject();
            FontConfig.WriteToJson(ref fontNode);
            _json["font"] = fontNode;

            JsonNode viewNode = new JsonObject();
            ViewConfig.WriteToJson(ref viewNode);
            _json["view"] = viewNode;

            JsonNode extensionNode = new JsonObject();
            ExtensionConfig.WriteToJson(ref extensionNode);
            _json["extension"] = extensionNode;
            
            JsonNode backupNode = new JsonObject();
            BackupConfig.WriteToJson(ref backupNode);
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
                FontConfig.ReadFromJson(json["font"]!);
                ViewConfig.ReadFromJson(json["view"]!);
                ExtensionConfig.ReadFromJson(json["extension"]!);
                BackupConfig.ReadFromJson(json["backup"]!);

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

        Save(DefaultFilePath);
    }
}
