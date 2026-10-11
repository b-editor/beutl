using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Media;
using Beutl.Serialization;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Beutl.ProjectSystem;

public partial class Scene
{
    // Keys use the same relative-URI rules as media properties when serialized.
    [NotAutoSerialized]
    public Dictionary<string, MediaFileFingerprint> MediaFingerprints { get; } = new(StringComparer.Ordinal);

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);

        context.SetValue("Width", FrameSize.Width);
        context.SetValue("Height", FrameSize.Height);
        context.SetValue("Groups", Groups.Select(ids => string.Join(':', ids)).ToArray());
        context.SetValue(nameof(Markers), Markers);
        Recovery.WriteMetadata(context);
        if (MediaFingerprints.Count > 0)
            context.SetValue(nameof(MediaFingerprints), MediaFingerprints.ToDictionary(
                pair => UriHelper.ToSerializedUri(new Uri(pair.Key), context.BaseUri).ToString(), pair => pair.Value));

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

            UpdateInclude();

            WriteElementPatterns(elementsNode, "Include", _includeElements);
            WriteElementPatterns(elementsNode, "Exclude", _excludeElements);

            context.SetValue("Elements", elementsNode);
        }
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);

        if (context.Contains("Width") && context.Contains("Height"))
        {
            FrameSize = new PixelSize(context.GetValue<int>("Width"), context.GetValue<int>("Height"));
        }

        Recovery.ReadMetadata(context);
        MediaFingerprints.Clear();
        if (context.GetValue<Dictionary<string, MediaFileFingerprint>>(nameof(MediaFingerprints)) is { } fingerprints)
        {
            foreach (var pair in fingerprints)
            {
                Uri uri;
                try { uri = UriHelper.ResolvePersistedReference(pair.Key, context.BaseUri); }
                catch (JsonException) { continue; }
                if (uri.IsFile)
                    MediaFingerprints[uri.AbsoluteUri] = pair.Value;
            }
        }

        Markers.Clear();
        if (context.Contains(nameof(Markers))
            && context.GetValue<SceneMarker[]>(nameof(Markers)) is { } markers)
        {
            Markers.AddRange(markers);
        }

        if (context.GetValue<JsonNode>(nameof(Elements)) is { } elementsJson)
        {
            if (elementsJson is JsonObject elementsObject)
            {
                var directoryName = Path.GetDirectoryName(Uri!.LocalPath)!;
                IEnumerable<string> includes = [];
                IEnumerable<string> excludes = [];

                // 含めるクリップ
                if (elementsObject.TryGetPropertyValue("Include", out JsonNode? includeNode))
                {
                    ReadElementPatterns(includeNode!, _includeElements);
                    includes = _includeElements;
                }

                // 除外するクリップ
                if (elementsObject.TryGetPropertyValue("Exclude", out JsonNode? excludeNode))
                {
                    ReadElementPatterns(excludeNode!, _excludeElements);
                    excludes = _excludeElements;
                }

                SyncronizeFiles(MatchElementFiles(directoryName, includes, excludes));
            }
            else
            {
                Children.Replace(context.GetValue<Elements>(nameof(Elements))!);
            }
        }
        else
        {
            Children.Clear();
        }

        if (context.Contains("Groups"))
        {
            string[]? groups = context.GetValue<string[]>("Groups");
            Groups.Clear();
            foreach (string group in groups ?? [])
            {
                var ids = group.Split(':')
                    .Select(s => Guid.TryParse(s, out Guid id) ? id : Guid.Empty)
                    .Select(id => Recovery.MapElementId(id))
                    .Where(i => i != Guid.Empty && Children.Any(e => e.Id == i))
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

        // 削除するElements
        Element[] elementsRemove = Children.ExceptBy(uriToElement, x => x.Uri).ToArray();
        // 追加するElements
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

    internal static SuppressedStorageSource? TryResumeElementPersistence(Element element)
        => SceneRecovery.TryResumeElementPersistence(element);

    private void UpdateInclude()
    {
        string dirPath = Path.GetDirectoryName(Uri!.LocalPath)!;
        var elementPaths = Children.Select(item => NormalizeElementPattern(
            Path.GetRelativePath(dirPath, item.Uri!.LocalPath))).ToHashSet(StringComparer.Ordinal);

        // Attached children must not retain exclusions left by an earlier removal.
        _excludeElements.RemoveAll(pattern => elementPaths.Contains(pattern)
            || elementPaths.Any(path => AreSameElementPaths(dirPath, pattern, path)));

        string[] files = MatchElementFiles(dirPath, _includeElements, _excludeElements);
        foreach (string rel in elementPaths)
        {
            // 含まれていない場合追加
            if (!files.Contains(rel) && !_includeElements.Contains(rel))
            {
                _includeElements.Add(rel);
            }
        }
    }

    private static string[] MatchElementFiles(
        string directoryName, IEnumerable<string> includes, IEnumerable<string> excludes)
    {
        var matcher = new Matcher();
        matcher.AddIncludePatterns(includes);
        var literalExclusions = new List<string>();
        foreach (string pattern in excludes)
        {
            if (pattern.Contains('*') || pattern.EndsWith('/')
                || Directory.Exists(Path.Combine(directoryName, pattern)))
            {
                matcher.AddExclude(pattern);
            }
            else
            {
                literalExclusions.Add(pattern);
            }
        }

        // The glob matcher ignores case. Literal files instead follow their actual storage identity.
        var directory = new DirectoryInfoWrapper(new DirectoryInfo(directoryName));
        return matcher.Execute(directory).Files.Select(file => file.Path)
            .Where(path => !literalExclusions.Any(pattern => AreSameElementPaths(directoryName, pattern, path)))
            .ToArray();
    }

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
