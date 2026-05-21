using Beutl.Language;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

public sealed class ElementResizeService : IElementResizeService
{
    private readonly HistoryManager _historyManager;

    public ElementResizeService(HistoryManager historyManager)
    {
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
    }

    public IElementResizeDragSession BeginResize(
        Scene scene,
        IReadOnlyList<Element> elements,
        ResizeEdge edge,
        bool clampToOriginalDuration)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(elements);
        return new DragSession(_historyManager, scene, elements, edge, clampToOriginalDuration);
    }

    private sealed class DragSession : IElementResizeDragSession
    {
        private readonly HistoryManager _historyManager;
        private readonly Scene _scene;
        private readonly InitialState[] _initial;
        private bool _disposed;
        private bool _settled;

        public DragSession(
            HistoryManager historyManager,
            Scene scene,
            IReadOnlyList<Element> elements,
            ResizeEdge edge,
            bool clampToOriginalDuration)
        {
            _historyManager = historyManager;
            _scene = scene;
            Edge = edge;
            ClampToOriginalDuration = clampToOriginalDuration;
            _initial = elements
                .Select(e => new InitialState(e, e.Start, e.Length, e.ZIndex))
                .ToArray();
        }

        public ResizeEdge Edge { get; }

        public bool ClampToOriginalDuration { get; }

        public void Commit(IReadOnlyList<ElementResizeRequest> finalSizes)
        {
            if (_disposed || _settled) return;
            ArgumentNullException.ThrowIfNull(finalSizes);
            _settled = true;

            if (finalSizes.Count == 0) return;

            foreach (ElementResizeRequest req in finalSizes)
            {
                _scene.MoveChild(req.ZIndex, req.NewStart, req.NewLength, req.Element);
            }

            _historyManager.Commit(CommandNames.MoveElement);
        }

        public void Cancel()
        {
            if (_disposed || _settled) return;
            _settled = true;

            foreach (InitialState s in _initial)
            {
                _scene.MoveChild(s.ZIndex, s.Start, s.Length, s.Element);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }

        private readonly record struct InitialState(Element Element, TimeSpan Start, TimeSpan Length, int ZIndex);
    }
}
