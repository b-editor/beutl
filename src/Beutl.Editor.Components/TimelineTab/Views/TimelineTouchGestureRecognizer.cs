using Avalonia;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;

namespace Beutl.Editor.Components.TimelineTab.Views;

// The scroll recognizer already owns the first pointer after a pan begins. Keeping both gestures here lets the
// second pointer promote that pan using the first pointer's current position, then capture both for the pinch.
internal sealed class TimelineTouchGestureRecognizer : ScrollGestureRecognizer
{
    private IPointer? _first;
    private IPointer? _second;
    private Point _firstPoint;
    private Point _secondPoint;
    private Point _origin;
    private double _initialDistance;

    protected override void PointerPressed(PointerPressedEventArgs e)
    {
        if (e.Pointer.Type != PointerType.Touch || Target is not Visual visual)
        {
            base.PointerPressed(e);
            return;
        }

        if (_first is null)
        {
            _first = e.Pointer;
            _firstPoint = e.GetPosition(visual);
            base.PointerPressed(e);
        }
        else if (_second is null && e.Pointer != _first)
        {
            base.PointerCaptureLost(_first);
            _second = e.Pointer;
            _secondPoint = e.GetPosition(visual);
            _origin = (_firstPoint + _secondPoint) / 2;
            _initialDistance = ((Vector)(_firstPoint - _secondPoint)).Length;
            Capture(_first);
            Capture(_second);
            e.Handled = true;
        }
    }

    protected override void PointerMoved(PointerEventArgs e)
    {
        if (Target is Visual visual)
        {
            if (e.Pointer == _first)
                _firstPoint = e.GetPosition(visual);
            else if (e.Pointer == _second)
                _secondPoint = e.GetPosition(visual);
        }

        if (_second == null)
        {
            base.PointerMoved(e);
        }
        else if (e.Pointer == _first || e.Pointer == _second)
        {
            double distance = ((Vector)(_firstPoint - _secondPoint)).Length;
            if (_initialDistance == 0) _initialDistance = distance;
            if (distance > 0)
                Target?.RaiseEvent(new PinchEventArgs(distance / _initialDistance, _origin));
            e.Handled = true;
        }
    }

    protected override void PointerReleased(PointerReleasedEventArgs e)
    {
        if (EndContact(e.Pointer))
            e.Handled = true;
        else
            base.PointerReleased(e);
    }

    protected override void PointerCaptureLost(IPointer pointer)
    {
        EndContact(pointer);
        base.PointerCaptureLost(pointer);
    }

    private bool EndContact(IPointer pointer)
    {
        if (pointer != _first && pointer != _second) return false;

        bool wasPinch = _second != null;
        _first = _second = null;
        if (wasPinch) Target?.RaiseEvent(new PinchEndedEventArgs());
        return wasPinch;
    }
}
