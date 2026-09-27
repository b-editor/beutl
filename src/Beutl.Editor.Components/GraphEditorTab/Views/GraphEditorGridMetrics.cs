namespace Beutl.Editor.Components.GraphEditorTab.Views;

internal static class GraphEditorGridMetrics
{
    public static double Step(double scale)
    {
        double target = 72 / Math.Max(0.000001, scale);
        double unit = Math.Pow(10, Math.Floor(Math.Log10(target)));
        double fraction = target / unit;
        return (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10) * unit;
    }
}
