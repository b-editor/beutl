namespace Beutl.Graphics.Rendering;

public readonly struct RenderHitTestInput
{
    private readonly Func<Point, bool>? _hitTest;

    internal RenderHitTestInput(Rect bounds, Func<Point, bool> hitTest)
    {
        RenderRectValidation.ThrowIfInvalidInput(bounds, nameof(bounds));
        ArgumentNullException.ThrowIfNull(hitTest);
        Bounds = bounds;
        _hitTest = hitTest;
    }

    public Rect Bounds { get; }

    public bool HitTest(Point point)
    {
        if (_hitTest is null)
            throw new InvalidOperationException("The hit-test input is uninitialized.");

        return _hitTest(point);
    }
}
