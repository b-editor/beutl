using Beutl.Animation.Easings;

namespace Beutl.Editor.Components.GraphEditorTab.ViewModels;

internal static class GraphEditorCurveMath
{
    internal static double KeyVelocity(GraphEditorKeyFrameViewModel item, bool outgoing = false)
    {
        var channel = item.Parent;
        int index = channel.KeyFrames.IndexOf(item);
        if (outgoing || index == 0)
        {
            if (index + 1 >= channel.KeyFrames.Count) return 0;
            var next = channel.KeyFrames[index + 1].Model;
            return GraphEditorCurveMath.Velocity(next.Easing, 0,
                channel.ConvertToDouble(next.Value) - channel.ConvertToDouble(item.Model.Value),
                (next.KeyTime - item.Model.KeyTime).TotalSeconds);
        }
        var previous = channel.KeyFrames[index - 1].Model;
        return GraphEditorCurveMath.Velocity(item.Model.Easing, 1,
            channel.ConvertToDouble(item.Model.Value) - channel.ConvertToDouble(previous.Value),
            (item.Model.KeyTime - previous.KeyTime).TotalSeconds);
    }

    // Value graph derivatives are signed (units/second), including decreasing scalar channels.
    public static double Velocity(Easing easing, double progress, double difference, double duration)
    {
        if (duration <= 0 || difference == 0 || easing is HoldEasing) return 0;
        double slope;
        if (easing is LinearEasing) slope = 1;
        else if (easing is SplineEasing spline)
        {
            if (progress <= 0 && spline.X1 > 0)
                return difference / duration * spline.Y1 / spline.X1;
            if (progress >= 1 && spline.X2 < 1)
                return difference / duration * (1 - spline.Y2) / (1 - spline.X2);
            double lo = 0, hi = 1;
            for (int i = 0; i < 28; i++)
            {
                double mid = (lo + hi) / 2;
                if (Cubic(mid, spline.X1, spline.X2) < progress) lo = mid;
                else hi = mid;
            }
            double t = Math.Clamp((lo + hi) / 2, 0.000001, 0.999999);
            double dx = Derivative(t, spline.X1, spline.X2);
            slope = Derivative(t, spline.Y1, spline.Y2) / dx;
        }
        else
        {
            const double h = 0.0001;
            double a = Math.Max(0, progress - h), b = Math.Min(1, progress + h);
            slope = (easing.Ease((float)b) - easing.Ease((float)a)) / (b - a);
        }
        double result = slope * difference / duration;
        return double.IsFinite(result) ? result : 0;
    }

    private static double Cubic(double t, double a, double b) =>
        3 * (1 - t) * (1 - t) * t * a + 3 * (1 - t) * t * t * b + t * t * t;

    private static double Derivative(double t, double a, double b) =>
        3 * ((1 - t) * (1 - t) * a + 2 * (1 - t) * t * (b - a) + t * t * (1 - b));

    public static SplineEasing ToSpline(Easing easing)
    {
        if (easing is SplineEasing spline) return spline;
        // Match endpoint velocities when converting another easing to editable Bezier handles.
        return new SplineEasing(1f / 3, (float)(Velocity(easing, 0, 1, 1) / 3),
            2f / 3, (float)(1 - Velocity(easing, 1, 1, 1) / 3));
    }
}
