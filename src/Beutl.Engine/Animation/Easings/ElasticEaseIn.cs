namespace Beutl.Animation.Easings;

public sealed class ElasticEaseIn : Easing
{
    public override bool TryGetOutputRange(out float minimum, out float maximum)
    {
        minimum = -1;
        maximum = 1;
        return true;
    }

    public override float Ease(float progress)
    {
        return Funcs.ElasticEaseIn(progress);
    }
}
