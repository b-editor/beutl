namespace BEditorNext.Animation.Easings;

public sealed class CircularEaseIn : Easing
{
    public override float Ease(float progress)
    {
        return Funcs.CircularEaseIn(progress);
    }
}
