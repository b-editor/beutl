namespace BEditorNext.Animation.Easings;

public sealed class QuadraticEaseInOut : Easing
{
    public override float Ease(float progress)
    {
        return Funcs.QuadraticEaseInOut(progress);
    }
}
