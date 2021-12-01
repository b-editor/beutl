namespace BEditorNext.Animation.Easings;

public sealed class ElasticEaseIn : Easing
{
    public override float Ease(float progress)
    {
        return Funcs.ElasticEaseIn(progress);
    }
}
