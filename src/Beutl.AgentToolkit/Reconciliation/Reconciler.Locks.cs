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
    // layer (Scene.IsElementLocked), and to place an element on a locked layer. Judge every change against
    // the current scene so an agent's edit follows the same rule. Locking is allowed; unlocking stays a
    // user action.
    private static void ThrowIfLockedChanges(CoreObject root, CoreObject sandboxRoot, List<ChangeSetEntry> changes)
    {
        if (root is not Scene scene || changes.Count == 0)
        {
            return;
        }

        // A corrupted project can repeat an Id; a locked copy keeps that Id locked.
        ILookup<Guid, Element> elements = scene.Children.ToLookup(element => element.Id);
        foreach (ChangeSetEntry change in changes)
        {
            if (FindLockViolation(scene, sandboxRoot, elements, change) is { } error)
            {
                throw new ReconcileException(error);
            }
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

    private static ToolError? FindLockViolation(
        Scene scene,
        CoreObject sandboxRoot,
        ILookup<Guid, Element> elements,
        ChangeSetEntry change)
    {
        if (TrySplitItemPath(change.Path, ElementsPath, out Guid elementId, out string member))
        {
            // Setting IsLocked to true only adds protection, even on an element its layer already locks.
            if (member == "/IsLocked" && IsTrue(change.NewValue))
            {
                return null;
            }

            if (elements[elementId].FirstOrDefault(scene.IsElementLocked) is { } locked)
            {
                return CreateLockedElementError(scene, locked, change.Path);
            }

            return member == "/ZIndex" && FindElement(sandboxRoot, elementId) is { } moved
                ? CreateLockedLayerPlacementError(scene, moved, change.Path)
                : null;
        }

        if (change.Path == ElementsPath)
        {
            switch (change.Operation)
            {
                case ChangeOperations.RemoveChild when Guid.TryParse(change.TargetId, out Guid removedId)
                    && elements[removedId].FirstOrDefault(scene.IsElementLocked) is { } removed:
                    return CreateLockedElementError(scene, removed, $"{ElementsPath}[Id={removed.Id}]");
                case ChangeOperations.InsertChild when Guid.TryParse(change.TargetId, out Guid insertedId)
                    && FindElement(sandboxRoot, insertedId) is { } inserted:
                    return CreateLockedLayerPlacementError(scene, inserted, $"{ElementsPath}[Id={inserted.Id}]");
                // A full desired document that omits Elements clears the scene.
                case ChangeOperations.SetProperty when scene.Children.FirstOrDefault(scene.IsElementLocked) is { } cleared:
                    return CreateLockedElementError(scene, cleared, $"{ElementsPath}[Id={cleared.Id}]");
            }

            // A move within the Elements array changes no timeline position: inserting or deleting
            // another element shifts every later index.
            return null;
        }

        // A locked layer keeps its Name, Color and mute and solo flags editable, as in the timeline. Moving
        // or removing it would unlock its row.
        if (TrySplitItemPath(change.Path, LayersPath, out Guid layerId, out string layerMember))
        {
            return layerMember is "/IsLocked" or "/ZIndex" && FindLockedLayer(scene, layerId) is { } layer
                ? CreateLayerUnlockError(layer, change.Path)
                : null;
        }

        return change.Path == LayersPath
               && change.Operation == ChangeOperations.RemoveChild
               && Guid.TryParse(change.TargetId, out Guid removedLayerId)
               && FindLockedLayer(scene, removedLayerId) is { } removedLayer
            ? CreateLayerUnlockError(removedLayer, $"{LayersPath}[Id={removedLayer.Id}]")
            : null;
    }

    private static ToolError? CreateLockedLayerPlacementError(Scene scene, Element placed, string target)
    {
        if (!scene.IsLayerLocked(placed.ZIndex))
        {
            return null;
        }

        return new ToolError(
            ErrorCode.ValidationRejected,
            $"Layer {placed.ZIndex} is locked, so element {DescribeElement(placed)} cannot be placed on it.",
            target,
            $"Use an unlocked layer (ZIndex), or ask the user to unlock layer {placed.ZIndex} in the timeline. {LockHint}");
    }

    private static ToolError CreateLayerUnlockError(TimelineLayer layer, string target)
    {
        return new ToolError(
            ErrorCode.ValidationRejected,
            $"Layer {layer.ZIndex} is locked, so an agent cannot unlock, move or remove it.",
            target,
            $"Keep the layer's IsLocked, ZIndex and its entry in Layers unchanged; only the user can unlock it in the timeline. {LockHint}");
    }

    private static TimelineLayer? FindLockedLayer(Scene scene, Guid layerId)
    {
        return scene.Layers.FirstOrDefault(layer => layer.Id == layerId && layer.IsLocked);
    }

    // The sandbox holds the edit already applied, so it carries the ZIndex an element ends up on.
    private static Element? FindElement(CoreObject sandboxRoot, Guid elementId)
    {
        return IdentityHelper.FindById(sandboxRoot, elementId) as Element;
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
