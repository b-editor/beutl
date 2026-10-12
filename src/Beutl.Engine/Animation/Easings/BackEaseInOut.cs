namespace Beutl.Animation.Easings;

public sealed class BackEaseInOut : Easing
{
    public override bool TryGetOutputRange(out float minimum, out float maximum)
    {
        minimum = -0.195f;
        maximum = 1.195f;
        return true;
    }

    public override float Ease(float progress)
    {
        if (progress < 0.5f)
        {
            var f = 2f * progress;
            return 0.5f * f * (f * f - MathF.Sin(f * MathF.PI));
        }
        else
        {
            var f = 1f - (2f * progress - 1f);
            return 0.5f * (1f - f * (f * f - MathF.Sin(f * MathF.PI))) + 0.5f;
        }
    }
}
