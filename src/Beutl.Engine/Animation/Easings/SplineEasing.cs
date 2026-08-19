namespace Beutl.Animation.Easings;

public class SplineEasing : Easing
{
    public SplineEasing(float x1 = 0, float y1 = 0, float x2 = 1, float y2 = 1)
    {
        _internalKeySpline = new KeySpline();

        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;
    }

    public SplineEasing(KeySpline keySpline)
    {
        _internalKeySpline = keySpline;
    }

    public SplineEasing()
    {
        _internalKeySpline = new KeySpline();
    }

    public event EventHandler? Changed;

    public float X1
    {
        get => _internalKeySpline.ControlPointX1;
        set
        {
            _internalKeySpline.ControlPointX1 = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public float Y1
    {
        get => _internalKeySpline.ControlPointY1;
        set
        {
            _internalKeySpline.ControlPointY1 = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public float X2
    {
        get => _internalKeySpline.ControlPointX2;
        set
        {
            _internalKeySpline.ControlPointX2 = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public float Y2
    {
        get => _internalKeySpline.ControlPointY2;
        set
        {
            _internalKeySpline.ControlPointY2 = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private readonly KeySpline _internalKeySpline;

    public override bool TryGetOutputRange(out float minimum, out float maximum)
    {
        // KeySpline.Build caches 3*Y1 and 3*Y2. Finite control points can still overflow those
        // coefficients, in which case Ease may produce a non-finite value despite a finite hull.
        float bx = 3f * X1;
        float cx = 3f * X2;
        float cxBx = 2f * (cx - bx);
        float threeCx = 3f - cx;
        float by = 3f * Y1;
        float cy = 3f * Y2;
        if (X1 < 0f
            || X1 > 1f
            || X2 < 0f
            || X2 > 1f
            || !float.IsFinite(X1)
            || !float.IsFinite(X2)
            || !float.IsFinite(Y1)
            || !float.IsFinite(Y2)
            || !float.IsFinite(bx)
            || !float.IsFinite(cx)
            || !float.IsFinite(cxBx)
            || !float.IsFinite(threeCx)
            || !float.IsFinite(by)
            || !float.IsFinite(cy))
        {
            minimum = default;
            maximum = default;
            return false;
        }

        // A cubic Bézier curve stays inside the convex hull of its control points.
        minimum = Math.Min(0, Math.Min(Y1, Math.Min(Y2, 1)));
        maximum = Math.Max(0, Math.Max(Y1, Math.Max(Y2, 1)));
        return float.IsFinite(minimum) && float.IsFinite(maximum);
    }

    public override float Ease(float progress)
    {
        return _internalKeySpline.GetSplineProgress(progress);
    }
}
