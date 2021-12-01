namespace BEditorNext.Animation.Easings;

public sealed class QuadraticEaseIn : Easing
{
    public override float Ease(float progress)
    {
        return Funcs.QuadraticEaseIn(progress);
    }
}
