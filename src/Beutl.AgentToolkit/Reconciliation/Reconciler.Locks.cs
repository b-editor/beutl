using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
using Beutl.Engine;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Reconciliation;

public sealed partial class Reconciler
{
    private const string LockHint = "Agents can lock elements and layers but cannot unlock them.";

    // Agent edits follow the editor's locks (Scene.IsElementLockedByUser): a locked element, or one on a
    // locked layer, cannot change, and nothing may be placed on a locked layer; locking is allowed, unlocking
    // is not. An element that could not be fully loaded may only be deleted or repaired, as in the editor.
    // Elements and layers are matched by Id, not by change path, because the diff keeps the caller's key spelling.
    private static void ThrowIfLockedChanges(IEditingSession session, CoreObject sandboxRoot, JsonObject currentDocument)
    {
        if (session.Root is not Scene scene || sandboxRoot is not Scene sandbox)
        {
            return;
        }

        Element[] lockedElements = scene.Children.Where(scene.IsElementLockedByUser).ToArray();
        TimelineLayer[] lockedLayers = scene.Layers.Where(layer => layer.IsLocked).ToArray();
        Element[] protectedElements = scene.Children
            .Where(element => element.IsRecoveryProtected && !scene.IsElementLockedByUser(element))
            .ToArray();
        if (lockedElements.Length == 0 && lockedLayers.Length == 0 && protectedElements.Length == 0)
        {
            return;
        }

        ToolError? error = FindDuplicatedLockedElement(scene, sandbox, lockedElements)
                           ?? FindChangedLockedElement(session, scene, sandbox, lockedElements, currentDocument)
                           ?? FindUnrepairedProtectedElement(session, scene, sandbox, protectedElements, currentDocument)
                           ?? FindRegroupedLockedElement(scene, sandbox, lockedElements)
                           ?? FindUnlockedLayer(scene, sandbox, lockedLayers)
                           ?? FindElementPlacedOnLockedLayer(scene, sandbox);
        if (error is not null)
        {
            throw new ReconcileException(error);
        }
    }

    // A corrupted project can repeat an element Id, and the applier then edits whichever copy comes
    // first, so a locked copy cannot be told apart from the others. Refuse until the project is repaired.
    private static ToolError? FindDuplicatedLockedElement(Scene scene, Scene sandbox, Element[] lockedElements)
    {
        if (lockedElements.Length == 0)
        {
            return null;
        }

        Dictionary<Guid, int> currentCounts = CountIds(scene.Children);
        Dictionary<Guid, int> appliedCounts = CountIds(sandbox.Children);
        foreach (Element locked in lockedElements)
        {
            if (currentCounts.GetValueOrDefault(locked.Id) > 1 || appliedCounts.GetValueOrDefault(locked.Id) > 1)
            {
                return new ToolError(
                    ErrorCode.ValidationRejected,
                    $"Element {DescribeElement(locked)} is locked and shares its Id with another element, so its lock cannot be checked.",
                    ElementTarget(locked),
                    "The project has duplicate element Ids that must be repaired before an agent can edit this scene, for example by removing one copy in the timeline.");
            }
        }

        return null;
    }

    // As in the editor (ElementStructureService.Group and Ungroup), a locked element is never added to a
    // group, taken out of one or moved to another. Its group may only lose other members, and it may be
    // left alone only when the others were deleted, since deleting an element prunes its group.
    private static ToolError? FindRegroupedLockedElement(Scene scene, Scene sandbox, Element[] lockedElements)
    {
        if (lockedElements.Length == 0 || (scene.Groups.Count == 0 && sandbox.Groups.Count == 0))
        {
            return null;
        }

        HashSet<Guid> remaining = sandbox.Children.Select(element => element.Id).ToHashSet();
        foreach (Element locked in lockedElements)
        {
            ImmutableHashSet<Guid>[] before = scene.Groups.Where(group => group.Contains(locked.Id)).ToArray();
            ImmutableHashSet<Guid>[] after = sandbox.Groups.Where(group => group.Contains(locked.Id)).ToArray();
            // The editor never puts an element in two groups, so that comes from a corrupted project, whose
            // groups cannot be matched before and after the edit. Refuse until the project is repaired.
            if (before.Length > 1)
            {
                return new ToolError(
                    ErrorCode.ValidationRejected,
                    $"Element {DescribeElement(locked)} is locked and is in more than one group, so its grouping cannot be checked.",
                    ElementTarget(locked),
                    "The project has overlapping groups containing a locked element, which must be repaired before an agent can edit this scene.");
            }

            ImmutableHashSet<Guid>? original = before.FirstOrDefault();
            bool kept = after.Length switch
            {
                0 => original is null || !original.Any(id => id != locked.Id && remaining.Contains(id)),
                1 => original is not null && after[0].IsSubsetOf(original),
                _ => false,
            };
            if (!kept)
            {
                return new ToolError(
                    ErrorCode.ValidationRejected,
                    $"Element {DescribeElement(locked)} is locked, so an agent cannot add it to a group, take it out of its group or move it to another.",
                    ElementTarget(locked),
                    $"Keep the Groups entry that holds this element as it is; other members may leave it. Or ask the user to unlock the element in the timeline. {LockHint}");
            }
        }

        return null;
    }

    private static Dictionary<Guid, int> CountIds(IEnumerable<Element> elements)
    {
        var counts = new Dictionary<Guid, int>();
        foreach (Element element in elements)
        {
            counts[element.Id] = counts.GetValueOrDefault(element.Id) + 1;
        }

        return counts;
    }

    internal static ToolError CreateLockedElementError(Scene scene, Element element, string target)
    {
        string identity = DescribeElement(element);
        if (element.IsLocked)
        {
            return new ToolError(
                ErrorCode.ValidationRejected,
                $"Element {identity} is locked, so an agent cannot change, delete or unlock it.",
                target,
                $"Leave this element out of the edit, or ask the user to unlock it in the timeline. {LockHint}");
        }

        if (scene.IsLayerLocked(element.ZIndex))
        {
            return new ToolError(
                ErrorCode.ValidationRejected,
                $"Element {identity} is on locked layer {element.ZIndex}, so an agent cannot change, delete or move it.",
                target,
                $"Leave this element out of the edit, or ask the user to unlock layer {element.ZIndex} in the timeline. {LockHint}");
        }

        return new ToolError(
            ErrorCode.ValidationRejected,
            $"Element {identity} could not be fully loaded, so it is read-only in the editor until it is repaired.",
            target,
            "Leave this element out of the edit, delete it, or repair it first by removing or replacing what could not be loaded.");
    }

    // Recovery keeps the file of an element that could not be fully loaded until nothing unloadable is left in it,
    // so saving would drop any other edit. The edit may delete the element or leave it repaired.
    private static ToolError? FindUnrepairedProtectedElement(
        IEditingSession session,
        Scene scene,
        Scene sandbox,
        Element[] protectedElements,
        JsonObject currentDocument)
    {
        if (protectedElements.Length == 0)
        {
            return null;
        }

        Dictionary<Guid, JsonObject> current = IndexSerializedElements(currentDocument);
        Dictionary<Guid, JsonObject> applied = IndexSerializedElements(session.Documents.Read(sandbox));
        foreach (Element element in protectedElements)
        {
            if (!applied.TryGetValue(element.Id, out JsonObject? after)
                || (current.TryGetValue(element.Id, out JsonObject? before) && JsonNode.DeepEquals(before, after)))
            {
                continue;
            }

            // The sandbox is a copy, which keeps no object marked as a blocker by identity, so those are found by Id.
            HashSet<Guid> blockers = SerializedGraphTraversal.Enumerate(element)
                .Where(SceneRecovery.IsRecoveryBlocker)
                .OfType<EngineObject>()
                .Select(blocker => blocker.Id)
                .ToHashSet();
            if (sandbox.Children.FirstOrDefault(child => child.Id == element.Id) is not { } repaired
                || SceneRecovery.HoldsUnresolvedRecoveryBlocker(repaired)
                || SerializedGraphTraversal.Enumerate(repaired).OfType<EngineObject>().Any(item => blockers.Contains(item.Id)))
            {
                return CreateLockedElementError(scene, element, ElementTarget(element));
            }
        }

        return null;
    }

    // An element locked before the edit is still there and serializes the same, apart from its own
    // IsLocked, which may only turn true. An element that becomes locked in this edit is new protection.
    private static ToolError? FindChangedLockedElement(
        IEditingSession session,
        Scene scene,
        Scene sandbox,
        Element[] lockedElements,
        JsonObject currentDocument)
    {
        if (lockedElements.Length == 0)
        {
            return null;
        }

        Dictionary<Guid, JsonObject> current = IndexSerializedElements(currentDocument);
        Dictionary<Guid, JsonObject> applied = IndexSerializedElements(session.Documents.Read(sandbox));
        foreach (Element locked in lockedElements)
        {
            if (!current.TryGetValue(locked.Id, out JsonObject? before)
                || !applied.TryGetValue(locked.Id, out JsonObject? after)
                || (locked.IsLocked && !IsTrue(after[nameof(Element.IsLocked)]))
                || !JsonNode.DeepEquals(WithoutLock(before), WithoutLock(after)))
            {
                return CreateLockedElementError(scene, locked, ElementTarget(locked));
            }
        }

        return null;
    }

    // A locked layer keeps its row and its lock. Its Name, Color and mute and solo flags stay editable,
    // as in the timeline.
    private static ToolError? FindUnlockedLayer(Scene scene, Scene sandbox, TimelineLayer[] lockedLayers)
    {
        foreach (TimelineLayer locked in lockedLayers)
        {
            // As with duplicate element Ids, only a corrupted project repeats a layer Id, and then one copy
            // could vouch for another that the edit removed. Refuse until the project is repaired.
            if (scene.Layers.Count(layer => layer.Id == locked.Id) > 1
                || sandbox.Layers.Count(layer => layer.Id == locked.Id) > 1)
            {
                return new ToolError(
                    ErrorCode.ValidationRejected,
                    $"Layer {locked.ZIndex} is locked and shares its Id with another layer, so its lock cannot be checked.",
                    $"$/Layers[Id={locked.Id}]",
                    "The project has duplicate layer Ids that must be repaired before an agent can edit this scene.");
            }

            if (!sandbox.Layers.Any(layer => layer.Id == locked.Id && layer.IsLocked && layer.ZIndex == locked.ZIndex))
            {
                return new ToolError(
                    ErrorCode.ValidationRejected,
                    $"Layer {locked.ZIndex} is locked, so an agent cannot unlock, move or remove it.",
                    $"$/Layers[Id={locked.Id}]",
                    $"Keep the layer's IsLocked, ZIndex and its entry in Layers unchanged; only the user can unlock it in the timeline. {LockHint}");
            }
        }

        return null;
    }

    // Only an element that was already on a locked layer may sit there after the edit, and the check
    // above keeps it unchanged. A new element, or one moved onto the layer, is refused.
    private static ToolError? FindElementPlacedOnLockedLayer(Scene scene, Scene sandbox)
    {
        if (!scene.Layers.Any(layer => layer.IsLocked))
        {
            return null;
        }

        var currentZIndices = new Dictionary<Guid, int>();
        foreach (Element element in scene.Children)
        {
            currentZIndices.TryAdd(element.Id, element.ZIndex);
        }

        foreach (Element placed in sandbox.Children)
        {
            if (scene.IsLayerLocked(placed.ZIndex)
                && (!currentZIndices.TryGetValue(placed.Id, out int zIndex) || zIndex != placed.ZIndex))
            {
                return new ToolError(
                    ErrorCode.ValidationRejected,
                    $"Layer {placed.ZIndex} is locked, so element {DescribeElement(placed)} cannot be placed on it.",
                    ElementTarget(placed),
                    $"Use an unlocked layer (ZIndex), or ask the user to unlock layer {placed.ZIndex} in the timeline. {LockHint}");
            }
        }

        return null;
    }

    private static Dictionary<Guid, JsonObject> IndexSerializedElements(JsonObject document)
    {
        var elements = new Dictionary<Guid, JsonObject>();
        if (document["Elements"] is JsonArray array)
        {
            foreach (JsonObject element in array.OfType<JsonObject>())
            {
                if (CollectionReconciler.TryGetId(element, out Guid id))
                {
                    elements.TryAdd(id, element);
                }
            }
        }

        return elements;
    }

    private static JsonObject WithoutLock(JsonObject element)
    {
        var copy = (JsonObject)element.DeepClone();
        copy.Remove(nameof(Element.IsLocked));
        return copy;
    }

    private static string ElementTarget(Element element) => $"$/Elements[Id={element.Id}]";

    private static bool IsTrue(JsonNode? node)
    {
        return node is JsonValue value && value.GetValueKind() == JsonValueKind.True;
    }

    private static string DescribeElement(Element element)
    {
        return string.IsNullOrWhiteSpace(element.Name)
            ? element.Id.ToString()
            : $"'{element.Name}' ({element.Id})";
    }
}
