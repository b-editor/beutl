namespace Beutl.Animation.Easings;

public sealed class ElasticEaseOut : Easing
{
    private const float HalfPI = MathF.PI / 2;

    public override bool TryGetOutputRange(out float minimum, out float maximum)
    {
        minimum = 0;
        maximum = 2;
        return true;
    }

    public override float Ease(float progress)
    {
        return MathF.Sin(-13f * HalfPI * (progress + 1)) * MathF.Pow(2f, -10f * progress) + 1f;
    }
}
