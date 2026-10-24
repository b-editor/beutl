using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Serialization;

namespace Beutl.ProjectSystem;

// Fingerprints use their own schema; the serializer owns storage and preservation.
internal sealed class MediaFingerprintFile : CoreObject, IStorageVersioned
{
    // In the scene file's folder.
    internal const string FileName = "media-fingerprints.json";

    // Not "MediaFingerprints", which development builds read as the fingerprints themselves.
    internal const string SceneProperty = "MediaFingerprintFile";

    // The format this version reads and writes. A change that this version could not read raises it.
    internal const int CurrentVersion = 1;

    private const string VersionProperty = "Version";
    private const string FingerprintsProperty = "Fingerprints";

    string IStorageVersioned.VersionMember => VersionProperty;

    long IStorageVersioned.SupportedVersion => CurrentVersion;

    public Dictionary<string, MediaFileFingerprint> Fingerprints { get; } = new(StringComparer.Ordinal);

    internal bool IsFromLaterVersion => SuppressedStorageSource is not null;

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

    public override void Serialize(ICoreSerializationContext context)
    {
        context.SetValue(VersionProperty, CurrentVersion);
        context.SetValue(FingerprintsProperty, ToJson(Fingerprints, context.BaseUri));
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        long? version = context.GetValue<JsonNode>(VersionProperty) is JsonValue value
                        && value.GetValueKind() == JsonValueKind.Number
                        && value.TryGetValue(out long number)
            ? number
            : null;
        if (version != CurrentVersion
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

        var file = new MediaFingerprintFile(fingerprints);
        if (file.Fingerprints.Count == 0) return false;
        return CoreSerializer.StoreToUriIfChanged(file, new Uri(GetPath(sceneUri.LocalPath)), CoreSerializationMode.Write);
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
}
