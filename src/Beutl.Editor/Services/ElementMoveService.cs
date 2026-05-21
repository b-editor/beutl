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

        if (elements.Count == 0) return ElementMoveOutcome.None;
        if (deltaStart == TimeSpan.Zero && deltaZIndex == 0) return ElementMoveOutcome.None;

        scene.MoveChildren(deltaZIndex, deltaStart, elements.ToArray());
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

        if (elements.Count == 0) return ElementMoveOutcome.None;

        Element[] source = elements.ToArray();
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
}
