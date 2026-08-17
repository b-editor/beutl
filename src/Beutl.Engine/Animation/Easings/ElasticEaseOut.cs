namespace Beutl.Animation.Easings;

public sealed class ElasticEaseOut : Easing
{
    public override bool TryGetOutputRange(out float minimum, out float maximum)
    {
        minimum = 0;
        maximum = 2;
        return true;
    }

    public override float Ease(float progress)
    {
        return Funcs.ElasticEaseOut(progress);
    }
}
