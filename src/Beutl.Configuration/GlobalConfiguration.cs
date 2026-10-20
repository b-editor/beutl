using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using Beutl.Logging;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

namespace Beutl.Configuration;

public sealed class GlobalConfiguration
{
    // Not a section: the version of the build that saved the file last.
    private const string VersionKey = "Version";

    // Sections this build no longer has. They are read and dropped instead of being kept for the next save.
    private static readonly string[] s_droppedSections = ["Backup", "backup"];

    public static readonly GlobalConfiguration Instance = new();
    private readonly (string Key, string? LegacyKey, ConfigurationBase Config)[] _sections;
    private string? _filePath;
    // The top-level entries the last Restore found that this build does not know, in the order the file had them.
    private (string Key, JsonNode? Value)[] _unknownSections = [];
    // The file the last Restore could neither read nor copy aside, and why the copy failed.
    private (string File, Exception CopyFailure)? _uncopiedFile;

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
    /// before logging is set up, so each host reports them through <see cref="LogRestoreFailures"/>. A file
    /// that could not be read at all is reported through <see cref="RestoreFileFailure"/> instead.
    /// </summary>
    public IReadOnlyList<ConfigurationRestoreFailure> RestoreFailures { get; private set; } = [];

    /// <summary>
    /// Why the last <see cref="Restore"/> could not read the file at all: it is not valid JSON, repeats a
    /// top-level key, or is not a JSON object. Every setting then keeps its default. Null when the file was
    /// read or does not exist.
    /// </summary>
    public Exception? RestoreFileFailure { get; private set; }

    /// <summary>
    /// Where the last <see cref="Restore"/> copied the file before a save drops what it could not read:
    /// <c>settings.json.bak</c> for unreadable values, <c>settings.json.corrupt-…</c> for an unreadable file.
    /// Null when nothing failed or the copy failed.
    /// </summary>
    public string? RestoreBackupPath { get; private set; }

    public void Save(string file)
    {
        // Replacing a file the last Restore could neither read nor copy aside would lose it.
        if (_uncopiedFile is { } uncopied && FilePathComparison.AreSameCanonicalPath(file, uncopied.File))
            return;

        try
        {
            _filePath = file;
            RemoveHandlers();
            string dir = Path.GetDirectoryName(file)!;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Version names the build that saved the file, which the next start reads as LastStartedVersion, so
            // it is this build's version even when a newer build wrote the file.
            var json = new JsonObject()
            {
                [VersionKey] = BeutlApplication.Version
            };

            foreach ((string key, _, ConfigurationBase config) in _sections)
            {
                json[key] = CoreSerializer.SerializeToJsonObject(config);
            }

            // What a newer build, or an extension that is not loaded, wrote goes back as it was read, after this
            // build's sections and in the order the file had it. A node has one parent, so each save adds a copy.
            foreach ((string key, JsonNode? value) in _unknownSections)
            {
                json[key] = value?.DeepClone();
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
        if (RestoreFileFailure is null && RestoreFailures.Count == 0)
            return;

        ILogger logger = Log.CreateLogger<GlobalConfiguration>();
        if (RestoreFileFailure is { } unreadable)
            logger.LogError(unreadable, "Could not read settings.json, so every setting keeps its default.");

        foreach (ConfigurationRestoreFailure failure in RestoreFailures)
        {
            logger.LogWarning(failure.Exception, "Could not read {Setting} from settings.json, so it keeps its default.", failure.Setting);
        }

        if (RestoreBackupPath is { } backup)
            logger.LogWarning("settings.json as read is kept as {Backup}.", backup);
        else if (_uncopiedFile is { } uncopied)
            logger.LogWarning(uncopied.CopyFailure, "Could not copy settings.json aside, so settings are not saved over it this session.");
        else
            logger.LogWarning("Could not keep a copy of settings.json as read; the values it could not read are dropped at the next save.");
    }

    public void Restore(string file)
    {
        var failures = new List<ConfigurationRestoreFailure>();
        RestoreFileFailure = null;
        RestoreBackupPath = null;
        _unknownSections = [];
        _uncopiedFile = null;
        try
        {
            _filePath = file;
            RemoveHandlers();
            if (!File.Exists(file))
                return;

            if (!TryParse(file, out JsonObject? json, out Exception? unreadable))
            {
                // Nothing can be read, and the next save replaces the whole file, so it is copied aside first.
                RestoreFileFailure = unreadable;
                RestoreBackupPath = KeepUnreadableCopy(file);
                return;
            }

            foreach ((string key, string? legacyKey, ConfigurationBase config) in _sections)
            {
                // A section that has a lower-case legacy key is read from that key when it is present.
                JsonNode? node = legacyKey is null ? json[key] : json[legacyKey] ?? json[key];
                if (node is JsonObject section)
                    RestoreSection(key, config, section, failures);
                else if (node is not null)
                    failures.Add(new ConfigurationRestoreFailure(key, new JsonException($"{key} is not a JSON object.")));
            }

            // Kept for Save. A legacy key is known and not kept: it is read in place of the section's key when
            // present, so writing it back would hide every later change to that section.
            _unknownSections = [.. json.Where(p => !IsKnownKey(p.Key)).Select(p => (p.Key, p.Value?.DeepClone()))];

            if (json[VersionKey] is JsonValue version
                && version.TryGetValue(out string? versionString))
            {
                LastStartedVersion = versionString;
            }

            // The next save drops what could not be read, so the file is kept as it was.
            if (failures.Count > 0)
                RestoreBackupPath = KeepBackup(file);
        }
        finally
        {
            RestoreFailures = failures;
            AddHandlers();
        }
    }

    // Only parse failures are reported; a failure to open the file propagates.
    private static bool TryParse(
        string file, [NotNullWhen(true)] out JsonObject? json, [NotNullWhen(false)] out Exception? failure)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (JsonNode.Parse(stream) is JsonObject root)
            {
                // A JsonObject reads its properties on first use, which throws for a repeated key.
                _ = root.Count;
                json = root;
                failure = null;
                return true;
            }

            failure = new JsonException($"{Path.GetFileName(file)} is not a JSON object.");
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            failure = ex;
        }

        json = null;
        return false;
    }

    private bool IsKnownKey(string key)
        => key == VersionKey
           || s_droppedSections.Contains(key)
           || Array.Exists(_sections, s => s.Key == key || s.LegacyKey == key);

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

    // The UTC time and a random suffix keep copies made in the same second apart.
    private string? KeepUnreadableCopy(string file)
    {
        string copy = string.Create(
            CultureInfo.InvariantCulture,
            $"{file}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}");
        try
        {
            File.Copy(file, copy, overwrite: false);
            return copy;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // The file is gone, so there is nothing left for a save to replace.
            return null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The file stays where it is, and Save leaves it alone this session.
            _uncopiedFile = (file, ex);
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
