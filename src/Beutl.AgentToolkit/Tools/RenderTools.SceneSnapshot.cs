using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Documents;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Tools;

public sealed partial class RenderTools
{
    internal Scene RequireSceneSnapshot()
    {
        IEditingSession session = sessions.RequireSession();
        return CreateSceneSnapshot(session);
    }

    internal static Scene CreateSceneSnapshot(IEditingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return session.ReadOnSession(() =>
        {
            if (session.Root is not Scene scene)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    "The current editing session is not attached to a scene."));
            }

            // Renders and analyzers run after ReadOnSession releases the dispatch lock, so a
            // concurrent apply_edit can mutate the live scene mid-render for ANY session source;
            // every snapshot must be an isolated clone.
            JsonObject snapshot = session.Documents.Read(scene);
            snapshot.Remove(SchemaVersion.PropertyName);
            var clone = (Scene)DocumentAdapter.DeserializeDetached(snapshot, typeof(Scene), scene.Uri);
            IReadOnlyList<CoreObject> referenceClones = CloneReferencedObjectsInto(scene, clone);
            AttachSnapshotRoot(scene, clone, referenceClones);
            return clone;
        });
    }

    // The snapshot is a Project-detached clone, so its ReferenceExpression targets (referenced by
    // ObjectId) cannot resolve against the live project; the referenced scenes are cloned into the
    // snapshot with their original Ids and attached to the snapshot root (AttachSnapshotRoot) so the
    // expression's FindById(ObjectId) resolves snapshot-locally instead of every SceneDrawable/
    // SceneSound rendering empty.
    private static IReadOnlyList<CoreObject> CloneReferencedObjectsInto(Scene liveRoot, Scene clone)
    {
        var referenceClones = new List<CoreObject>();
        var scanned = new HashSet<Guid> { liveRoot.Id };
        var liveScanQueue = new Queue<IHierarchical>();
        liveScanQueue.Enqueue(liveRoot);

        while (liveScanQueue.TryDequeue(out IHierarchical? scanRoot))
        {
            foreach (CoreObject target in EnumerateLiveReferenceTargets(scanRoot))
            {
                if (!scanned.Add(target.Id))
                {
                    continue;
                }

                referenceClones.Add(CloneDetached(target));
                if (target is IHierarchical hierarchicalTarget)
                {
                    liveScanQueue.Enqueue(hierarchicalTarget);
                }
            }
        }

        return referenceClones;
    }

    // Root the snapshot in its own Project and BeutlApplication: expressions of a detached owner
    // fall back to the live BeutlApplication.Current.
    private static void AttachSnapshotRoot(Scene liveScene, Scene clone, IReadOnlyList<CoreObject> referenceClones)
    {
        var project = new Project();
        if (liveScene.FindHierarchicalParent<Project>() is { } liveProject)
        {
            foreach ((string key, string value) in liveProject.Variables)
            {
                project.Variables[key] = value;
            }
        }

        foreach (CoreObject referenceClone in referenceClones)
        {
            if (referenceClone is ProjectItem item)
            {
                project.Items.Add(item);
            }
        }

        project.Items.Add(clone);
        _ = new BeutlApplication { Project = project };
    }

    private static IEnumerable<CoreObject> EnumerateLiveReferenceTargets(IHierarchical root)
    {
        var lookupRoot = root.FindHierarchicalRoot() as ICoreObject ?? root as ICoreObject;
        var visited = new HashSet<IHierarchical>(ReferenceEqualityComparer.Instance) { root };
        var stack = new Stack<IHierarchical>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            IHierarchical current = stack.Pop();
            if (current is Engine.EngineObject engineObject)
            {
                foreach (Engine.IProperty property in engineObject.Properties)
                {
                    // Only the known scene-reference properties are followed: ReferenceExpression is a
                    // general binding form, so an arbitrary data-binding on another property must not
                    // clone unrelated objects, and the PropertyPath form (rejected at apply time) leaves
                    // only a direct ObjectId to resolve.
                    if (Common.ReferenceProperties.Describe(property) is { } descriptor
                        && property.Expression is Engine.Expressions.IReferenceExpression { HasPropertyPath: false } referenceExpression
                        && referenceExpression.ObjectId != Guid.Empty
                        && lookupRoot?.FindById(referenceExpression.ObjectId) is CoreObject expressionTarget
                        && descriptor.ReferencedType.IsInstanceOfType(expressionTarget))
                    {
                        yield return expressionTarget;
                    }
                }
            }

            foreach (IHierarchical child in current.HierarchicalChildren)
            {
                if (visited.Add(child))
                {
                    stack.Push(child);
                }
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
        return DocumentAdapter.DeserializeDetached(json, source.GetType(), source.Uri);
    }
}
