using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Beutl.Engine;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.Editor;

// A detached copy of a scene for work off the UI thread, such as export, which later edits must not reach (#2873).
internal static partial class SceneSnapshot
{
    // Call on the thread that owns the scene. The copy keeps the Ids of the scene and of the scenes it references.
    internal static Scene Create(Scene scene)
    {
        JsonObject json = CoreSerializer.SerializeToJsonObject(scene, new CoreSerializerOptions
        {
            BaseUri = scene.Uri,
            Mode = CoreSerializationMode.EmbedReferencedObjects,
            KeepUnknownMembers = false
        });

        var clone = (Scene)DeserializeDetached(json, typeof(Scene), scene.Uri);
        AttachRoot(scene, clone, CloneReferencedObjects(scene));
        return clone;
    }

    // The clone gets its source's Uri first: Scene.Children_CollectionChanged reads it while elements deserialize.
    private static CoreObject DeserializeDetached(JsonObject json, Type type, Uri? uri)
    {
        if (uri is not null)
        {
            json["Uri"] = uri.ToString();
        }

        var clone = (CoreObject)CoreSerializer.DeserializeFromJsonObject(json, type, new CoreSerializerOptions
        {
            BaseUri = uri,
            Mode = CoreSerializationMode.Read | CoreSerializationMode.EmbedReferencedObjects,
            PreserveUnchangedValues = true,
            KeepUnknownMembers = false
        });
        clone.Uri ??= uri;
        return clone;
    }

    // A reference expression finds its target by Id in the clone's own root, so the targets are cloned with it.
    private static List<CoreObject> CloneReferencedObjects(Scene scene)
    {
        var clones = new List<CoreObject>();
        var scanned = new HashSet<Guid> { scene.Id.Value };
        var queue = new Queue<IHierarchical>();
        queue.Enqueue(scene);
        while (queue.TryDequeue(out IHierarchical? root))
        {
            foreach (CoreObject target in EnumerateReferenceTargets(root))
            {
                if (!scanned.Add(target.Id.Value))
                    continue;

                clones.Add(CloneDetached(target));
                if (target is IHierarchical hierarchical)
                    queue.Enqueue(hierarchical);
            }
        }

        return clones;
    }

    // Expressions find other objects only by Id: reference expressions and GetProperty("{id}.Name") hold it as text.
    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();

    // Yields the project item, such as a scene, that holds each object an expression reads.
    internal static IEnumerable<CoreObject> EnumerateReferenceTargets(IHierarchical root)
    {
        var lookupRoot = root.FindHierarchicalRoot() as ICoreObject ?? root as ICoreObject;
        var visited = new HashSet<IHierarchical>(ReferenceEqualityComparer.Instance) { root };
        var stack = new Stack<IHierarchical>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            IHierarchical current = stack.Pop();
            if (current is EngineObject engineObject)
            {
                foreach (IProperty property in engineObject.Properties)
                {
                    if (property is not IExpressionProperty { Expression: { } expression })
                        continue;

                    foreach (Match match in GuidPattern().Matches(expression.ExpressionString))
                    {
                        if (Guid.TryParse(match.ValueSpan, out Guid id)
                            && ReferenceScope.Find(engineObject, id, lookupRoot) is IHierarchical target
                            && target.FindHierarchicalParent<ProjectItem>(includeSelf: true) is { } item)
                        {
                            yield return item;
                        }
                    }
                }
            }

            foreach (IHierarchical child in current.HierarchicalChildren)
            {
                if (visited.Add(child))
                    stack.Push(child);
            }
        }
    }

    private static CoreObject CloneDetached(CoreObject source)
    {
        JsonObject json = CoreSerializer.SerializeToJsonObject(source, new CoreSerializerOptions
        {
            BaseUri = source.Uri,
            Mode = CoreSerializationMode.Write | CoreSerializationMode.EmbedReferencedObjects,
        });

        return DeserializeDetached(json, source.GetType(), source.Uri);
    }

    // A Project and BeutlApplication of its own keep the clone's expressions from reaching BeutlApplication.Current.
    private static void AttachRoot(Scene source, Scene clone, List<CoreObject> references)
    {
        var project = new Project();
        if (source.FindHierarchicalParent<Project>() is { } sourceProject)
        {
            project.Id.Value = sourceProject.Id.Value;
            project.Name.Value = sourceProject.Name.Value;
            project.FrameRate.Value = sourceProject.FrameRate.Value;
            project.SampleRate.Value = sourceProject.SampleRate.Value;
            project.ChannelLayout.Value = sourceProject.ChannelLayout.Value;
            foreach ((string key, string value) in sourceProject.Variables.Value)
            {
                project.Variables.Value[key] = value;
            }
        }

        foreach (CoreObject reference in references)
        {
            if (reference is ProjectItem item)
                project.Items.Add(item);
        }

        project.Items.Add(clone);
        _ = new BeutlApplication { Project = { Value = project } };
    }
}
