using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Reconciliation;

public sealed partial class Reconciler
{
    private const string ElementsPath = "$/Elements";
    private const string LayersPath = "$/Layers";
    private const string LockHint = "Agents can lock elements and layers but cannot unlock them.";

    // The timeline and property editors refuse to change an element that is locked or sits on a locked
    // layer (Scene.IsElementLocked), and to place an element on a locked layer. Agent edits follow the
    // same rule, judged against the current scene. Locking is allowed; unlocking stays a user action.
    //
    // The planned changes alone are not enough: MCP binds JSON case-insensitively, so a desired document
    // can carry "islocked" or "zindex", which the diff reports under that spelling while the applier still
    // reads it as IsLocked or ZIndex. So the applied sandbox is checked as well, and so is $type, which
    // the diff never reports.
    private static void ThrowIfLockedChanges(
        CoreObject root,
        CoreObject sandboxRoot,
        JsonObject currentDocument,
        JsonObject desiredDocument,
        List<ChangeSetEntry> changes)
    {
        if (root is not Scene scene || sandboxRoot is not Scene sandbox)
        {
            return;
        }

        Element[] lockedElements = scene.Children.Where(scene.IsElementLocked).ToArray();
        TimelineLayer[] lockedLayers = scene.Layers.Where(layer => layer.IsLocked).ToArray();
        ToolError? error = FindPlannedLockViolation(scene, lockedElements, lockedLayers, changes)
                           ?? FindAppliedLockViolation(scene, sandbox, lockedElements, lockedLayers)
                           ?? FindLockedTypeChange(scene, lockedElements, currentDocument, desiredDocument);
        if (error is not null)
        {
            throw new ReconcileException(error);
        }
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
            $"Element {identity} is read-only in the editor, so an agent cannot change or delete it.",
            target,
            "Leave this element out of the edit.");
    }

    // What the plan says: any change under a locked element, deleting it, or replacing the whole
    // Elements or Layers array while it holds something locked.
    private static ToolError? FindPlannedLockViolation(
        Scene scene,
        Element[] lockedElements,
        TimelineLayer[] lockedLayers,
        List<ChangeSetEntry> changes)
    {
        if (lockedElements.Length == 0 && lockedLayers.Length == 0)
        {
            return null;
        }

        foreach (ChangeSetEntry change in changes)
        {
            if (TrySplitItemPath(change.Path, ElementsPath, out Guid elementId, out string member))
            {
                // Setting IsLocked to true only adds protection, even on an element its layer already locks.
                if (member.Equals("/IsLocked", StringComparison.OrdinalIgnoreCase) && IsTrue(change.NewValue))
                {
                    continue;
                }

                if (lockedElements.FirstOrDefault(element => element.Id == elementId) is { } locked)
                {
                    return CreateLockedElementError(scene, locked, change.Path);
                }
            }
            else if (change.Path == ElementsPath)
            {
                // Inserts are judged on the applied result below. A move inside the array changes no
                // timeline position: inserting or deleting another element shifts every later index.
                Element? removed = change.Operation switch
                {
                    ChangeOperations.SetProperty => lockedElements.FirstOrDefault(),
                    ChangeOperations.RemoveChild when Guid.TryParse(change.TargetId, out Guid removedId)
                        => lockedElements.FirstOrDefault(element => element.Id == removedId),
                    _ => null,
                };
                if (removed is not null)
                {
                    return CreateLockedElementError(scene, removed, $"{ElementsPath}[Id={removed.Id}]");
                }
            }
            else if (change.Path == LayersPath
                     && change.Operation == ChangeOperations.SetProperty
                     && lockedLayers.Length > 0)
            {
                return CreateLayerUnlockError(lockedLayers[0], LayersPath);
            }
        }

        return null;
    }

    // What the apply does, whatever shape the edit took: a locked element keeps its timing, layer and
    // lock; a locked layer keeps its row and lock; nothing new or moved lands on a locked layer.
    private static ToolError? FindAppliedLockViolation(
        Scene scene,
        Scene sandbox,
        Element[] lockedElements,
        TimelineLayer[] lockedLayers)
    {
        foreach (Element locked in lockedElements)
        {
            Element? applied = sandbox.Children.FirstOrDefault(element => element.Id == locked.Id);
            if (applied is null
                || applied.Start != locked.Start
                || applied.Length != locked.Length
                || applied.ZIndex != locked.ZIndex
                || (locked.IsLocked && !applied.IsLocked))
            {
                return CreateLockedElementError(scene, locked, $"{ElementsPath}[Id={locked.Id}]");
            }
        }

        foreach (TimelineLayer locked in lockedLayers)
        {
            if (!sandbox.Layers.Any(layer => layer.Id == locked.Id && layer.IsLocked && layer.ZIndex == locked.ZIndex))
            {
                return CreateLayerUnlockError(locked, $"{LayersPath}[Id={locked.Id}]");
            }
        }

        if (lockedLayers.Length == 0)
        {
            return null;
        }

        Dictionary<Guid, int> currentZIndices = scene.Children
            .GroupBy(element => element.Id)
            .ToDictionary(group => group.Key, group => group.First().ZIndex);
        foreach (Element applied in sandbox.Children)
        {
            bool placed = !currentZIndices.TryGetValue(applied.Id, out int zIndex) || zIndex != applied.ZIndex;
            if (placed && scene.IsLayerLocked(applied.ZIndex))
            {
                return new ToolError(
                    ErrorCode.ValidationRejected,
                    $"Layer {applied.ZIndex} is locked, so element {DescribeElement(applied)} cannot be placed on it.",
                    $"{ElementsPath}[Id={applied.Id}]",
                    $"Use an unlocked layer (ZIndex), or ask the user to unlock layer {applied.ZIndex} in the timeline. {LockHint}");
            }
        }

        return null;
    }

    // The diff skips $type, so replacing an object inside a locked element with one of another type
    // that has the same members leaves no planned change, while the applier swaps the object.
    private static ToolError? FindLockedTypeChange(
        Scene scene,
        Element[] lockedElements,
        JsonObject currentDocument,
        JsonObject desiredDocument)
    {
        if (lockedElements.Length == 0
            || currentDocument["Elements"] is not JsonArray currentElements
            || desiredDocument["Elements"] is not JsonArray desiredElements)
        {
            return null;
        }

        Dictionary<Guid, (int Index, JsonObject Node)> current = IndexById(currentElements);
        Dictionary<Guid, (int Index, JsonObject Node)> desired = IndexById(desiredElements);
        foreach (Element locked in lockedElements)
        {
            if (current.TryGetValue(locked.Id, out (int Index, JsonObject Node) currentItem)
                && desired.TryGetValue(locked.Id, out (int Index, JsonObject Node) desiredItem)
                && HasTypeChange(currentItem.Node, desiredItem.Node))
            {
                return CreateLockedElementError(scene, locked, $"{ElementsPath}[Id={locked.Id}]");
            }
        }

        return null;
    }

    // Walks the nodes the diff pairs: objects by member name, identity arrays by Id. The diff compares
    // other arrays whole, $type included.
    private static bool HasTypeChange(JsonNode? current, JsonNode? desired)
    {
        switch (current, desired)
        {
            case (JsonObject currentObject, JsonObject desiredObject):
                if (!JsonEquals(currentObject["$type"], desiredObject["$type"])
                    && (currentObject.GetDiscriminator() is not { } currentType
                        || currentType != desiredObject.GetDiscriminator()))
                {
                    return true;
                }

                foreach (KeyValuePair<string, JsonNode?> pair in desiredObject)
                {
                    if (currentObject.TryGetPropertyValue(pair.Key, out JsonNode? child) && HasTypeChange(child, pair.Value))
                    {
                        return true;
                    }
                }

                return false;
            case (JsonArray currentArray, JsonArray desiredArray):
                Dictionary<Guid, (int Index, JsonObject Node)> currentById = IndexById(currentArray);
                foreach (KeyValuePair<Guid, (int Index, JsonObject Node)> item in IndexById(desiredArray))
                {
                    if (currentById.TryGetValue(item.Key, out (int Index, JsonObject Node) currentItem)
                        && HasTypeChange(currentItem.Node, item.Value.Node))
                    {
                        return true;
                    }
                }

                return false;
            default:
                return false;
        }
    }

    private static ToolError CreateLayerUnlockError(TimelineLayer layer, string target)
    {
        return new ToolError(
            ErrorCode.ValidationRejected,
            $"Layer {layer.ZIndex} is locked, so an agent cannot unlock, move or remove it.",
            target,
            $"Keep the layer's IsLocked, ZIndex and its entry in Layers unchanged; only the user can unlock it in the timeline. {LockHint}");
    }

    // Splits "$/Elements[Id=<guid>]/Start" into the item's Id and the member path after it.
    private static bool TrySplitItemPath(string path, string arrayPath, out Guid id, out string member)
    {
        id = Guid.Empty;
        member = string.Empty;
        string prefix = $"{arrayPath}[Id=";
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        int end = path.IndexOf(']', prefix.Length);
        if (end < 0 || !Guid.TryParse(path.AsSpan(prefix.Length, end - prefix.Length), out id))
        {
            return false;
        }

        member = path[(end + 1)..];
        return true;
    }

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
