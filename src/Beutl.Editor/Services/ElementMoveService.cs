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

    public IElementMoveDragSession BeginMove(
        Scene scene,
        IReadOnlyList<Element> elements,
        Element anchor,
        bool duplicateMode)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(anchor);
        return new DragSession(_historyManager, _duplicateService, scene, elements, anchor, duplicateMode);
    }

    private sealed class DragSession : IElementMoveDragSession
    {
        private readonly HistoryManager _historyManager;
        private readonly IElementDuplicateService _duplicateService;
        private readonly Scene _scene;
        private readonly Element[] _elements;
        private readonly Element _anchor;
        private readonly bool _duplicateMode;
        private bool _disposed;
        private bool _settled;

        public DragSession(
            HistoryManager historyManager,
            IElementDuplicateService duplicateService,
            Scene scene,
            IReadOnlyList<Element> elements,
            Element anchor,
            bool duplicateMode)
        {
            _historyManager = historyManager;
            _duplicateService = duplicateService;
            _scene = scene;
            _elements = elements.ToArray();
            _anchor = anchor;
            _duplicateMode = duplicateMode;
        }

        public ElementMoveOutcome Commit(TimeSpan deltaStart, int deltaZIndex)
        {
            if (_disposed || _settled) return ElementMoveOutcome.None;
            _settled = true;

            if (_elements.Length == 0) return ElementMoveOutcome.None;
            if (deltaStart == TimeSpan.Zero && deltaZIndex == 0 && !_duplicateMode)
            {
                return ElementMoveOutcome.None;
            }

            if (_duplicateMode)
            {
                TimeSpan minSourceStart = TimeSpan.MaxValue;
                int minSourceZIndex = int.MaxValue;
                foreach (Element e in _elements)
                {
                    if (e.Start < minSourceStart) minSourceStart = e.Start;
                    if (e.ZIndex < minSourceZIndex) minSourceZIndex = e.ZIndex;
                }

                TimeSpan anchorStart = minSourceStart + deltaStart;
                if (anchorStart < TimeSpan.Zero) anchorStart = TimeSpan.Zero;
                int anchorZIndex = Math.Max(minSourceZIndex + deltaZIndex, 0);

                if (_duplicateService.WouldOverlap(_elements, anchorStart, anchorZIndex))
                {
                    return ElementMoveOutcome.DuplicateOverlapsSource;
                }

                if (_duplicateService.DuplicateAtPosition(_scene, _elements, anchorStart, anchorZIndex))
                {
                    return ElementMoveOutcome.Duplicated;
                }

                if (deltaStart == TimeSpan.Zero && deltaZIndex == 0)
                {
                    return ElementMoveOutcome.None;
                }

                _scene.MoveChildren(deltaZIndex, deltaStart, _elements);
                _historyManager.Commit(CommandNames.MoveElement);
                return ElementMoveOutcome.FellBackToMove;
            }

            _scene.MoveChildren(deltaZIndex, deltaStart, _elements);
            _historyManager.Commit(CommandNames.MoveElement);
            return ElementMoveOutcome.Moved;
        }

        public void Cancel()
        {
            if (_disposed || _settled) return;
            _settled = true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
