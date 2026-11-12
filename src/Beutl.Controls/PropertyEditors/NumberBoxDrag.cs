using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Beutl.Controls.PropertyEditors;

// The drag of a number box, or of a slider over one, for VectorEditorBase and NumberSliderEditor. A press waits; once it
// has travelled 3 px it scrubs the value as a drag of the header does, and a press that does not travel edits the text,
// as a click does. A move with the button up or a lost capture ends the press as its release would, since the release
// then never reaches the box. The editor supplies what differs:
// - began, scrub and ended: the scrub's start, each step and its confirmation;
// - clicked: the text editing of a press that did not travel;
// - canArm: whether a press on the box can be a drag at all;
// - canBegin: whether an armed press may begin to scrub once it has travelled;
// - left: what follows a press that ended where nothing says whether the pointer is still over the box.
internal sealed class NumberBoxDrag(
    PropertyEditor editor,
    Action began,
    NumberBoxDrag.ScrubStep scrub,
    Action ended,
    Action<Control> clicked,
    Func<Control, bool>? canArm = null,
    Func<bool>? canBegin = null,
    Action? left = null)
{
    private HeaderScrubGesture _gesture;

    // One step of the scrub of box, by the pointer's move in e.
    public delegate void ScrubStep(ref HeaderScrubGesture gesture, Control box, PointerEventArgs e);

    // Whether a press is armed or scrubbing.
    public bool IsHeld => _gesture.IsHeld;

    public void Subscribe(Control box, CompositeDisposable disposables, RoutingStrategies routes)
        => new ScrubHeaderHandlers(OnPressed, OnReleased, OnMoved, OnCaptureLost, routes).SubscribePointer(box, disposables);

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control box || editor.IsReadOnly || canArm?.Invoke(box) == false)
            return;

        PointerPoint point = e.GetCurrentPoint(box);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        e.Handled = true;
        editor.EndTextEditing();
        _gesture.Arm(point.Position);
        e.Pointer.Capture(box);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Control box || !_gesture.IsHeld)
            return;

        // A move with the button up means the release never reached the box.
        if (!e.Properties.IsLeftButtonPressed)
        {
            Abandon();
            return;
        }

        e.Handled = true;
        if (_gesture.IsArmed)
        {
            if (canBegin?.Invoke() == false || !_gesture.TryBegin(box, e.GetPosition(box)))
                return;

            began();
        }

        scrub(ref _gesture, box, e);
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Control box || !_gesture.IsHeld || e.InitialPressMouseButton != MouseButton.Left)
            return;

        e.Handled = true;
        bool click = _gesture.IsArmed;
        End();
        if (click)
            clicked(box);

        // Only an unlocked pointer can be released away from the box, and then nothing else says that it has left.
        if (!new Rect(box.Bounds.Size).Contains(e.GetPosition(box)))
            left?.Invoke();
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e) => Abandon();

    // Ends a press whose release was lost, after which nothing else says where the pointer went. It is not one when the
    // capture goes after a release, which has ended the press already.
    private void Abandon()
    {
        if (!_gesture.IsHeld)
            return;

        End();
        left?.Invoke();
    }

    private void End()
    {
        _gesture.Disarm();
        if (!_gesture.IsActive)
            return;

        // Ended before confirming, so a confirmation that rebuilds the box cannot end it twice.
        _gesture.End();
        ended();
    }
}
