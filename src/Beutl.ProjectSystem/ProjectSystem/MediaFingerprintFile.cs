using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Logging;
using Beutl.Serialization;
using Beutl.Utilities;
using Microsoft.Extensions.Logging;

namespace Beutl.ProjectSystem;

// A scene's media fingerprints, which the missing-media repair tool matches files by, in a JSON file in the scene's
// folder (Intro/Intro.scene keeps Intro/media-fingerprints.json) that the scene file references under
// "MediaFingerprintFile". The name is fixed because every scene gets a folder of its own. The file maps each media
// path, relative to it, to the file's length and SHA-256, in ordinal key order, and the editor writes it only when
// that changes. A scene without fingerprints has no file, and a file with a later Version is kept as it is.
internal sealed class MediaFingerprintFile : CoreObject
{
    // In the scene file's folder.
    internal const string FileName = "media-fingerprints.json";

    // Not "MediaFingerprints", which development builds read as the fingerprints themselves.
    internal const string SceneProperty = "MediaFingerprintFile";

    // The format this version reads and writes. A change that this version could not read raises it.
    internal const int CurrentVersion = 1;

    private const string VersionProperty = "Version";
    private const string FingerprintsProperty = "Fingerprints";

    private static readonly ILogger s_logger = Log.CreateLogger<MediaFingerprintFile>();

    // The version of the file this object was read from, when a later version of Beutl wrote it.
    private long? _laterVersion;

    // Keyed by absolute URI. Always empty in an object read from a file with a later format version.
    public Dictionary<string, MediaFileFingerprint> Fingerprints { get; } = new(StringComparer.Ordinal);

    internal bool IsFromLaterVersion => _laterVersion is not null;

    public MediaFingerprintFile()
    {
    }

    internal MediaFingerprintFile(IEnumerable<KeyValuePair<string, MediaFileFingerprint>> fingerprints)
    {
        foreach ((string key, MediaFileFingerprint fingerprint) in fingerprints)
        {
            Fingerprints[key] = fingerprint;
        }
    }

    internal static string GetPath(string scenePath) => Path.Combine(Path.GetDirectoryName(scenePath) ?? string.Empty, FileName);

    // The format version and the fingerprints, keyed relative to the file: Id and Name mean nothing here. An object
    // from a later version's file is never written this way, since its file keeps the bytes it was read with, and an
    // embedded copy of it keeps only that version.
    public override void Serialize(ICoreSerializationContext context)
    {
        context.SetValue(VersionProperty, _laterVersion ?? CurrentVersion);
        if (_laterVersion is null)
            context.SetValue(FingerprintsProperty, ToJson(Fingerprints, context.BaseUri));
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        long? version = context.GetValue<JsonNode>(VersionProperty) is JsonValue value
                        && value.GetValueKind() == JsonValueKind.Number
                        && value.TryGetValue(out long number)
            ? number
            : null;
        if (version > CurrentVersion)
        {
            _laterVersion = version;
            // Nothing else of a later format is read, or kept to be written back: such a file is never rewritten, and
            // an embedded copy holds only the version. Taking the JSON object tells the serializer that this object
            // handles every key itself.
            (context as IJsonSerializationContext)?.GetJsonObject();
        }
        else if (version != CurrentVersion
                 || context.GetValue<JsonNode>(FingerprintsProperty) is not JsonObject stored)
        {
            throw new JsonException($"The media fingerprints are not in format {CurrentVersion}.");
        }
        else
        {
            foreach ((string key, MediaFileFingerprint? fingerprint)
                     in stored.Deserialize<Dictionary<string, MediaFileFingerprint?>>(JsonHelper.SerializerOptions) ?? [])
            {
                if (!IsComplete(fingerprint)) continue;
                Uri uri;
                try { uri = UriHelper.ResolvePersistedReference(key, context.BaseUri); }
                catch (JsonException) { continue; }
                if (uri.IsFile) Fingerprints[uri.AbsoluteUri] = fingerprint;
            }
        }
    }

    // Reads the reference or, in an embedded scene, the object itself. Null when there are none or the file is missing
    // or unreadable: the fingerprints only help the repair tool, so the scene opens without them.
    internal static MediaFingerprintFile? Load(JsonNode? stored, Uri? baseUri)
    {
        Uri? source = baseUri;
        try
        {
            var file = new MediaFingerprintFile();
            if (stored is JsonObject embedded)
            {
                CoreSerializer.PopulateFromJsonObject(file, embedded);
            }
            else if (stored is JsonValue reference && reference.TryGetValue(out string? value))
            {
                source = UriHelper.ResolvePersistedReference(value, baseUri);
                if (!source.IsFile || !File.Exists(source.LocalPath)) return null;
                CoreSerializer.PopulateFromUri(file, source);
                if (file.IsFromLaterVersion)
                {
                    // A save of the scene that stores this object keeps the file as it is instead of writing this
                    // version's format over it.
                    file.SuppressedStorageSource =
                        new SuppressedStorageSource(File.ReadAllBytes(source.LocalPath), source);
                    s_logger.LogWarning(
                        "The media fingerprints in {Uri} have format version {Version}, which this version of Beutl does not read; the scene opens without them, and the file is left as it is.",
                        source,
                        file._laterVersion);
                }
            }
            else
            {
                return null;
            }

            return file.Fingerprints.Count > 0 || file.IsFromLaterVersion ? file : null;
        }
        catch (Exception ex) when (!ExceptionHelpers.ContainsFatalFailure(ex))
        {
            s_logger.LogWarning(ex, "Could not read the media fingerprints from {Uri}; the scene opens without them.", source);
            return null;
        }
    }

    // Writes the fingerprints to fileUri, or removes the file when there are none, unless it already holds them or a
    // later version wrote it. Returns whether the file changed.
    internal bool Save(Uri fileUri)
    {
        if (IsFromLaterVersion) return false;
        string path = fileUri.LocalPath;
        JsonObject? json = Fingerprints.Count == 0
            ? null
            : CoreSerializer.SerializeToJsonObject(this, new CoreSerializerOptions { BaseUri = fileUri, Mode = CoreSerializationMode.Write });
        if (File.Exists(path))
        {
            using JsonDocument? current = TryParse(path);
            if (current is not null && GetVersion(current.RootElement) > CurrentVersion)
                return false;
            if (current is not null && json is not null
                && JsonElement.DeepEquals(current.RootElement, JsonSerializer.SerializeToElement(json, JsonHelper.SerializerOptions)))
                return false;
        }
        else if (json is null)
        {
            return false;
        }

        if (json is null)
        {
            // Journaled like a replacement, so a failed save puts the file back.
            StorageWriteTransaction.Delete(path);
        }
        else
        {
            CoreSerializer.StoreToUri(this, fileUri, CoreSerializationMode.Write);
        }

        return true;
    }

    // The editor's background update: saves the fingerprints without serializing the scene's unsaved edits, only for a
    // scene file that references the file, and returns whether it changed.
    internal static bool Update(Uri sceneUri, Guid sceneId, IEnumerable<KeyValuePair<string, MediaFileFingerprint>> fingerprints)
    {
        if (!File.Exists(sceneUri.LocalPath)) return false;
        JsonObject scene;
        using (FileStream stream = File.OpenRead(sceneUri.LocalPath))
            scene = JsonNode.Parse(stream) as JsonObject ?? throw new JsonException("The scene file is not a JSON object.");
        // The file can hold another scene by now, for example after a checkout.
        if (!Guid.TryParse(scene[nameof(CoreObject.Id)]?.GetValue<string>(), out Guid id) || id != sceneId
            || scene[SceneProperty] is null)
            return false;

        return new MediaFingerprintFile(fingerprints).Save(new Uri(GetPath(sceneUri.LocalPath)));
    }

    // Skips a null entry or one without a hash, at any depth, which a hand edit can leave: a save would fail on the
    // null, and an entry without a hash never matches.
    private static bool IsComplete([NotNullWhen(true)] MediaFileFingerprint? fingerprint)
        => fingerprint is not null
           && !string.IsNullOrEmpty(fingerprint.Sha256)
           && (fingerprint.Dependencies is not { } dependencies || dependencies.Values.All(IsComplete));

    // Keys relative to the file and in ordinal order, a model's dependencies too, so the text does not depend on
    // the order the files were scanned in.
    private static JsonObject ToJson(IEnumerable<KeyValuePair<string, MediaFileFingerprint>> fingerprints, Uri? fileUri)
    {
        var entries = new SortedDictionary<string, MediaFileFingerprint>(StringComparer.Ordinal);
        foreach ((string key, MediaFileFingerprint fingerprint) in fingerprints)
        {
            entries.Add(UriHelper.ToSerializedUri(new Uri(key), fileUri).ToString(), fingerprint);
        }

        return ToJson(entries);
    }

    private static JsonObject ToJson(IEnumerable<KeyValuePair<string, MediaFileFingerprint>> sortedEntries)
    {
        var json = new JsonObject();
        foreach ((string key, MediaFileFingerprint fingerprint) in sortedEntries)
        {
            JsonObject entry = JsonSerializer.SerializeToNode(
                fingerprint with { Dependencies = null }, JsonHelper.SerializerOptions)!.AsObject();
            if (fingerprint.Dependencies is { } dependencies)
            {
                entry[nameof(MediaFileFingerprint.Dependencies)] =
                    ToJson(dependencies.OrderBy(pair => pair.Key, StringComparer.Ordinal));
            }

            json[key] = entry;
        }

        return json;
    }

    private static long? GetVersion(JsonElement root)
        => root.ValueKind == JsonValueKind.Object
           && root.TryGetProperty(VersionProperty, out JsonElement version)
           && version.ValueKind == JsonValueKind.Number
           && version.TryGetInt64(out long value)
            ? value
            : null;

    // Null when the file is not JSON, for example when a merge left conflict markers in it. Read through
    // JsonDocument, which, unlike JsonNode, also takes a hand edit that repeats a key.
    private static JsonDocument? TryParse(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            return JsonDocument.Parse(stream);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
