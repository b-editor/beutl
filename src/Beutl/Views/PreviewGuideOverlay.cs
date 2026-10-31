using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;

using Beutl.Configuration;
using Beutl.Editor.Components.PreviewSettingsTab.ViewModels;

namespace Beutl.Views;

// Safe areas and composition guides over the preview. They are a layer of the player view alone, so they never reach
// a rendered frame, the presentation window or an export. The overlay draws again only when the settings, its size
// or the zoom change: a new preview frame leaves it alone, so the guides cost playback nothing.
internal sealed class PreviewGuideOverlay : Control
{
    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<PreviewGuideOverlay, double>(nameof(Zoom), 1.0);

    private const double LineThickness = 1.0;
    private const double HaloThickness = 3.0;
    private const double HaloOpacity = 0.6;
    private const double LabelSize = 11.0;
    private const double LabelPadding = 4.0;
    private const double LabelChipMargin = 3.0;

    // In multiples of the line's thickness.
    private static readonly ImmutableDashStyle s_aspectDash = new([4, 3], 0);
    private static readonly Typeface s_labelTypeface = new(FontFamily.Default);

    private EditorConfig? _config;
    private PreviewGuideKinds _kinds;
    private Color _color = Colors.White;
    private double _opacity;
    private int _columns = 1;
    private int _rows = 1;
    private PreviewGuideAspectRatio[] _aspectRatios = [];

    static PreviewGuideOverlay()
    {
        AffectsRender<PreviewGuideOverlay>(ZoomProperty);
    }

    public PreviewGuideOverlay()
    {
        IsHitTestVisible = false;
        IsVisible = false;
    }

    // The preview's zoom, which the lines divide out to stay one pixel wide on screen.
    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    // The guides drawn now: none while the guides are turned off.
    internal PreviewGuideKinds Kinds => _kinds;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _config = GlobalConfiguration.Instance.EditorConfig;
        _config.PropertyChanged += OnConfigPropertyChanged;
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_config is { } config)
        {
            config.PropertyChanged -= OnConfigPropertyChanged;
        }
        _config = null;
    }

    private void OnConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorConfig.IsPreviewGuideEnabled)
            or nameof(EditorConfig.PreviewGuideKinds)
            or nameof(EditorConfig.PreviewGuideColor)
            or nameof(EditorConfig.PreviewGuideOpacity)
            or nameof(EditorConfig.PreviewGuideGridColumns)
            or nameof(EditorConfig.PreviewGuideGridRows)
            or nameof(EditorConfig.PreviewGuideAspectRatios))
        {
            if (Dispatcher.UIThread.CheckAccess())
                Refresh();
            else
                Dispatcher.UIThread.Post(Refresh);
        }
    }

    private void Refresh()
    {
        if (_config is not { } config) return;

        _kinds = config.IsPreviewGuideEnabled.Value ? config.PreviewGuideKinds.Value : PreviewGuideKinds.None;
        _color = PreviewGuideSettingsViewModel.ParseColor(config.PreviewGuideColor.Value);
        _opacity = Math.Clamp(config.PreviewGuideOpacity.Value, 0, 1);
        _columns = Math.Clamp(config.PreviewGuideGridColumns.Value, 1, EditorConfig.PreviewGuideGridMaxCells);
        _rows = Math.Clamp(config.PreviewGuideGridRows.Value, 1, EditorConfig.PreviewGuideGridMaxCells);
        // A hand-edited list keeps the ratios it can read.
        PreviewGuideAspectRatio.TryParseList(config.PreviewGuideAspectRatios.Value, out _aspectRatios);

        IsVisible = _kinds != PreviewGuideKinds.None && _opacity > 0;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Size size = Bounds.Size;
        if (_kinds == PreviewGuideKinds.None || !(size.Width > 0) || !(size.Height > 0)) return;

        var layout = PreviewGuideLayout.Create(size, _kinds, _columns, _rows, _aspectRatios);
        double zoom = Zoom;
        double unit = zoom > 0 && double.IsFinite(zoom) ? 1 / zoom : 1;

        var solid = new StreamGeometry();
        using (StreamGeometryContext ctx = solid.Open())
        {
            foreach (Rect area in layout.Areas)
            {
                ctx.BeginFigure(area.TopLeft, false);
                ctx.LineTo(area.TopRight);
                ctx.LineTo(area.BottomRight);
                ctx.LineTo(area.BottomLeft);
                ctx.EndFigure(true);
            }

            foreach (GuideLine line in layout.Lines)
            {
                AddLine(ctx, line);
            }
        }

        // A dark halo under every line keeps the guides visible over bright highlights, HDR ones tone-mapped for the
        // preview included, as much as over dark frames.
        var haloBrush = new ImmutableSolidColorBrush(Colors.Black, HaloOpacity * _opacity);
        var brush = new ImmutableSolidColorBrush(_color, _opacity);

        // Only the sides inside the frame: the others lie on its edges.
        var dashed = new StreamGeometry();
        var labels = new List<(Point Origin, FormattedText Text)>();
        int stacked = 0;
        using (StreamGeometryContext ctx = dashed.Open())
        {
            foreach ((PreviewGuideAspectRatio ratio, Rect frame) in layout.AspectFrames)
            {
                bool pillarbox = frame.Width < size.Width;
                if (pillarbox)
                {
                    AddLine(ctx, new GuideLine(frame.TopLeft, frame.BottomLeft));
                    AddLine(ctx, new GuideLine(frame.TopRight, frame.BottomRight));
                }
                else
                {
                    AddLine(ctx, new GuideLine(frame.TopLeft, frame.TopRight));
                    AddLine(ctx, new GuideLine(frame.BottomLeft, frame.BottomRight));
                }

                var text = new FormattedText(
                    ratio.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, s_labelTypeface,
                    LabelSize * unit, brush);
                // Pillarboxes share the top edge, so their labels step down to keep clear of each other.
                double offset = pillarbox ? stacked++ * (text.Height + LabelPadding * unit) : 0;
                labels.Add((new Point(frame.Left + LabelPadding * unit, frame.Top + LabelPadding * unit + offset), text));
            }
        }

        var halo = new ImmutablePen(haloBrush, HaloThickness * unit);
        context.DrawGeometry(null, halo, solid);
        context.DrawGeometry(null, halo, dashed);
        context.DrawGeometry(null, new ImmutablePen(brush, LineThickness * unit), solid);
        context.DrawGeometry(null, new ImmutablePen(brush, LineThickness * unit, s_aspectDash), dashed);

        // Labels go on top, each on a chip of the halo's dark, so lines crossing them leave them legible.
        foreach ((Point origin, FormattedText text) in labels)
        {
            double margin = LabelChipMargin * unit;
            var chip = new Rect(origin.X - margin, origin.Y, text.Width + margin * 2, text.Height);
            context.DrawRectangle(haloBrush, null, chip, margin, margin);
            context.DrawText(text, origin);
        }
    }

    private static void AddLine(StreamGeometryContext ctx, GuideLine line)
    {
        ctx.BeginFigure(line.Start, false);
        ctx.LineTo(line.End);
        ctx.EndFigure(false);
    }
}

internal readonly record struct GuideLine(Point Start, Point End);

// The guides in the overlay's coordinates, where the frame fills (0, 0) to size.
internal sealed class PreviewGuideLayout
{
    // The fractions of the frame the safe areas take, centered.
    internal const double TitleSafeScale = 0.9;
    internal const double ActionSafeScale = 0.95;

    // The cross's arms, as a fraction of the frame's shorter side.
    private const double CenterCrossArm = 0.05;

    // A ratio this close to the frame's own would outline the frame itself.
    private const double SameAspectTolerance = 1e-3;

    public List<Rect> Areas { get; } = [];

    public List<GuideLine> Lines { get; } = [];

    // Each the largest frame of its ratio, centered.
    public List<(PreviewGuideAspectRatio Ratio, Rect Frame)> AspectFrames { get; } = [];

    public static PreviewGuideLayout Create(
        Size size, PreviewGuideKinds kinds, int columns, int rows, IReadOnlyList<PreviewGuideAspectRatio> aspectRatios)
    {
        var layout = new PreviewGuideLayout();
        double width = size.Width;
        double height = size.Height;
        var bounds = new Rect(size);

        if (kinds.HasFlag(PreviewGuideKinds.ActionSafe))
            layout.Areas.Add(Centered(bounds, ActionSafeScale));
        if (kinds.HasFlag(PreviewGuideKinds.TitleSafe))
            layout.Areas.Add(Centered(bounds, TitleSafeScale));

        if (kinds.HasFlag(PreviewGuideKinds.RuleOfThirds))
            layout.AddGrid(size, 3, 3);
        if (kinds.HasFlag(PreviewGuideKinds.CustomGrid))
            layout.AddGrid(size, columns, rows);

        if (kinds.HasFlag(PreviewGuideKinds.Diagonals))
        {
            layout.Lines.Add(new GuideLine(bounds.TopLeft, bounds.BottomRight));
            layout.Lines.Add(new GuideLine(bounds.TopRight, bounds.BottomLeft));
        }

        if (kinds.HasFlag(PreviewGuideKinds.CenterCross))
        {
            Point center = bounds.Center;
            double arm = Math.Min(width, height) * CenterCrossArm;
            layout.Lines.Add(new GuideLine(new Point(center.X - arm, center.Y), new Point(center.X + arm, center.Y)));
            layout.Lines.Add(new GuideLine(new Point(center.X, center.Y - arm), new Point(center.X, center.Y + arm)));
        }

        if (kinds.HasFlag(PreviewGuideKinds.AspectRatio))
        {
            double frameAspect = width / height;
            foreach (PreviewGuideAspectRatio ratio in aspectRatios)
            {
                double value = ratio.Value;
                if (Math.Abs(value / frameAspect - 1) < SameAspectTolerance) continue;

                Size frame = value < frameAspect ? new Size(height * value, height) : new Size(width, width / value);
                layout.AspectFrames.Add((ratio, new Rect(
                    (width - frame.Width) / 2, (height - frame.Height) / 2, frame.Width, frame.Height)));
            }
        }

        return layout;
    }

    private void AddGrid(Size size, int columns, int rows)
    {
        for (int i = 1; i < columns; i++)
        {
            double x = size.Width * i / columns;
            Lines.Add(new GuideLine(new Point(x, 0), new Point(x, size.Height)));
        }

        for (int i = 1; i < rows; i++)
        {
            double y = size.Height * i / rows;
            Lines.Add(new GuideLine(new Point(0, y), new Point(size.Width, y)));
        }
    }

    private static Rect Centered(Rect bounds, double scale)
    {
        double width = bounds.Width * scale;
        double height = bounds.Height * scale;
        return new Rect((bounds.Width - width) / 2, (bounds.Height - height) / 2, width, height);
    }
}
