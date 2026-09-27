using Avalonia;
using Beutl.Animation.Easings;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.Helpers;

namespace Beutl.Editor.Components.GraphEditorTab.Views;

internal static class GraphEditorTangentCoupling
{
    // Vectors are in seconds/value units. Length is measured in the current graph's view:
    // both axes in the value graph, horizontal influence in the speed graph.
    public static Point Opposite(GraphEditorViewModel editor, Point driver, Point originalOpposite, bool independently = false)
    {
        if (independently || editor.Separately.Value) return originalOpposite;
        if (editor.Symmetry.Value) return driver * -1;
        double length = Length(editor, driver);
        return length > 1e-9 ? driver * (-Length(editor, originalOpposite) / length) : originalOpposite;
    }

    public static GraphEditorKeyFrameViewModel? OppositeSegment(GraphEditorKeyFrameViewModel segment, bool incoming)
    {
        var keys = segment.Parent.KeyFrames;
        int index = keys.IndexOf(segment);
        return incoming ? keys.ElementAtOrDefault(index + 1) : index > 1 ? keys[index - 1] : null;
    }

    public static GraphEditorKeyFrameViewModel? CrossedSegment(GraphEditorKeyFrameViewModel segment, bool incoming, double offsetX)
    {
        if (!(incoming ? offsetX > 0 : offsetX < 0)) return null;
        var opposite = OppositeSegment(segment, incoming);
        return opposite is { Model.Easing: SplineEasing } && opposite.Width.Value > 0 ? opposite : null;
    }

    public static Point Vector(GraphEditorKeyFrameViewModel segment, bool incoming)
    {
        var keys = segment.Parent.KeyFrames;
        int index = keys.IndexOf(segment);
        if (index <= 0) return default;
        var previous = keys[index - 1].Model;
        var spline = GraphEditorCurveMath.ToSpline(segment.Model.Easing);
        double duration = (segment.Model.KeyTime - previous.KeyTime).TotalSeconds;
        double difference = segment.Parent.ConvertToDouble(segment.Model.Value) - segment.Parent.ConvertToDouble(previous.Value);
        return incoming ? new Point((spline.X2 - 1) * duration, (spline.Y2 - 1) * difference)
            : new Point(spline.X1 * duration, spline.Y1 * difference);
    }

    public static Point ToPixels(GraphEditorViewModel editor, Point vector) => new(
        vector.X * TimeSpan.FromSeconds(1).TimeToPixel(editor.Options.Value.Scale), -vector.Y * editor.ScaleY.Value);

    private static double Length(GraphEditorViewModel editor, Point vector)
    {
        double x = vector.X * TimeSpan.FromSeconds(1).TimeToPixel(editor.Options.Value.Scale);
        double y = editor.IsSpeedGraph.Value ? 0 : vector.Y * editor.ScaleY.Value;
        return Math.Sqrt(x * x + y * y);
    }
}
