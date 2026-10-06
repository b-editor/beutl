using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
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

    // Reads the frame rate from the given session's live, Project-attached scene; CreateSceneSnapshot
    // returns a clone detached from its Project, on which the project frame-rate lookup would always
    // miss. Takes the same session the snapshot was read from so the two cannot straddle a swap.
    private static int ReadSessionFrameRate(IEditingSession session)
    {
        return session.ReadOnSession(() =>
            session.Root is Scene liveScene ? GetSceneFrameRate(liveScene) : DefaultSceneFrameRate);
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
            if (scene.Uri is { } sceneUri)
            {
                snapshot["Uri"] = sceneUri.ToString();
            }

            var clone = (Scene)CoreSerializer.DeserializeFromJsonObject(
                snapshot,
                typeof(Scene),
                new CoreSerializerOptions
                {
                    BaseUri = scene.Uri,
                    Mode = CoreSerializationMode.Read | CoreSerializationMode.EmbedReferencedObjects
                });
            clone.Uri ??= scene.Uri;
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

    // Expression evaluation resolves through the owner's hierarchical root and falls back to the
    // LIVE BeutlApplication.Current when the owner is detached, so a rootless snapshot would read
    // live objects mid-render — the exact concurrent-mutation hazard the snapshot exists to
    // prevent. Root the snapshot like FileEditingSession roots a headless session, with the
    // referenced-scene clones alongside so Id lookups resolve snapshot-locally.
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
        if (source.Uri is { } sourceUri)
        {
            // Scene.Children_CollectionChanged dereferences the scene's own Uri while elements
            // deserialize, so the clone must carry it from the start, not get it assigned after.
            json["Uri"] = sourceUri.ToString();
        }

        var clone = (CoreObject)CoreSerializer.DeserializeFromJsonObject(json, source.GetType(), new CoreSerializerOptions
        {
            BaseUri = source.Uri,
            Mode = CoreSerializationMode.Read | CoreSerializationMode.EmbedReferencedObjects,
        });
        clone.Uri ??= source.Uri;
        return clone;
    }
}
