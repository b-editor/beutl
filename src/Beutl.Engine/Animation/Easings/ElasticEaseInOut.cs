namespace Beutl.Animation.Easings;

public sealed class ElasticEaseInOut : Easing
{
    private const float HalfPI = MathF.PI / 2;

    public override bool TryGetOutputRange(out float minimum, out float maximum)
    {
        minimum = -0.5f;
        maximum = 1.5f;
        return true;
    }

    public override float Ease(float progress)
    {
        if (progress < 0.5f)
        {
            var t = 2f * progress;
            return 0.5f * MathF.Sin(13f * HalfPI * t) * MathF.Pow(2f, 10f * (t - 1f));
        }
        else
        {
            return 0.5f * (MathF.Sin(-13f * HalfPI * ((2f * progress - 1f) + 1f)) * MathF.Pow(2f, -10f * (2f * progress - 1f)) + 2f);
        }
    }
}
