namespace Beutl.Animation.Easings;

public sealed class ElasticEaseInOut : Easing
{
    public override bool TryGetOutputRange(out float minimum, out float maximum)
    {
        minimum = -0.5f;
        maximum = 1.5f;
        return true;
    }

    public override float Ease(float progress)
    {
        return Funcs.ElasticEaseInOut(progress);
    }
}
