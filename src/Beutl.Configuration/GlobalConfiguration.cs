using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

using Beutl.Serialization;

namespace Beutl.Configuration;

public sealed class GlobalConfiguration
{
    public static readonly GlobalConfiguration Instance = new();
    private readonly (string Key, string? LegacyKey, ConfigurationBase Config)[] _sections;
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
        // In the order Save writes the sections and Restore reads them.
        _sections =
        [
            ("Font", "font", FontConfig),
            ("View", "view", ViewConfig),
            ("Extension", "extension", ExtensionConfig),
            ("Backup", "backup", BackupConfig),
            ("Telemetry", "telemetry", TelemetryConfig),
            ("Editor", null, EditorConfig),
            ("Graphics", null, GraphicsConfig),
            ("Tutorial", null, TutorialConfig),
            ("AiAgent", null, AiAgentConfig),
            ("ProxyStore", null, ProxyStoreConfig),
            ("VersionControl", null, VersionControlConfig),
        ];
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

    public TutorialConfig TutorialConfig { get; } = new();

    public AiAgentConfig AiAgentConfig { get; } = new();

    public ProxyStoreConfig ProxyStoreConfig { get; } = new();

    public VersionControlConfig VersionControlConfig { get; } = new();

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

            foreach ((string key, _, ConfigurationBase config) in _sections)
            {
                json[key] = CoreSerializer.SerializeToJsonObject(config);
            }

            // Pending migration can still include a legacy live MCP bearer token.
            json.JsonSave(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
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
            _filePath = file;
            RemoveHandlers();
            if (JsonHelper.JsonRestore(file) is JsonObject json)
            {
                static void Deserialize(ICoreSerializable serializable, JsonObject obj)
                {
                    CoreSerializer.PopulateFromJsonObject(serializable, obj);
                }

                foreach ((string key, string? legacyKey, ConfigurationBase config) in _sections)
                {
                    // A section that has a lower-case legacy key is read from that key when it is present.
                    JsonNode? node = legacyKey is null ? json[key] : json[legacyKey] ?? json[key];
                    if (node is JsonObject section)
                        Deserialize(config, section);
                }

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
        foreach ((_, _, ConfigurationBase config) in _sections)
            config.ConfigurationChanged += OnConfigurationChanged;
    }

    private void RemoveHandlers()
    {
        foreach ((_, _, ConfigurationBase config) in _sections)
            config.ConfigurationChanged -= OnConfigurationChanged;
    }

    private void OnConfigurationChanged(object? sender, EventArgs e)
    {
        if (sender is ConfigurationBase config)
        {
            ConfigurationChanged?.Invoke(this, config);
        }

        // Only auto-save to a file this instance restored or saved. A process that never restored the
        // user's settings (tests, helper hosts) holds defaults and would otherwise overwrite settings.json.
        if (_filePath is { } file)
        {
            Save(file);
        }
    }
}
