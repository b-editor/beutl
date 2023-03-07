namespace Beutl.Animation.Easings;

public class SplineEasing : Easing
{
    public SplineEasing(float x1 = 0, float y1 = 0, float x2 = 1, float y2 = 1)
    {
        _internalKeySpline = new KeySpline();

        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y1 = y2;
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

    public override float Ease(float progress)
    {
        return _internalKeySpline.GetSplineProgress(progress);
    }
}
