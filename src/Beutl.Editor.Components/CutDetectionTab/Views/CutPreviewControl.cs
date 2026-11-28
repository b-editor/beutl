using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Beutl.Editor.Services;
using TimeRange = Beutl.Media.TimeRange;

namespace Beutl.Editor.Components.CutDetectionTab.Views;

// The silence tab's waveform, with per-frame difference scores in place of audio peaks.
public sealed class CutPreviewControl : Control
{
    private static readonly Color s_cutColor = Color.Parse("#F5962D");
    private static readonly ImmutableSolidColorBrush s_highlightBrush =
        new(Color.FromArgb(90, 245, 150, 45));
    private static readonly ImmutableSolidColorBrush s_cutBrush = new(s_cutColor);
    private IReadOnlyList<CutFrameScore> _frames = [];
    private IReadOnlyList<MediaTime> _cuts = [];
    private TimeRange _clipRange;
    private TimeRange _previewRange;
    private double _threshold = 0.3;

    public static readonly StyledProperty<IBrush?> ScoreBrushProperty =
        AvaloniaProperty.Register<CutPreviewControl, IBrush?>(nameof(ScoreBrush), Brushes.White);

    static CutPreviewControl()
    {
        AffectsRender<CutPreviewControl>(ScoreBrushProperty);
    }

    public IBrush? ScoreBrush
    {
        get => GetValue(ScoreBrushProperty);
        set => SetValue(ScoreBrushProperty, value);
    }

    internal IReadOnlyList<MediaTime> Cuts => _cuts;
    internal double Threshold => _threshold;

    internal void SetData(IReadOnlyList<CutFrameScore> frames, TimeRange clipRange, TimeRange previewRange,
        IReadOnlyList<MediaTime> cuts, double threshold)
    {
        _frames = frames;
        _clipRange = clipRange;
        _previewRange = previewRange;
        _cuts = cuts;
        _threshold = threshold;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width <= 0 || height <= 0) return;

        double duration = Math.Max(0.0001, _previewRange.Duration.ToSeconds());
        double start = _clipRange.Start.ToSeconds() + _previewRange.Start.ToSeconds();
        int visible = 0;
        foreach (CutFrameScore frame in _frames)
        {
            double relative = (frame.Time.ToSeconds() - start) / duration;
            if (relative is >= 0 and <= 1) visible++;
        }

        if (ScoreBrush is { } scoreBrush && visible > 0)
        {
            // One bar per sample while they fit the width; a zoomed-out view keeps the strong
            // sample in each pixel instead of skipping late cuts.
            int columns = Math.Min(visible, Math.Max(1, (int)width));
            var scores = new double[columns];
            foreach (CutFrameScore frame in _frames)
            {
                double relative = (frame.Time.ToSeconds() - start) / duration;
                if (relative is < 0 or > 1) continue;
                // The same mapping as the cut lines, so a cut sits on its own score bar.
                int column = (int)Math.Clamp(relative * columns, 0, columns - 1);
                scores[column] = Math.Max(scores[column], frame.Score);
            }

            double barWidth = width / columns;
            var geometry = new StreamGeometry();
            using (StreamGeometryContext figures = geometry.Open())
            {
                figures.SetFillRule(FillRule.NonZero);
                for (int i = 0; i < columns; i++)
                {
                    double score = Math.Clamp(scores[i], 0, 1);
                    if (score <= 0.001) continue;
                    double top = height - Math.Max(1, score * height);
                    double left = i * barWidth;
                    figures.BeginFigure(new Point(left, top), isFilled: true);
                    figures.LineTo(new Point(left + Math.Max(1, barWidth - 0.5), top));
                    figures.LineTo(new Point(left + Math.Max(1, barWidth - 0.5), height));
                    figures.LineTo(new Point(left, height));
                    figures.EndFigure(isClosed: true);
                }
            }

            context.DrawGeometry(scoreBrush, null, geometry);
        }

        double thresholdY = height - Math.Clamp(_threshold, 0, 1) * height;
        IBrush thresholdBrush = this.TryFindResource("TextFillColorSecondaryBrush", ActualThemeVariant, out object? value)
            && value is ISolidColorBrush secondary
                ? new ImmutableSolidColorBrush(secondary.Color, 0.5)
                : new ImmutableSolidColorBrush(Colors.Gray, 0.5);
        context.DrawLine(new Pen(thresholdBrush, 1) { DashStyle = DashStyle.Dash },
            new Point(0, thresholdY), new Point(width, thresholdY));

        foreach (MediaTime cut in _cuts)
        {
            double relative = (cut.ToSeconds() - start) / duration;
            if (relative is < 0 or > 1) continue;
            double x = relative * width;
            // The silence tab's region highlight, narrowed to a band around the cut.
            context.FillRectangle(s_highlightBrush, new Rect(x - 6, 0, 12, height));
            context.DrawLine(new Pen(s_cutBrush, 1.5), new Point(x, 0), new Point(x, height));
            var marker = new StreamGeometry();
            using (StreamGeometryContext markerContext = marker.Open())
            {
                markerContext.BeginFigure(new Point(x - 4, 0), true);
                markerContext.LineTo(new Point(x + 4, 0));
                markerContext.LineTo(new Point(x, 7));
                markerContext.EndFigure(true);
            }

            context.DrawGeometry(s_cutBrush, null, marker);
        }
    }
}
