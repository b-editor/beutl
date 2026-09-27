namespace Beutl.Editor.Components.GraphEditorTab.Views;

internal static class GraphEditorGridMetrics
{
    public static double Step(double scale)
    {
        if (!(scale > 0) || !double.IsFinite(scale)) return double.NaN;
        double target = 72 / scale;
        if (!double.IsFinite(target)) return double.NaN;
        double unit = Math.Pow(10, Math.Floor(Math.Log10(target)));
        double fraction = target / unit;
        return (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10) * unit;
    }

    public static IEnumerable<(double Value, double Y)> MajorTicks(double scale, double baseline, double top, double bottom)
    {
        double step = Step(scale);
        double pixels = step * scale;
        if (!(pixels > 0) || !double.IsFinite(pixels) || !double.IsFinite(baseline)
            || !double.IsFinite(top) || !double.IsFinite(bottom) || bottom < top) yield break;
        double first = Math.Floor(((baseline - bottom) / scale) / step) * step;
        double last = (baseline - top) / scale;
        if (!double.IsFinite(first) || !double.IsFinite(last)) yield break;
        // Include the major tick below the viewport for its visible minor ticks.
        int count = (int)Math.Clamp(Math.Ceiling((bottom - top) / pixels) + 2, 0, 4096);
        double value = first;
        for (int i = 0; i < count && value <= last; i++)
        {
            double y = baseline - value * scale;
            if (double.IsFinite(y)) yield return (value, y);
            double next = value + step;
            if (!(next > value)) yield break;
            value = next;
        }
    }
}
