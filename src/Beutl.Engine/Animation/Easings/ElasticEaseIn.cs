namespace Beutl.Animation.Easings;

public sealed class ElasticEaseIn : Easing
{
    private const float HalfPI = MathF.PI / 2;

    public override bool TryGetOutputRange(out float minimum, out float maximum)
    {
        minimum = -1;
        maximum = 1;
        return true;
    }

    public override float Ease(float progress)
    {
        return MathF.Sin(13f * HalfPI * progress) * MathF.Pow(2f, 10f * (progress - 1));
    }
}
