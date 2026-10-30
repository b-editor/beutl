using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Collections;
using Beutl.JsonConverters;
using Beutl.Media;
using Beutl.Serialization;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Beutl.ProjectSystem;

// Development builds since #2666 kept the media fingerprints in the scene file under MediaFingerprints. They are
// in a file of their own now (MediaFingerprintFile), and a scene drops that key rather than keeping it as an
// unknown member.
[DroppedMembers("MediaFingerprints")]
public partial class Scene
{
    // The file that stores the scene's media fingerprints, or null while the scene has none: its scene file then
    // references no file, and a save removes the one in its folder. A file that a later version of Beutl wrote
    // stays here without fingerprints, so a save keeps the reference to it and leaves the file alone.
    internal MediaFingerprintFile? MediaFingerprintFile { get; private set; }

    // Keyed by absolute URI.
    [NotAutoSerialized]
    public IReadOnlyDictionary<string, MediaFileFingerprint> MediaFingerprints
        => (IReadOnlyDictionary<string, MediaFileFingerprint>?)MediaFingerprintFile?.Fingerprints
           ?? ReadOnlyDictionary<string, MediaFileFingerprint>.Empty;

    // Replaces the fingerprints: the first one creates MediaFingerprintFile, and without any it is null again. One
    // read from a later version's file takes none.
    internal void SetMediaFingerprints(IEnumerable<KeyValuePair<string, MediaFileFingerprint>> fingerprints)
    {
        if (MediaFingerprintFile is { IsFromLaterVersion: true }) return;
        var file = new MediaFingerprintFile(fingerprints);
        MediaFingerprintFile = file.Fingerprints.Count == 0 ? null : file;
    }

    // Drops one fingerprint, and MediaFingerprintFile with the last.
    internal void RemoveMediaFingerprint(string uri)
    {
        if (MediaFingerprints.ContainsKey(uri))
            SetMediaFingerprints(MediaFingerprints.Where(pair => pair.Key != uri));
    }

    // Writes MediaFingerprintFile into the scene's folder, or removes the file there while the scene has no
    // fingerprints. A save of the scene with SaveReferencedObjects does this itself.
    internal void SaveMediaFingerprintFile()
    {
        if (GetMediaFingerprintFileUri() is { } fileUri)
            (MediaFingerprintFile ?? new MediaFingerprintFile()).Save(fileUri);
    }

    // Follows the scene's Uri, so a scene that moves, for example into the copy a package export saves, takes its
    // fingerprints along instead of writing them back to the folder it came from.
    private Uri? GetMediaFingerprintFileUri()
        => Uri is { IsFile: true } uri ? UriHelper.CreateFromPath(MediaFingerprintFile.GetPath(uri.LocalPath)) : null;

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);

        context.SetValue(nameof(Duration), MediaTimeJsonConverter.LengthToWrite(Start.Value, Duration.Value));
        context.SetValue("Width", FrameSize.Value.Width);
        context.SetValue("Height", FrameSize.Value.Height);
        context.SetValue("Groups", Groups.Select(ids => string.Join(':', ids)).ToArray());
        context.SetValue(nameof(Markers), Markers);
        Recovery.WriteMetadata(context);

        if (context.Mode.HasFlag(CoreSerializationMode.SaveReferencedObjects))
        {
            string sidecarRoot = Path.GetDirectoryName(Uri!.LocalPath)
                                 ?? throw new JsonException("Scene has no sidecar directory.");
            foreach (Element item in Children)
            {
                CoreSerializer.StoreToUri(item, item.Uri!, sidecarRoot);
            }
        }

        if (context.Mode.HasFlag(CoreSerializationMode.EmbedReferencedObjects))
        {
            context.SetValue("Elements", Children);
        }
        else
        {
            var elementsNode = new JsonObject();

            UpdateInclude((context as JsonSerializationContext)?.PendingFileChanges);

            WriteElementPatterns(elementsNode, "Include", _includeElements);
            WriteElementPatterns(elementsNode, "Exclude", _excludeElements);

            context.SetValue("Elements", elementsNode);
        }

        if (context.Mode.HasFlag(CoreSerializationMode.SaveReferencedObjects))
        {
            // Saved, or removed, here rather than by the serializer, which would rewrite the file on every save
            // and replace one a later version of Beutl wrote.
            SaveMediaFingerprintFile();
        }

        // Only a scene with fingerprints references the file.
        if (MediaFingerprintFile is { } fingerprints && GetMediaFingerprintFileUri() is { } fingerprintsUri)
        {
            if (context.Mode.HasFlag(CoreSerializationMode.SaveReferencedObjects))
            {
                Uri serializedUri = UriHelper.ToSerializedUri(fingerprintsUri, context.BaseUri);
                context.SetValue(MediaFingerprintFile.SceneProperty,
                    UriHelper.ToPersistedReference(serializedUri));
            }
            else
            {
                // The serializer writes the reference, or the fingerprints themselves when it embeds referenced
                // objects.
                fingerprints.Uri = fingerprintsUri;
                context.SetValue(MediaFingerprintFile.SceneProperty, fingerprints);
            }
        }
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        Duration.Value = MediaTimeJsonConverter.LengthRead(context, nameof(Start), nameof(Duration), Start.Value, Duration.Value);
        // Layers keeps its list, as its CoreProperty accessor did.
        KeptListReader.ReadInto<CoreList<TimelineLayer>, TimelineLayer>(context, nameof(Layers), Layers);

        if (context.Contains("Width") && context.Contains("Height"))
        {
            FrameSize.Value = new PixelSize(context.GetValue<int>("Width"), context.GetValue<int>("Height"));
        }

        Recovery.ReadMetadata(context);
        // Read here rather than by the serializer, which would fail the whole scene on a missing or unreadable file.
        MediaFingerprintFile = MediaFingerprintFile.Load(
            context.GetValue<JsonNode>(MediaFingerprintFile.SceneProperty), context.BaseUri);

        Markers.Clear();
        if (context.Contains(nameof(Markers))
            && context.GetValue<SceneMarker[]>(nameof(Markers)) is { } markers)
        {
            Markers.AddRange(markers);
        }

        if (context.GetValue<JsonNode>("Elements") is { } elementsJson)
        {
            if (elementsJson is JsonObject elementsObject)
            {
                var directoryName = Path.GetDirectoryName(Uri!.LocalPath)!;
                IEnumerable<string> includes = [];
                IEnumerable<string> excludes = [];

                if (elementsObject.TryGetPropertyValue("Include", out JsonNode? includeNode))
                {
                    ReadElementPatterns(includeNode!, _includeElements);
                    includes = _includeElements;
                }

                if (elementsObject.TryGetPropertyValue("Exclude", out JsonNode? excludeNode))
                {
                    ReadElementPatterns(excludeNode!, _excludeElements);
                    excludes = _excludeElements;
                }

                SyncronizeFiles(MatchElementFiles(directoryName, includes, excludes));
            }
            else
            {
                Children.Replace(context.GetValue<Element[]>("Elements")!);
            }
        }
        else
        {
            Children.Clear();
        }

        if (context.Contains("Groups"))
        {
            string[]? groups = context.GetValue<string[]>("Groups");
            var elementIds = Children.Select(element => element.Id.Value).ToHashSet();
            Groups.Clear();
            foreach (string group in groups ?? [])
            {
                var ids = group.Split(':')
                    .Select(s => Guid.TryParse(s, out Guid id) ? id : Guid.Empty)
                    .Select(id => Recovery.MapElementId(id))
                    .Where(i => i != Guid.Empty && elementIds.Contains(i))
                    .ToImmutableHashSet();
                if (ids.Count >= 2)
                {
                    Groups.Add(ids);
                }
            }
        }

    }

    private static void WriteElementPatterns(JsonObject jobject, string jsonName, List<string> list)
    {
        if (list.Count == 1)
        {
            jobject[jsonName] = JsonValue.Create(NormalizeElementPattern(list[0]));
        }
        else if (list.Count >= 2)
        {
            var jarray = new JsonArray();
            foreach (string item in list)
            {
                jarray.Add(JsonValue.Create(NormalizeElementPattern(item)));
            }

            jobject[jsonName] = jarray;
        }
        else
        {
            jobject.Remove(jsonName);
        }
    }

    private static void ReadElementPatterns(JsonNode node, List<string> list)
    {
        list.Clear();
        if (node is JsonValue jvalue &&
            jvalue.TryGetValue(out string? pattern))
        {
            pattern = NormalizeElementPattern(pattern);
            list.Add(pattern);
        }
        else if (node is JsonArray array)
        {
            foreach (JsonValue item in array.OfType<JsonValue>())
            {
                if (item.TryGetValue(out pattern))
                {
                    pattern = NormalizeElementPattern(pattern);
                    list.Add(pattern);
                }
            }
        }
    }

    private void SyncronizeFiles(IEnumerable<string> pathToElement)
    {
        using Activity? activity = BeutlApplication.ActivitySource.StartActivity("Scene.SyncronizeFiles");

        string sceneDirectory = Path.GetDirectoryName(Uri!.LocalPath)!;
        var uriToElement = pathToElement.Select(path =>
        {
            string fullPath = Path.GetFullPath(Path.Combine(sceneDirectory, path));
            if (!FilePathComparison.IsSameOrDescendant(sceneDirectory, fullPath))
                throw new JsonException($"Element path escapes the scene directory: {path}");
            return UriHelper.CreateFromPath(fullPath);
        }).ToArray();

        Element[] elementsRemove = Children.ExceptBy(uriToElement, x => x.Uri).ToArray();
        Uri[] urisAdd = uriToElement.Except(Children.Select(x => x.Uri).Where(u => u != null)).ToArray()!;

        foreach (Element item in elementsRemove)
        {
            Children.Remove(item);
        }

        Children.AddRange(urisAdd.AsParallel().Select(Recovery.RestoreElement));
        Recovery.Reconcile();

        activity?.SetTag("addCount", urisAdd.Length);
        activity?.SetTag("removeCount", elementsRemove.Length);
        activity?.SetTag("childrenCount", Children.Count);
    }

    private void UpdateInclude(IReadOnlyDictionary<string, bool>? pendingFiles = null)
    {
        string dirPath = Path.GetDirectoryName(Uri!.LocalPath)!;
        var elementPaths = Children.Select(item => NormalizeElementPattern(
            Path.GetRelativePath(dirPath, item.Uri!.LocalPath))).ToHashSet(StringComparer.Ordinal);
        var pathsByName = elementPaths.ToLookup(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);

        // Attached children must not retain exclusions left by an earlier removal.
        _excludeElements.RemoveAll(pattern => elementPaths.Contains(pattern)
            || pathsByName[Path.GetFileName(pattern)].Any(path => AreSameElementPaths(dirPath, pattern, path)));
        // A pending deletion can fail. Keep its exclusion in the live model and snapshot until
        // the writer confirms success; a later save prunes the live entry once the file is gone.
        _excludeElements.RemoveAll(pattern => GetLiteralElementPath(dirPath, pattern) is { } path
            && pendingFiles?.ContainsKey(path) != true && !File.Exists(path));

        var files = MatchElementFiles(dirPath, _includeElements, _excludeElements, pendingFiles).ToHashSet(StringComparer.Ordinal);
        var includes = _includeElements.ToHashSet(StringComparer.Ordinal);
        foreach (string rel in elementPaths)
        {
            if (!files.Contains(rel) && includes.Add(rel))
            {
                _includeElements.Add(rel);
            }
        }
    }

    private static string[] MatchElementFiles(
        string directoryName, IEnumerable<string> includes, IEnumerable<string> excludes,
        IReadOnlyDictionary<string, bool>? pendingFiles = null)
    {
        var matcher = new Matcher();
        matcher.AddIncludePatterns(includes);
        var literalExclusions = new List<string>();
        foreach (string pattern in excludes)
        {
            if (!IsLiteralPattern(pattern))
            {
                matcher.AddExclude(pattern);
            }
            else
            {
                // A literal can also name a directory, using the matcher's case and path rules.
                matcher.AddExclude(pattern + "/");
                literalExclusions.Add(pattern);
            }
        }

        // The glob matcher ignores case. Literal files instead follow their actual storage identity.
        var directory = new DirectoryInfoWrapper(new DirectoryInfo(directoryName));
        IEnumerable<string> files = matcher.Execute(directory).Files.Select(file => file.Path);
        if (pendingFiles is not null)
        {
            files = files.Where(path => !pendingFiles.TryGetValue(Path.GetFullPath(path, directoryName), out bool exists) || exists)
                .Concat(matcher.Match(directoryName, pendingFiles.Where(pair => pair.Value).Select(pair => pair.Key))
                    .Files.Select(file => file.Path));
        }
        var exclusionsByName = literalExclusions.ToLookup(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);
        return files.Where(path => !exclusionsByName[Path.GetFileName(path)]
                .Any(pattern => AreSameElementPaths(directoryName, pattern, path)))
            .ToArray();
    }

    // Runs on the writer's captured JSON, after element deletions. Never mutate the live exclusions:
    // an edit or Undo may have changed them while this batch was being written.
    internal static void RemoveDeletedElementExclusions(JsonObject snapshot, Uri sceneUri, IReadOnlySet<string> deletedFiles)
    {
        if (deletedFiles.Count == 0 || snapshot["Elements"] is not JsonObject elements
            || elements["Exclude"] is not { } excluded) return;

        List<string> patterns = [];
        ReadElementPatterns(excluded, patterns);
        string directory = Path.GetDirectoryName(sceneUri.LocalPath)!;
        patterns.RemoveAll(pattern => GetLiteralElementPath(directory, pattern) is { } path && deletedFiles.Contains(path));
        WriteElementPatterns(elements, "Exclude", patterns);
    }

    private static string? GetLiteralElementPath(string directory, string pattern)
        => IsLiteralPattern(pattern) && pattern.EndsWith(".belm", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFullPath(pattern.TrimStart('/'), directory) : null;

    private static bool IsLiteralPattern(string pattern)
        => !pattern.Contains('*') && !pattern.EndsWith('/') && Path.GetFileName(pattern) is not ("" or "." or "..");

    private static bool AreSameElementPaths(string directoryName, string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal)) return true;
        if (!Path.GetFileName(left.AsSpan()).Equals(Path.GetFileName(right.AsSpan()), StringComparison.OrdinalIgnoreCase))
            return false;

        // Matcher's leading slash is relative to the scene, and dot segments do not change a literal path.
        string leftPath = Path.GetFullPath(left.TrimStart('/'), directoryName);
        string rightPath = Path.GetFullPath(right.TrimStart('/'), directoryName);
        return string.Equals(leftPath, rightPath, StringComparison.Ordinal)
            || string.Equals(leftPath, rightPath, StringComparison.OrdinalIgnoreCase)
            && FilePathComparison.AreSameCanonicalPath(
                leftPath, rightPath);
    }

    private static string NormalizeElementPattern(string pattern)
    {
        return pattern.Replace('\\', '/');
    }
}
