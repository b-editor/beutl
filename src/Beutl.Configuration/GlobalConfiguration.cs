using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

using Beutl.Serialization;

namespace Beutl.Configuration;

public sealed class GlobalConfiguration
{
    public static readonly GlobalConfiguration Instance = new();
    private string? _filePath;

    public static string DefaultFilePath
    {
        get
        {
            return Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "settings.json");
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

    public TelemetryConfig TelemetryConfig { get; } = new();

    public EditorConfig EditorConfig { get; } = new();

    [AllowNull]
    public string LastStartedVersion { get; private set; } = BeutlApplication.Version;

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

            var json = new JsonObject()
            {
                ["Version"] = BeutlApplication.Version
            };

            json["Font"] = CoreSerializer.SerializeToJsonObject(FontConfig);

            json["View"] = CoreSerializer.SerializeToJsonObject(ViewConfig);

            json["Extension"] = CoreSerializer.SerializeToJsonObject(ExtensionConfig);

            json["Backup"] = CoreSerializer.SerializeToJsonObject(BackupConfig);

            json["Telemetry"] = CoreSerializer.SerializeToJsonObject(TelemetryConfig);

            json["Editor"] = CoreSerializer.SerializeToJsonObject(EditorConfig);

            json["Graphics"] = CoreSerializer.SerializeToJsonObject(GraphicsConfig);

            json.JsonSave(file);
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
                JsonNode? GetNode(string name1, string name2)
                {
                    if (json[name1] is JsonNode node1)
                        return node1;
                    else if (json[name2] is JsonNode node2)
                        return node2;
                    else
                        return null;
                }
                static void Deserialize(ICoreSerializable serializable, JsonObject obj)
                {
                    CoreSerializer.PopulateFromJsonObject(serializable, obj);
                }

                if (GetNode("font", "Font") is JsonObject font)
                    Deserialize(FontConfig, font);

                if (GetNode("view", "View") is JsonObject view)
                    Deserialize(ViewConfig, view);

                if (GetNode("extension", "Extension") is JsonObject extension)
                    Deserialize(ExtensionConfig, extension);

                if (GetNode("backup", "Backup") is JsonObject backup)
                    Deserialize(BackupConfig, backup);

                if (GetNode("telemetry", "Telemetry") is JsonObject telemetry)
                    Deserialize(TelemetryConfig, telemetry);

                if (json["Editor"] is JsonObject editor)
                    Deserialize(EditorConfig, editor);

                if (json["Graphics"] is JsonObject graphics)
                    Deserialize(GraphicsConfig, graphics);

                if (json["Version"] is JsonValue version
                    && version.TryGetValue(out string? versionString))
                {
                    LastStartedVersion = versionString;
                }
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
        TelemetryConfig.ConfigurationChanged += OnConfigurationChanged;
        EditorConfig.ConfigurationChanged += OnConfigurationChanged;
    }

    private void RemoveHandlers()
    {
        GraphicsConfig.ConfigurationChanged -= OnConfigurationChanged;
        FontConfig.ConfigurationChanged -= OnConfigurationChanged;
        ViewConfig.ConfigurationChanged -= OnConfigurationChanged;
        ExtensionConfig.ConfigurationChanged -= OnConfigurationChanged;
        TelemetryConfig.ConfigurationChanged -= OnConfigurationChanged;
        EditorConfig.ConfigurationChanged -= OnConfigurationChanged;
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
