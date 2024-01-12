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
    public string LastStartedVersion { get; private set; } = GitVersionInformation.SemVer;

    public void Save(string file)
    {
        try
        {
            static void Serialize(ICoreSerializable serializable, JsonObject obj)
            {
                var context = new JsonSerializationContext(serializable.GetType(), NullSerializationErrorNotifier.Instance, json: obj);
                using (ThreadLocalSerializationContext.Enter(context))
                {
                    serializable.Serialize(context);
                }
            }

            _filePath = file;
            RemoveHandlers();
            string dir = Path.GetDirectoryName(file)!;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = new JsonObject()
            {
                ["Version"] = GitVersionInformation.NuGetVersionV2
            };

            var fontNode = new JsonObject();
            Serialize(FontConfig, fontNode);
            json["Font"] = fontNode;

            var viewNode = new JsonObject();
            Serialize(ViewConfig, viewNode);
            json["View"] = viewNode;

            var extensionNode = new JsonObject();
            Serialize(ExtensionConfig, extensionNode);
            json["Extension"] = extensionNode;

            var backupNode = new JsonObject();
            Serialize(BackupConfig, backupNode);
            json["Backup"] = backupNode;
            
            var telemetryNode = new JsonObject();
            Serialize(TelemetryConfig, telemetryNode);
            json["Telemetry"] = telemetryNode;
            
            var editorNode = new JsonObject();
            Serialize(EditorConfig, editorNode);
            json["Editor"] = editorNode;

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
                    var context = new JsonSerializationContext(
                        serializable.GetType(), NullSerializationErrorNotifier.Instance, json: obj);
                    using (ThreadLocalSerializationContext.Enter(context))
                    {
                        serializable.Deserialize(context);
                    }
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

                if (json["Version"] is JsonValue version)
                {
                    if (version.TryGetValue(out string? versionString))
                    {
                        LastStartedVersion = versionString;
                    }
                }
                else
                {
                    // Todo: 互換性維持のコード
                    LastStartedVersion = "1.0.0-preview.1";
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
