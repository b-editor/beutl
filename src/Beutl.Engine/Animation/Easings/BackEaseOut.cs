namespace Beutl.Animation.Easings;

public sealed class BackEaseOut : Easing
{
    public override bool TryGetOutputRange(out float minimum, out float maximum)
    {
        minimum = -0.000001f;
        maximum = 1.39f;
        return true;
    }

    protected override bool TryGetOutputRangeCore(
        float startProgress,
        float endProgress,
        out float minimum,
        out float maximum)
    {
        float start = Ease(startProgress);
        float end = Ease(endProgress);
        minimum = MathF.Min(start, end) - 0.000001f;
        maximum = MathF.Max(start, end) + 0.000001f;

        const float peakProgress = 0.4704272f;
        if (startProgress <= peakProgress && peakProgress <= endProgress)
            maximum = 1.39f;

        return true;
    }

    public override float Ease(float progress)
    {
        return Funcs.BackEaseOut(progress);
    }
}
