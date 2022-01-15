namespace BeUtl.Animation.Easings;

public sealed class ElasticEaseInOut : Easing
{
    public override float Ease(float progress)
    {
        return Funcs.ElasticEaseInOut(progress);
    }
}
