using BEditorNext.Graphics;

namespace BEditorNext.Animation.Animators;

public sealed class RectAnimator : Animator<Rect>
{
    public override Rect Interpolate(float progress, Rect oldValue, Rect newValue)
    {
        var deltaX = newValue.X - oldValue.X;
        var deltaY = newValue.Y - oldValue.Y;
        var deltaWidth = newValue.Width - oldValue.Width;
        var deltaHeight = newValue.Height - oldValue.Height;

        var newX = (deltaX * progress) + oldValue.X;
        var newY = (deltaY * progress) + oldValue.Y;
        var newWidth = (deltaWidth * progress) + oldValue.Width;
        var newHeight = (deltaHeight * progress) + oldValue.Height;

        return new Rect(newX, newY, newWidth, newHeight);
    }
}
