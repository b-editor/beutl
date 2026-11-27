using Beutl.Configuration;
using Beutl.Language;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

public sealed class ElementMoveService : IElementMoveService
{
    private readonly HistoryManager _historyManager;
    private readonly EditorConfig _configuration;
    private readonly IElementDuplicateService _duplicateService;

    public ElementMoveService(HistoryManager historyManager, EditorConfig configuration, IElementDuplicateService duplicateService)
    {
        _configuration = configuration;
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
        _duplicateService = duplicateService ?? throw new ArgumentNullException(nameof(duplicateService));
    }

    public ElementMoveOutcome Move(
        Scene scene,
        IReadOnlyList<Element> elements,
        MediaTime deltaStart,
        int deltaZIndex)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(elements);

        if (deltaStart == MediaTime.Zero && deltaZIndex == 0) return ElementMoveOutcome.None;

        Element[] editable = GetMovableElements(scene, elements, deltaZIndex);
        if (editable.Length == 0) return ElementMoveOutcome.None;

        scene.MoveChildren(deltaZIndex, deltaStart, editable, _configuration.AutoAdjustSceneDuration.Value);
        _historyManager.Commit(CommandNames.MoveElement);
        return ElementMoveOutcome.Moved;
    }

    internal static (MediaTime Start, int Layer) PreviewMove(Scene scene, IReadOnlyList<Element> elements,
        Element element, MediaTime deltaStart, int deltaZIndex)
    {
        if (deltaStart == MediaTime.Zero && deltaZIndex == 0) return (element.Start.Value, element.ZIndex.Value);

        Element[] editable = GetMovableElements(scene, elements, deltaZIndex);
        if (editable.Contains(element) && scene.ResolveGroupMove(editable, deltaZIndex, deltaStart) is { } delta)
            return (element.Start.Value + delta, element.ZIndex.Value + deltaZIndex);

        return (element.Start.Value, element.ZIndex.Value);
    }

    public ElementMoveOutcome DuplicateOrMove(
        Scene scene,
        IReadOnlyList<Element> elements,
        MediaTime deltaStart,
        int deltaZIndex)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(elements);

        // Locked sources must not move via the FellBackToMove path, and duplicating
        // them would bypass the lock's editing freeze, so drop them up front.
        Element[] source = FilterUnlocked(scene, elements);
        if (source.Length == 0) return ElementMoveOutcome.None;
        MediaTime minSourceStart = MediaTime.MaxValue;
        int minSourceZIndex = int.MaxValue;
        foreach (Element e in source)
        {
            if (e.Start.Value < minSourceStart) minSourceStart = e.Start.Value;
            if (e.ZIndex.Value < minSourceZIndex) minSourceZIndex = e.ZIndex.Value;
        }

        MediaTime anchorStart = minSourceStart + deltaStart;
        if (anchorStart < MediaTime.Zero) anchorStart = MediaTime.Zero;
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

        if (deltaStart == MediaTime.Zero && deltaZIndex == 0)
        {
            return ElementMoveOutcome.None;
        }

        scene.MoveChildren(deltaZIndex, deltaStart, source, _configuration.AutoAdjustSceneDuration.Value);
        _historyManager.Commit(CommandNames.MoveElement);
        return ElementMoveOutcome.FellBackToMove;
    }

    private static Element[] FilterUnlocked(Scene scene, IReadOnlyList<Element> elements)
        => elements.Where(e => !scene.IsElementLocked(e)).ToArray();

    private static Element[] GetMovableElements(Scene scene, IReadOnlyList<Element> elements, int deltaZIndex)
    {
        Element[] editable = FilterUnlocked(scene, elements);
        // A locked destination cancels the whole move.
        return AnyDestinationLayerLocked(scene, editable, deltaZIndex) ? [] : editable;
    }

    private static bool AnyDestinationLayerLocked(Scene scene, IReadOnlyList<Element> elements, int deltaZIndex)
    {
        foreach (Element e in elements)
        {
            if (scene.IsLayerLocked(Math.Max(e.ZIndex.Value + deltaZIndex, 0))) return true;
        }

        return false;
    }
}
