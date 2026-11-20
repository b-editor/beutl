using Avalonia;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Interactivity;

namespace Beutl.Editor.Components.Helpers;

internal readonly record struct TwoFingerTouchState(
    Point StartCenter,
    Point CurrentCenter,
    double ScaleFactor,
    Vector PanDelta,
    KeyModifiers KeyModifiers);

internal sealed class TwoFingerTouchGestureRecognizer(
    Action<Point> onStarted,
    Action<TwoFingerTouchState> onUpdated,
    Action? onEnded = null,
    Visual? coordinateTarget = null) : GestureRecognizer
{
    private const double MinPinchDistance = 4.0;

    private IPointer? _firstContact;
    private Point _firstPoint;
    private IPointer? _secondContact;
    private Point _secondPoint;
    private Point _startCenter;
    private double _startDistance;
    private bool _active;
    private bool _firstCapturedByRecognizer;
    private bool _suppressTouchHolding;
    private IInputElement? _holdingTarget;

    private Visual? ReferenceVisual => coordinateTarget ?? Target as Visual;

    protected override void PointerPressed(PointerPressedEventArgs e)
    {
        if (e.Pointer.Type != PointerType.Touch || ReferenceVisual is not { } visual)
        {
            return;
        }

        EnsureHoldingHandler();

        // Discard a stale first contact if its touch ended on a detached child without bubbling here.
        // A contact captured by this recognizer has Captured == null in Avalonia, so keep it alive.
        if (_firstContact != null && !_active && !_firstCapturedByRecognizer && _firstContact.Captured == null)
        {
            _firstContact = null;
        }

        if (_firstContact == null)
        {
            _suppressTouchHolding = false;
            _firstContact = e.Pointer;
            _firstPoint = e.GetPosition(visual);
            _firstCapturedByRecognizer = false;
            return;
        }

        if (_secondContact == null && _firstContact != e.Pointer)
        {
            _secondContact = e.Pointer;
            _secondPoint = e.GetPosition(visual);
            _startCenter = Midpoint(_firstPoint, _secondPoint);
            _startDistance = Distance(_firstPoint, _secondPoint);
            _active = true;
            _firstCapturedByRecognizer = true;
            // Avalonia's Gestures.PointerPressed arms a touch-hold timer on RouteFinished even after
            // a GestureRecognizer captures the pointer, and captured TouchUpdate/TouchEnd bypass the
            // element tree so that timer is never cancelled. Suppress the resulting stale HoldingEvent.
            _suppressTouchHolding = true;

            Capture(_firstContact);
            Capture(_secondContact);
            e.PreventGestureRecognition();
            e.Handled = true;
            onStarted(_startCenter);
        }
    }

    protected override void PointerMoved(PointerEventArgs e)
    {
        if (ReferenceVisual is not { } visual)
        {
            return;
        }

        if (e.Pointer == _firstContact)
        {
            _firstPoint = e.GetPosition(visual);
        }
        else if (e.Pointer == _secondContact)
        {
            _secondPoint = e.GetPosition(visual);
        }
        else
        {
            return;
        }

        if (!_active || _firstContact == null || _secondContact == null)
        {
            return;
        }

        Point currentCenter = Midpoint(_firstPoint, _secondPoint);
        double currentDistance = Distance(_firstPoint, _secondPoint);
        double scaleFactor = _startDistance >= MinPinchDistance && currentDistance >= MinPinchDistance
            ? currentDistance / _startDistance
            : 1.0;

        if (!double.IsFinite(scaleFactor) || scaleFactor <= 0)
        {
            scaleFactor = 1.0;
        }

        onUpdated(new TwoFingerTouchState(
            _startCenter,
            currentCenter,
            scaleFactor,
            currentCenter - _startCenter,
            e.KeyModifiers));
        e.Handled = true;
        e.PreventGestureRecognition();
    }

    protected override void PointerReleased(PointerReleasedEventArgs e)
    {
        bool wasActive = _active;
        if (RemoveContact(e.Pointer) && wasActive)
        {
            e.PreventGestureRecognition();
            e.Handled = true;
        }
    }

    protected override void PointerCaptureLost(IPointer pointer)
    {
        RemoveContact(pointer);
    }

    private void EnsureHoldingHandler()
    {
        if (ReferenceEquals(_holdingTarget, Target))
        {
            return;
        }

        _holdingTarget?.RemoveHandler(InputElement.HoldingEvent, OnTargetHolding);
        _holdingTarget = Target;
        _holdingTarget?.AddHandler(InputElement.HoldingEvent, OnTargetHolding, RoutingStrategies.Bubble);
    }

    private void OnTargetHolding(object? sender, HoldingRoutedEventArgs e)
    {
        if (e.PointerType != PointerType.Touch || (!_active && !_suppressTouchHolding))
        {
            return;
        }

        e.Handled = true;
        if (e.HoldingState == HoldingState.Started)
        {
            _suppressTouchHolding = false;
        }
    }

    private bool RemoveContact(IPointer pointer)
    {
        if (pointer != _firstContact && pointer != _secondContact)
        {
            return false;
        }

        if (pointer == _secondContact)
        {
            _secondContact = null;
        }
        else
        {
            _firstContact = _secondContact;
            _firstPoint = _secondPoint;
            _secondContact = null;
            if (_firstContact == null)
            {
                _firstCapturedByRecognizer = false;
            }
        }

        if (_active)
        {
            _active = false;
            onEnded?.Invoke();
        }

        return true;
    }

    private static Point Midpoint(Point a, Point b) => new((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5);

    private static double Distance(Point a, Point b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
