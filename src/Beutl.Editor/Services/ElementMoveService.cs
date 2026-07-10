using Beutl.Language;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

public sealed class ElementMoveService : IElementMoveService
{
    private readonly HistoryManager _historyManager;
    private readonly IElementDuplicateService _duplicateService;

    public ElementMoveService(HistoryManager historyManager, IElementDuplicateService duplicateService)
    {
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
        _duplicateService = duplicateService ?? throw new ArgumentNullException(nameof(duplicateService));
    }

    public ElementMoveOutcome Move(
        Scene scene,
        IReadOnlyList<Element> elements,
        TimeSpan deltaStart,
        int deltaZIndex)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(elements);

        if (deltaStart == TimeSpan.Zero && deltaZIndex == 0) return ElementMoveOutcome.None;

        Element[] editable = FilterUnlocked(scene, elements);
        if (editable.Length == 0) return ElementMoveOutcome.None;

        // Refuse dropping content onto a locked destination layer. None makes the
        // caller snap the drag visuals back to the model.
        if (AnyDestinationLayerLocked(scene, editable, deltaZIndex)) return ElementMoveOutcome.None;

        scene.MoveChildren(deltaZIndex, deltaStart, editable);
        _historyManager.Commit(CommandNames.MoveElement);
        return ElementMoveOutcome.Moved;
    }

    public ElementMoveOutcome DuplicateOrMove(
        Scene scene,
        IReadOnlyList<Element> elements,
        TimeSpan deltaStart,
        int deltaZIndex)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(elements);

        // Locked sources must not move via the FellBackToMove path, and duplicating
        // them would bypass the lock's editing freeze, so drop them up front.
        Element[] source = FilterUnlocked(scene, elements);
        if (source.Length == 0) return ElementMoveOutcome.None;
        TimeSpan minSourceStart = TimeSpan.MaxValue;
        int minSourceZIndex = int.MaxValue;
        foreach (Element e in source)
        {
            if (e.Start < minSourceStart) minSourceStart = e.Start;
            if (e.ZIndex < minSourceZIndex) minSourceZIndex = e.ZIndex;
        }

        TimeSpan anchorStart = minSourceStart + deltaStart;
        if (anchorStart < TimeSpan.Zero) anchorStart = TimeSpan.Zero;
        int anchorZIndex = Math.Max(minSourceZIndex + deltaZIndex, 0);

        // Refuse landing the duplicate (or its move fallback) on a locked layer.
        if (AnyDestinationLayerLocked(scene, source, anchorZIndex - minSourceZIndex))
        {
            return ElementMoveOutcome.None;
        }

        if (_duplicateService.WouldOverlap(source, anchorStart, anchorZIndex))
        {
            return ElementMoveOutcome.DuplicateOverlapsSource;
        }

        if (_duplicateService.DuplicateAtPosition(scene, source, anchorStart, anchorZIndex))
        {
            return ElementMoveOutcome.Duplicated;
        }

        if (deltaStart == TimeSpan.Zero && deltaZIndex == 0)
        {
            return ElementMoveOutcome.None;
        }

        scene.MoveChildren(deltaZIndex, deltaStart, source);
        _historyManager.Commit(CommandNames.MoveElement);
        return ElementMoveOutcome.FellBackToMove;
    }

    private static Element[] FilterUnlocked(Scene scene, IReadOnlyList<Element> elements)
        => elements.Where(e => !scene.IsElementLocked(e)).ToArray();

    private static bool AnyDestinationLayerLocked(Scene scene, IReadOnlyList<Element> elements, int deltaZIndex)
    {
        foreach (Element e in elements)
        {
            if (scene.IsLayerLocked(Math.Max(e.ZIndex + deltaZIndex, 0))) return true;
        }

        return false;
    }
}
