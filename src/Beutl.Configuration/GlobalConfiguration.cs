using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

using Beutl.Logging;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

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
    /// before logging is set up, so each host reports them through <see cref="LogRestoreFailures"/>.
    /// </summary>
    public IReadOnlyList<ConfigurationRestoreFailure> RestoreFailures { get; private set; } = [];

    /// <summary>
    /// Where the last <see cref="Restore"/> kept the file as it read it, because the next save drops what
    /// <see cref="RestoreFailures"/> lists. Null when nothing failed or the copy could not be written.
    /// </summary>
    public string? RestoreBackupPath { get; private set; }

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

    /// <summary>Logs what the last <see cref="Restore"/> could not read; call it once logging is set up.</summary>
    public void LogRestoreFailures()
    {
        if (RestoreFailures.Count == 0)
            return;

        ILogger logger = Log.CreateLogger<GlobalConfiguration>();
        foreach (ConfigurationRestoreFailure failure in RestoreFailures)
        {
            logger.LogWarning(failure.Exception, "Could not read {Setting} from settings.json, so it keeps its default.", failure.Setting);
        }

        if (RestoreBackupPath is { } backup)
            logger.LogWarning("settings.json as read is kept as {Backup}.", backup);
        else
            logger.LogWarning("Could not keep a copy of settings.json as read; the values it could not read are dropped at the next save.");
    }

    public void Restore(string file)
    {
        var failures = new List<ConfigurationRestoreFailure>();
        RestoreBackupPath = null;
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
                    else if (node is not null)
                        failures.Add(new ConfigurationRestoreFailure(key, new JsonException($"{key} is not a JSON object.")));
                }

                if (json["Version"] is JsonValue version
                    && version.TryGetValue(out string? versionString))
                {
                    LastStartedVersion = versionString;
                }

                // The next save drops what could not be read, so the file is kept as it was.
                if (failures.Count > 0)
                    RestoreBackupPath = KeepBackup(file);
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
            // Not expected: a section reads its own values tolerantly too. Its settings read so far stay.
            sectionFailure = ex;
        }

        foreach ((string property, Exception exception) in config.TakeDeserializeFailures())
            failures.Add(new ConfigurationRestoreFailure($"{key}.{property}", exception));

        if (sectionFailure is not null)
            failures.Add(new ConfigurationRestoreFailure(key, sectionFailure));
    }

    private static string? KeepBackup(string file)
    {
        string backup = file + ".bak";
        try
        {
            File.Copy(file, backup, overwrite: true);
            return backup;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The settings that could be read are restored either way; the caller reports the missing copy.
            return null;
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
