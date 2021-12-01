namespace BEditorNext.Animation.Easings;

public sealed class BackEaseIn : Easing
{
    public override float Ease(float progress)
    {
        return Funcs.BackEaseIn(progress);
    }
}
