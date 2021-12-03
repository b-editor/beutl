namespace BEditorNext.Animation.Easings;

public sealed class QuarticEaseInOut : Easing
{
    public override float Ease(float progress)
    {
        return Funcs.QuarticEaseInOut(progress);
    }
}
