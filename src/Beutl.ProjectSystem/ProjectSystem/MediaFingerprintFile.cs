using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Logging;
using Beutl.Serialization;
using Beutl.Utilities;
using Microsoft.Extensions.Logging;

namespace Beutl.ProjectSystem;

// A scene's media fingerprints, stored like any other file-backed object: in a JSON file of their own, which the
// scene file references under "MediaFingerprintFile". The file is in the scene's folder, so the scene file stays
// readable: for Intro/Intro.scene it is Intro/media-fingerprints.json. The name is fixed because a folder belongs
// to one scene. Every way Beutl creates a scene gives it a new folder of its own: the New Scene and New Project
// dialogs refuse a folder that exists (NewDocumentValidation.CanCreateAt), and the agent toolkit's add_scene
// reserves a folder no other scene uses. A scene also loads every **/*.belm under its folder, so two scenes in
// one folder would load each other's elements.
//
// The file maps each media file the scene references, by its path relative to the file, to the file's length
// and SHA-256, and for a 3D model also to those of the files the model loads. Version is the format's version:
//
//   {
//     "Version": 1,
//     "Fingerprints": {
//       "../footage/clip.mp4": {
//         "Length": 1048576,
//         "Sha256": "9F86D081884C7D65..."
//       }
//     },
//     "$type": "[Beutl.ProjectSystem]:MediaFingerprintFile"
//   }
//
// The missing-media repair tool uses them to check a candidate's content, find a renamed file and tell whether
// a relinked model is the same asset, so the file belongs to the project like the scene file: it moves and
// copies with the scene folder, and version control commits it. Keys are in ordinal order, and the file is
// written only when its content changes, so neither the order of a scan nor a media file whose timestamp alone
// changed rewrites it. A scene without fingerprints has no object of this type (Scene.MediaFingerprintFile), so
// its scene file references no file and a save removes the one in its folder. A file with a later format version
// is neither used nor rewritten nor deleted, so an older Beutl does not destroy what a newer one wrote: the scene
// keeps an object for it that holds no fingerprints, and its scene file keeps the reference.
internal sealed class MediaFingerprintFile : CoreObject
{
    // In the scene file's folder. Renaming the file takes only this constant.
    internal const string FileName = "media-fingerprints.json";

    // The scene file's key for the reference. Development builds since #2666 read "MediaFingerprints" there as
    // the fingerprints themselves and would fail on a reference.
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

    internal static string GetPath(string scenePath) => Path.Combine(Path.GetDirectoryName(scenePath) ?? string.Empty, FileName);

    // The format version and the fingerprints, keyed relative to the file: Id and Name mean nothing here. An object
    // from a later version's file is never saved, and an embedded copy of it keeps only that version.
    public override void Serialize(ICoreSerializationContext context)
    {
        context.SetValue(VersionProperty, _laterVersion ?? CurrentVersion);
        if (_laterVersion is null)
            context.SetValue(FingerprintsProperty, ToJson(Fingerprints, context.BaseUri));
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        var fingerprints = new Dictionary<string, MediaFileFingerprint>(StringComparer.Ordinal);
        long? version = context.GetValue<JsonNode>(VersionProperty) is JsonValue value
                        && value.GetValueKind() == JsonValueKind.Number
                        && value.TryGetValue(out long number)
            ? number
            : null;
        _laterVersion = null;
        if (version > CurrentVersion)
        {
            _laterVersion = version;
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
                if (uri.IsFile) fingerprints[uri.AbsoluteUri] = fingerprint;
            }
        }

        Fingerprints.Clear();
        foreach ((string key, MediaFileFingerprint fingerprint) in fingerprints)
        {
            Fingerprints.Add(key, fingerprint);
        }
    }

    // Reads what a scene file holds under SceneProperty: a reference to the file or, in an embedded serialization
    // of the scene, the fingerprints themselves. Null when there are none: a missing file reads as none, and so does
    // an unreadable one, which only helps the repair tool, so the scene opens without it and the next save removes
    // or replaces it. A file with a later format version reads as an object without fingerprints.
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

    // Writes the fingerprints to fileUri, or removes the file there when there are none, unless the file already
    // holds them or a later version of Beutl wrote it. An object read from such a file writes nothing. Returns
    // whether the file changed. A save of the scene calls it rather than letting the serializer store the file,
    // which rewrites it every time.
    internal bool Save(Uri fileUri)
    {
        if (IsFromLaterVersion) return false;
        Uri = fileUri;
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

    // The editor's background update after a scan. It saves the fingerprints of the saved scene graph without
    // serializing the scene, whose unsaved edits it must not save, and returns whether the file changed, which
    // is what asks version control for a snapshot. It writes only for a scene file that references the file: one
    // saved while the scene had no fingerprints references none, and the scene's next save writes both.
    internal static bool Update(Uri sceneUri, Guid sceneId, IEnumerable<KeyValuePair<string, MediaFileFingerprint>> fingerprints)
    {
        if (!sceneUri.IsFile || !File.Exists(sceneUri.LocalPath)) return false;
        JsonObject scene;
        using (FileStream stream = File.OpenRead(sceneUri.LocalPath))
            scene = JsonNode.Parse(stream) as JsonObject ?? throw new JsonException("The scene file is not a JSON object.");
        // The file can hold another scene by now, for example after a checkout.
        if (!Guid.TryParse(scene[nameof(CoreObject.Id)]?.GetValue<string>(), out Guid id) || id != sceneId
            || scene[SceneProperty] is null)
            return false;

        var file = new MediaFingerprintFile();
        foreach ((string key, MediaFileFingerprint fingerprint) in fingerprints)
        {
            file.Fingerprints.Add(key, fingerprint);
        }

        return file.Save(new Uri(GetPath(sceneUri.LocalPath)));
    }

    // A hand edit or a merge can leave null where a fingerprint belongs, or a fingerprint without a hash, also among
    // a model's dependencies at any depth. Loading skips such an entry: a save or the repair tool would meet the
    // null, and an entry without a hash never matches, which is worse than none. The next scan of the media hashes
    // it again.
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

    // The format version a file states, or null when it states none.
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
