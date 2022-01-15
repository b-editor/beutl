namespace BeUtl.Animation.Easings;

public sealed class ElasticEaseOut : Easing
{
    public override float Ease(float progress)
    {
        return Funcs.ElasticEaseOut(progress);
    }
}
