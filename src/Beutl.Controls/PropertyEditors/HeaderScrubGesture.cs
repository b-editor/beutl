using System.Numerics;

using Avalonia;
using Avalonia.Input;

namespace Beutl.Controls.PropertyEditors;

// Drag-to-scrub state of a header text block, or of a number box that is also a slider. Editors keep it in a
// non-readonly field and call the methods on that field, so the state is updated in place rather than on a copy.
// A scrub hides the cursor until End; editors also end it when the header loses the pointer capture
// or sees a move with the button up, since the release then never reaches the header.
internal struct HeaderScrubGesture
{
    // How far an armed press travels before it is a scrub.
    private const double ClickTolerance = 3;

    private Point _dragStart;
    private double _accumulator;

    // Scrubs begun and not yet ended across all editors; tests check that the cursor is shown again.
    internal static int ActiveCount { get; private set; }

    public bool IsActive { get; private set; }

    // Whether a press on a box has yet to travel, so that it can still be a click.
    public bool IsArmed { get; private set; }

    // Whether a press is armed or scrubbing.
    public bool IsHeld => IsArmed || IsActive;

    // Waits for the press to travel, on a box where a click edits the text. The pointer locks only once it has, so that
    // a click does not make the cursor blink.
    public void Arm(Point position)
    {
        _dragStart = position;
        IsArmed = true;
    }

    public void Disarm() => IsArmed = false;

    // Begins the scrub once the armed press has travelled, and says whether it has.
    public bool TryBegin(Visual header, Point position)
    {
        if (Math.Abs(position.X - _dragStart.X) < ClickTolerance)
            return false;

        IsArmed = false;
        Begin(header, position);
        return true;
    }

    // Begins the scrub where the armed press was, once it has travelled along either axis, so the travel still counts
    // as a move: for a pad that moves in two dimensions.
    public bool TryBeginAtPress(Visual header, Point position)
    {
        if (Math.Abs(position.X - _dragStart.X) < ClickTolerance && Math.Abs(position.Y - _dragStart.Y) < ClickTolerance)
            return false;

        IsArmed = false;
        Begin(header, _dragStart);
        return true;
    }

    public void Begin(Visual header, Point position)
    {
        _dragStart = position;
        _accumulator = 0;
        PointerLockHelper.Pressed(header, _dragStart);
        IsActive = true;
        ActiveCount++;
    }

    // The pointer's move since the last one, with the pointer held where the scrub began.
    public Point NextMove(Visual header, PointerEventArgs e)
        => PointerLockHelper.Moved(header, e.GetPosition(header), ref _dragStart);

    public TValue NextDelta<TValue>(Visual header, PointerEventArgs e)
        where TValue : INumber<TValue>
    {
        Point move = NextMove(header, e);
        double scaledX = NumberEditorHelper.ApplyScrubModifier(move.X, e.KeyModifiers);
        return NumberEditorHelper.ConsumeScrubAccumulator<TValue>(ref _accumulator, scaledX);
    }

    public void End()
    {
        PointerLockHelper.Released();
        IsActive = false;
        ActiveCount--;
    }
}
