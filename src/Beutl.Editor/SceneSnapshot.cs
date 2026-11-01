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
    internal static Scene Create(Scene scene) => Materialize(Capture(scene));

    // Reads what the copy is made from, on the thread that owns the scene.
    internal static SceneCapture Capture(Scene scene)
    {
        JsonObject json = CoreSerializer.SerializeToJsonObject(scene, new CoreSerializerOptions
        {
            BaseUri = scene.Uri,
            Mode = CoreSerializationMode.EmbedReferencedObjects,
            KeepUnknownMembers = false
        });

        return new SceneCapture(json, scene.Uri, CaptureReferencedObjects(scene), CreateProject(scene));
    }

    // Builds the copy on any thread. It takes over the capture, so a capture is materialized once.
    internal static Scene Materialize(SceneCapture capture)
    {
        var clone = (Scene)DeserializeDetached(capture.Scene, typeof(Scene), capture.Uri);
        foreach (CapturedObject reference in capture.References)
        {
            if (DeserializeDetached(reference.Json, reference.Type, reference.Uri) is ProjectItem item)
                capture.Project.Items.Add(item);
        }

        capture.Project.Items.Add(clone);
        // A BeutlApplication of its own keeps the clone's expressions from reaching BeutlApplication.Current.
        _ = new BeutlApplication { Project = { Value = capture.Project } };
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
    private static List<CapturedObject> CaptureReferencedObjects(Scene scene)
    {
        var captured = new List<CapturedObject>();
        var scanned = new HashSet<Guid> { scene.Id.Value };
        var queue = new Queue<IHierarchical>();
        queue.Enqueue(scene);
        while (queue.TryDequeue(out IHierarchical? root))
        {
            foreach (CoreObject target in EnumerateReferenceTargets(root))
            {
                if (!scanned.Add(target.Id.Value))
                    continue;

                JsonObject json = CoreSerializer.SerializeToJsonObject(target, new CoreSerializerOptions
                {
                    BaseUri = target.Uri,
                    Mode = CoreSerializationMode.Write | CoreSerializationMode.EmbedReferencedObjects,
                });
                captured.Add(new CapturedObject(json, target.GetType(), target.Uri));
                if (target is IHierarchical hierarchical)
                    queue.Enqueue(hierarchical);
            }
        }

        return captured;
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

    // A Project of its own, with the source project's settings, gives the clone a root apart from the open project.
    private static Project CreateProject(Scene source)
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

        return project;
    }

    // What Materialize builds a copy from: the scene, the objects its expressions reach, and a project to hold them.
    internal sealed record SceneCapture(
        JsonObject Scene,
        Uri? Uri,
        IReadOnlyList<CapturedObject> References,
        Project Project);

    internal sealed record CapturedObject(JsonObject Json, Type Type, Uri? Uri);
}
