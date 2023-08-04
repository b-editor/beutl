using Beutl.Graphics;

namespace Beutl.Animation.Animators;

public sealed class PointAnimator : Animator<Point>
{
    public override Point Interpolate(float progress, Point oldValue, Point newValue)
    {
        var deltaX = newValue.X - oldValue.X;
        var deltaY = newValue.Y - oldValue.Y;

        var newX = (deltaX * progress) + oldValue.X;
        var newY = (deltaY * progress) + oldValue.Y;

        return new Point(newX, newY);
    }
}
