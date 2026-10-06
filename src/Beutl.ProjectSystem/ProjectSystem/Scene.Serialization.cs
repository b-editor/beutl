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
    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);

        static void Process(JsonObject jobject, string jsonName, List<string> list)
        {
            if (list.Count == 1)
            {
                jobject[jsonName] = JsonValue.Create(NormalizeElementPatternForStorage(list[0]));
            }
            else if (list.Count >= 2)
            {
                var jarray = new JsonArray();
                foreach (string item in list)
                {
                    jarray.Add(JsonValue.Create(NormalizeElementPatternForStorage(item)));
                }

                jobject[jsonName] = jarray;
            }
            else
            {
                jobject.Remove(jsonName);
            }
        }

        context.SetValue("Width", FrameSize.Width);
        context.SetValue("Height", FrameSize.Height);
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

            UpdateInclude();

            Process(elementsNode, "Include", _includeElements);
            Process(elementsNode, "Exclude", _excludeElements);

            context.SetValue("Elements", elementsNode);
        }
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);

        static void Process(Func<string, Matcher> add, JsonNode node, List<string> list)
        {
            list.Clear();
            if (node is JsonValue jvalue &&
                jvalue.TryGetValue(out string? pattern))
            {
                pattern = NormalizeElementPatternForRead(pattern);
                list.Add(pattern);
                add(pattern);
            }
            else if (node is JsonArray array)
            {
                foreach (JsonValue item in array.OfType<JsonValue>())
                {
                    if (item.TryGetValue(out pattern))
                    {
                        pattern = NormalizeElementPatternForRead(pattern);
                        list.Add(pattern);
                        add(pattern);
                    }
                }
            }
        }

        if (context.Contains("Width") && context.Contains("Height"))
        {
            FrameSize = new PixelSize(context.GetValue<int>("Width"), context.GetValue<int>("Height"));
        }

        Recovery.ReadMetadata(context);

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
                var matcher = new Matcher();
                var directoryName = Path.GetDirectoryName(Uri!.LocalPath)!;
                var directory = new DirectoryInfoWrapper(new DirectoryInfo(directoryName));

                // 含めるクリップ
                if (elementsObject.TryGetPropertyValue("Include", out JsonNode? includeNode))
                {
                    Process(matcher.AddInclude, includeNode!, _includeElements);
                }

                // 除外するクリップ
                if (elementsObject.TryGetPropertyValue("Exclude", out JsonNode? excludeNode))
                {
                    Process(matcher.AddExclude, excludeNode!, _excludeElements);
                }

                PatternMatchingResult result = matcher.Execute(directory);
                SyncronizeFiles(result.Files.Select(x => x.Path));
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
        var directory = new DirectoryInfoWrapper(new DirectoryInfo(dirPath));

        var matcher = new Matcher();
        matcher.AddIncludePatterns(_includeElements);
        matcher.AddExcludePatterns(_excludeElements);

        string[] files = matcher.Execute(directory).Files.Select(x => x.Path).ToArray();
        foreach (Element item in Children)
        {
            string rel = NormalizeElementPatternForStorage(
                Path.GetRelativePath(dirPath, item.Uri!.LocalPath));

            // 含まれていない場合追加
            if (!files.Contains(rel))
            {
                _includeElements.Add(rel);
            }
        }
    }

    private static string NormalizeElementPatternForRead(string pattern)
    {
        return pattern.Replace('\\', '/');
    }

    private static string NormalizeElementPatternForStorage(string pattern)
    {
        return pattern.Replace('\\', '/');
    }
}
