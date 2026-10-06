using System.Numerics;

using Avalonia;
using Avalonia.Input;

namespace Beutl.Controls.PropertyEditors;

// Drag-to-scrub state of a header text block. Editors keep it in a non-readonly field and call the
// methods on that field, so the state is updated in place rather than on a copy.
internal struct HeaderScrubGesture
{
    private Point _dragStart;
    private double _accumulator;

    public bool IsActive { get; private set; }

    public void Begin(Visual header, Point position)
    {
        _dragStart = position;
        _accumulator = 0;
        PointerLockHelper.Pressed(header, _dragStart);
        IsActive = true;
    }

    public TValue NextDelta<TValue>(Visual header, PointerEventArgs e)
        where TValue : INumber<TValue>
    {
        Point point = e.GetPosition(header);

        // ポインタロック + デルタ取得
        Point move = PointerLockHelper.Moved(header, point, ref _dragStart);
        double scaledX = NumberEditorHelper.ApplyScrubModifier(move.X, e.KeyModifiers);
        return NumberEditorHelper.ConsumeScrubAccumulator<TValue>(ref _accumulator, scaledX);
    }

    public void End()
    {
        PointerLockHelper.Released();
        IsActive = false;
    }
}
