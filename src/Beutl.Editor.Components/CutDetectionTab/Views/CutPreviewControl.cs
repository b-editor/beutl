using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Beutl.Editor.Services;
using TimeRange = Beutl.Media.TimeRange;

namespace Beutl.Editor.Components.CutDetectionTab.Views;

// A compact score strip: one bar per sampled frame, with detected cuts drawn over it.
public sealed class CutPreviewControl : Control
{
    private static readonly Color s_fallbackAccent = Color.Parse("#F5962D");
    private IReadOnlyList<CutFrameScore> _frames = [];
    private IReadOnlyList<MediaTime> _cuts = [];
    private TimeRange _clipRange;
    private double _threshold = 0.3;
    private MediaTime? _playhead;

    internal void SetData(IReadOnlyList<CutFrameScore> frames, TimeRange clipRange,
        IReadOnlyList<MediaTime> cuts, double threshold)
    {
        _frames = frames;
        _clipRange = clipRange;
        _cuts = cuts;
        _threshold = threshold;
        InvalidateVisual();
    }

    internal void SetPlayhead(MediaTime? time)
    {
        if (_playhead == time) return;
        _playhead = time;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width <= 0 || height <= 0) return;

        IBrush primary = ResolveBrush("TextFillColorPrimaryBrush", Colors.White);
        IBrush secondary = ResolveBrush("TextFillColorSecondaryBrush", Colors.Gray);
        IBrush divider = ResolveBrush("DividerStrokeColorDefaultBrush", Color.FromArgb(40, 255, 255, 255));
        var accent = new ImmutableSolidColorBrush(s_fallbackAccent);
        var accentSoft = new ImmutableSolidColorBrush(s_fallbackAccent, 0.62);
        Color secondaryColor = secondary is ISolidColorBrush solid ? solid.Color : Colors.Gray;
        var belowThreshold = new ImmutableSolidColorBrush(secondaryColor, 0.32);

        double plotBottom = height - 12;
        double plotHeight = Math.Max(1, plotBottom - 8);
        var gridPen = new Pen(divider, 0.5);
        for (int i = 1; i <= 3; i++)
        {
            double y = plotBottom - plotHeight * i / 4;
            context.DrawLine(gridPen, new Point(0, y), new Point(width, y));
        }

        context.DrawLine(new Pen(divider, 1), new Point(0, plotBottom), new Point(width, plotBottom));

        double duration = Math.Max(0.0001, _clipRange.Duration.ToSeconds());
        if (_frames.Count > 0)
        {
            // One bar per sample while they fit the width; a long clip keeps its strongest
            // sample in each pixel, so a late cut is never skipped.
            int columns = Math.Min(_frames.Count, Math.Max(1, (int)width));
            var scores = new double[columns];
            foreach (CutFrameScore frame in _frames)
            {
                double relative = (frame.Time - _clipRange.Start).ToSeconds() / duration;
                int column = (int)Math.Clamp(relative * (columns - 1), 0, columns - 1);
                scores[column] = Math.Max(scores[column], frame.Score);
            }

            double barWidth = width / columns;
            for (int i = 0; i < columns; i++)
            {
                double score = Math.Clamp(scores[i], 0, 1);
                if (score <= 0.001) continue;
                double barHeight = Math.Max(1, score * plotHeight);
                context.FillRectangle(score >= _threshold ? accentSoft : belowThreshold,
                    new Rect(i * barWidth, plotBottom - barHeight, Math.Max(1, barWidth + 0.5), barHeight));
            }

            double thresholdY = plotBottom - Math.Clamp(_threshold, 0, 1) * plotHeight;
            context.DrawLine(new Pen(new ImmutableSolidColorBrush(secondaryColor, 0.75), 1) { DashStyle = DashStyle.Dash },
                new Point(0, thresholdY), new Point(width, thresholdY));
        }

        var cutPen = new Pen(accent, 1.5);
        foreach (MediaTime cut in _cuts)
        {
            double relative = (cut - _clipRange.Start).ToSeconds() / duration;
            if (relative is < 0 or > 1) continue;
            double x = relative * width;
            context.DrawLine(cutPen, new Point(x, 0), new Point(x, plotBottom));
            var marker = new StreamGeometry();
            using (StreamGeometryContext markerContext = marker.Open())
            {
                markerContext.BeginFigure(new Point(x - 4, 0), true);
                markerContext.LineTo(new Point(x + 4, 0));
                markerContext.LineTo(new Point(x, 7));
                markerContext.EndFigure(true);
            }

            context.DrawGeometry(accent, null, marker);
        }

        if (_playhead is { } playhead)
        {
            double relative = (playhead - _clipRange.Start).ToSeconds() / duration;
            if (relative is >= 0 and <= 1)
            {
                double x = relative * width;
                context.DrawLine(new Pen(primary, 1), new Point(x, 0), new Point(x, height));
            }
        }
    }

    private IBrush ResolveBrush(string key, Color fallback)
        => this.TryFindResource(key, ActualThemeVariant, out object? value) && value is IBrush brush
            ? brush
            : new ImmutableSolidColorBrush(fallback);
}
