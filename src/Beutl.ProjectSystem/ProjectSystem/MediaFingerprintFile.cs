using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Logging;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

namespace Beutl.ProjectSystem;

// A scene's media fingerprints live in a JSON file of their own in the scene's folder, so the scene file stays
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
//       "footage/clip.mp4": {
//         "Length": 1048576,
//         "Sha256": "9F86D081884C7D65..."
//       }
//     }
//   }
//
// The missing-media repair tool uses them to check a candidate's content, find a renamed file and tell whether
// a relinked model is the same asset, so the file belongs to the project like the scene file: it moves and
// copies with the scene folder, and version control commits it. Keys are in ordinal order, and the file is
// written only when its content changes, so neither the order of a scan nor a media file whose timestamp alone
// changed rewrites it. A scene without fingerprints has no file. A file with a later format version is neither
// used nor rewritten nor deleted, so an older Beutl does not destroy what a newer one wrote.
//
// Development builds from #2666 until this file existed kept the fingerprints, the map without a version, inside
// the scene file under "MediaFingerprints". Loading still reads them there, and the next save of the scene
// moves them here.
internal static class MediaFingerprintFile
{
    // In the scene file's folder. Renaming the file takes only this constant.
    internal const string FileName = "media-fingerprints.json";

    // The format this version reads and writes. A change that this version could not read raises it.
    internal const int CurrentVersion = 1;

    // Where those development builds kept the fingerprints, in the scene file.
    internal const string LegacySceneProperty = nameof(Scene.MediaFingerprints);

    private const string VersionProperty = "Version";
    private const string FingerprintsProperty = "Fingerprints";

    private static readonly ILogger s_logger = Log.CreateLogger(typeof(MediaFingerprintFile));

    internal static string GetPath(string scenePath) => Path.Combine(Path.GetDirectoryName(scenePath) ?? string.Empty, FileName);

    // Keyed by absolute URI. A missing file reads as no fingerprints, and so does an unreadable one: they only
    // help the repair tool, so the scene opens without them, and the next write replaces the file. A file with a
    // later format version reads as none too, but no write replaces it.
    internal static Dictionary<string, MediaFileFingerprint> Read(Uri sceneUri)
    {
        var result = new Dictionary<string, MediaFileFingerprint>(StringComparer.Ordinal);
        string path = GetPath(sceneUri.LocalPath);
        Dictionary<string, MediaFileFingerprint?>? stored;
        try
        {
            if (!File.Exists(path)) return result;
            using JsonDocument document = Parse(path);
            JsonElement root = document.RootElement;
            long? version = GetVersion(root);
            if (version > CurrentVersion)
            {
                s_logger.LogWarning(
                    "The media fingerprints in {Path} have format version {Version}, which this version of Beutl does not read; the scene opens without them, and the file is left as it is.",
                    path,
                    version);
                return result;
            }

            if (version != CurrentVersion
                || !root.TryGetProperty(FingerprintsProperty, out JsonElement fingerprints)
                || fingerprints.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException($"The file is not in media fingerprint format {CurrentVersion}.");
            }

            stored = fingerprints.Deserialize<Dictionary<string, MediaFileFingerprint?>>(JsonHelper.SerializerOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            s_logger.LogWarning(ex, "Could not read the media fingerprints in {Path}; the scene opens without them.", path);
            return result;
        }

        if (stored is null) return result;
        var fileUri = new Uri(path);
        foreach ((string key, MediaFileFingerprint? fingerprint) in stored)
        {
            if (!IsComplete(fingerprint)) continue;
            Uri uri;
            try { uri = UriHelper.ResolvePersistedReference(key, fileUri); }
            catch (JsonException) { continue; }
            if (uri.IsFile) result[uri.AbsoluteUri] = fingerprint;
        }

        return result;
    }

    // A hand edit or a merge can leave null where a fingerprint belongs, also among a model's dependencies at any
    // depth. Loading skips such an entry like an unreadable file, so neither a save nor the repair tool meets a
    // null; the next scan of the media hashes it again.
    internal static bool IsComplete([NotNullWhen(true)] MediaFileFingerprint? fingerprint)
        => fingerprint is not null
           && (fingerprint.Dependencies is not { } dependencies || dependencies.Values.All(IsComplete));

    // A save of the scene to sceneUri, through StoreToUri, before it writes the scene file.
    internal static void Store(Uri sceneUri, IEnumerable<KeyValuePair<string, MediaFileFingerprint>> fingerprints)
    {
        string path = GetPath(sceneUri.LocalPath);
        WriteIfChanged(path, ToJson(fingerprints, new Uri(path)));
    }

    // The editor's background update after a scan. It stores the fingerprints of the saved scene graph without
    // serializing the scene, whose unsaved edits it must not save, and returns whether the file changed, which
    // is what asks version control for a snapshot.
    internal static bool Update(Uri sceneUri, Guid sceneId, IEnumerable<KeyValuePair<string, MediaFileFingerprint>> fingerprints)
    {
        if (!sceneUri.IsFile || !File.Exists(sceneUri.LocalPath)) return false;
        JsonObject scene;
        using (FileStream stream = File.OpenRead(sceneUri.LocalPath))
            scene = JsonNode.Parse(stream) as JsonObject ?? throw new JsonException("The scene file is not a JSON object.");
        // The file can hold another scene by now, for example after a checkout.
        if (!Guid.TryParse(scene[nameof(CoreObject.Id)]?.GetValue<string>(), out Guid id) || id != sceneId)
            return false;

        string path = GetPath(sceneUri.LocalPath);
        JsonObject? json = ToJson(fingerprints, new Uri(path));
        // A scene a development build saved keeps its fingerprints inside until its next save moves them, so a
        // scan that finds the same ones changes nothing. The key holds the map without the version around it.
        if (!File.Exists(path) && scene[LegacySceneProperty] is { } legacy
            && JsonNode.DeepEquals(legacy, json?[FingerprintsProperty]))
            return false;

        return WriteIfChanged(path, json);
    }

    // Keys relative to the file and in ordinal order, a model's dependencies too, so the text does not depend
    // on the order the files were scanned in. Null when there is nothing to store.
    private static JsonObject? ToJson(IEnumerable<KeyValuePair<string, MediaFileFingerprint>> fingerprints, Uri fileUri)
    {
        var entries = new SortedDictionary<string, MediaFileFingerprint>(StringComparer.Ordinal);
        foreach ((string key, MediaFileFingerprint fingerprint) in fingerprints)
        {
            entries.Add(UriHelper.ToSerializedUri(new Uri(key), fileUri).ToString(), fingerprint);
        }

        return entries.Count == 0
            ? null
            : new JsonObject { [VersionProperty] = CurrentVersion, [FingerprintsProperty] = ToJson(entries) };
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

    // Writes json to path, or removes the file when json is null, unless the file already says the same. A file
    // with a later format version is left as it is.
    private static bool WriteIfChanged(string path, JsonObject? json)
    {
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
            CoreSerializer.WriteJsonAtomically(
                path, isCompatibilityGate: false, writer => json.WriteTo(writer, JsonHelper.SerializerOptions));
        }

        return true;
    }

    // The format version a file states, or null when it states none.
    private static long? GetVersion(JsonElement root)
        => root.ValueKind == JsonValueKind.Object
           && root.TryGetProperty(VersionProperty, out JsonElement version)
           && version.ValueKind == JsonValueKind.Number
           && version.TryGetInt64(out long value)
            ? value
            : null;

    // Read through JsonDocument, which, unlike JsonNode, also takes a hand edit that repeats a key.
    private static JsonDocument Parse(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return JsonDocument.Parse(stream);
    }

    // Null when the file is not JSON, for example when a merge left conflict markers in it.
    private static JsonDocument? TryParse(string path)
    {
        try
        {
            return Parse(path);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
