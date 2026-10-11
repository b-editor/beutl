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

    /// <summary>
    /// The settings the last <see cref="Restore"/> could not read, which kept their defaults. Restore runs
    /// before logging is set up, so the caller reports them.
    /// </summary>
    public IReadOnlyList<ConfigurationRestoreFailure> RestoreFailures { get; private set; } = [];

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
        var failures = new List<ConfigurationRestoreFailure>();
        try
        {
            _filePath = file;
            RemoveHandlers();
            if (JsonHelper.JsonRestore(file) is JsonObject json)
            {
                foreach ((string key, string? legacyKey, ConfigurationBase config) in _sections)
                {
                    // A section that has a lower-case legacy key is read from that key when it is present.
                    JsonNode? node = legacyKey is null ? json[key] : json[legacyKey] ?? json[key];
                    if (node is JsonObject section)
                        RestoreSection(key, config, section, failures);
                }

                if (json["Version"] is JsonValue version
                    && version.TryGetValue(out string? versionString))
                {
                    LastStartedVersion = versionString;
                }

                // The next save drops what could not be read, so the file is kept as it was.
                if (failures.Count > 0)
                    KeepBackup(file);
            }
        }
        finally
        {
            RestoreFailures = failures;
            AddHandlers();
        }
    }

    // A setting that cannot be read keeps its default, and the sections after it are still read.
    private static void RestoreSection(
        string key, ConfigurationBase config, JsonObject section, List<ConfigurationRestoreFailure> failures)
    {
        Exception? sectionFailure = null;
        try
        {
            CoreSerializer.PopulateFromJsonObject(config, section);
        }
        catch (Exception ex)
        {
            // Thrown by a section's own reads, after its registered settings were read.
            sectionFailure = ex;
        }

        foreach ((string property, Exception exception) in config.TakeDeserializeFailures())
            failures.Add(new ConfigurationRestoreFailure($"{key}.{property}", exception));

        if (sectionFailure is not null)
            failures.Add(new ConfigurationRestoreFailure(key, sectionFailure));
    }

    private static void KeepBackup(string file)
    {
        try
        {
            File.Copy(file, file + ".bak", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: the settings that could be read are restored either way.
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
