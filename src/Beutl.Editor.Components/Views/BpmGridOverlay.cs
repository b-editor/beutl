using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Beutl.Editor.Components.Helpers;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Components.Views;

public sealed class BpmGridOverlay : Control
{
    public static readonly DirectProperty<BpmGridOverlay, Vector> OffsetProperty
        = AvaloniaProperty.RegisterDirect<BpmGridOverlay, Vector>(
            nameof(Offset), o => o.Offset, (o, v) => o.Offset = v);

    public static readonly DirectProperty<BpmGridOverlay, Size> ViewportProperty
        = AvaloniaProperty.RegisterDirect<BpmGridOverlay, Size>(
            nameof(Viewport), o => o.Viewport, (o, v) => o.Viewport = v);

    public static readonly DirectProperty<BpmGridOverlay, float> ScaleProperty
        = AvaloniaProperty.RegisterDirect<BpmGridOverlay, float>(
            nameof(Scale), o => o.Scale, (o, v) => o.Scale = v);

    public static readonly DirectProperty<BpmGridOverlay, BpmGridOptions> BpmOptionsProperty
        = AvaloniaProperty.RegisterDirect<BpmGridOverlay, BpmGridOptions>(
            nameof(BpmOptions), o => o.BpmOptions, (o, v) => o.BpmOptions = v);

    public static readonly StyledProperty<IBrush?> BeatBrushProperty
        = AvaloniaProperty.Register<BpmGridOverlay, IBrush?>(nameof(BeatBrush));

    public static readonly StyledProperty<IBrush?> SubdivisionBrushProperty
        = AvaloniaProperty.Register<BpmGridOverlay, IBrush?>(nameof(SubdivisionBrush));

    private Vector _offset;
    private Size _viewport;
    private float _scale;
    private BpmGridOptions _bpmOptions;
    private ImmutablePen? _beatPen;
    private ImmutablePen? _subdivisionPen;

    static BpmGridOverlay()
    {
        AffectsRender<BpmGridOverlay>(
            OffsetProperty,
            ViewportProperty,
            ScaleProperty,
            BpmOptionsProperty,
            BeatBrushProperty,
            SubdivisionBrushProperty);
    }

    public BpmGridOverlay()
    {
        ClipToBounds = true;
        IsHitTestVisible = false;
    }

    public Vector Offset
    {
        get => _offset;
        set => SetAndRaise(OffsetProperty, ref _offset, value);
    }

    public Size Viewport
    {
        get => _viewport;
        set => SetAndRaise(ViewportProperty, ref _viewport, value);
    }

    public float Scale
    {
        get => _scale;
        set => SetAndRaise(ScaleProperty, ref _scale, value);
    }

    public BpmGridOptions BpmOptions
    {
        get => _bpmOptions;
        set => SetAndRaise(BpmOptionsProperty, ref _bpmOptions, value);
    }

    public IBrush? BeatBrush
    {
        get => GetValue(BeatBrushProperty);
        set => SetValue(BeatBrushProperty, value);
    }

    public IBrush? SubdivisionBrush
    {
        get => GetValue(SubdivisionBrushProperty);
        set => SetValue(SubdivisionBrushProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BeatBrushProperty)
        {
            _beatPen = CreatePen(BeatBrush, 1.0, 0.45);
        }
        else if (change.Property == SubdivisionBrushProperty)
        {
            _subdivisionPen = CreatePen(SubdivisionBrush, 0.5, 0.25);
        }
    }

    private static ImmutablePen? CreatePen(IBrush? brush, double thickness, double opacity)
    {
        if (brush is not ISolidColorBrush solidBrush)
            return brush != null ? new ImmutablePen(brush.ToImmutable(), thickness) : null;

        var color = solidBrush.Color;
        var fadedBrush = new ImmutableSolidColorBrush(color, opacity);
        return new ImmutablePen(fadedBrush, thickness);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (!_bpmOptions.IsEnabled || _bpmOptions.Bpm <= 0 || _scale <= 0)
            return;

        _beatPen ??= CreatePen(BeatBrush, 1.0, 0.45);
        _subdivisionPen ??= CreatePen(SubdivisionBrush, 0.5, 0.25);

        if (_beatPen == null)
            return;

        double secondWidth = FrameNumberHelper.SecondWidth;
        double beatIntervalSeconds = 60.0 / _bpmOptions.Bpm;
        double beatIntervalPixels = beatIntervalSeconds * secondWidth * _scale;

        if (beatIntervalPixels < 1)
            return;

        int subdivisions = Math.Max(1, _bpmOptions.Subdivisions);
        double subdivisionIntervalPixels = beatIntervalPixels / subdivisions;
        bool drawSubdivisions = subdivisions > 1 && subdivisionIntervalPixels >= 3 && _subdivisionPen != null;

        double offsetSeconds = _bpmOptions.Offset.TotalSeconds;
        double offsetPixels = offsetSeconds * secondWidth * _scale;

        double viewLeft = _offset.X;
        double viewRight = viewLeft + _viewport.Width;
        double height = _viewport.Height;

        int firstBeat = (int)Math.Floor((viewLeft - offsetPixels) / beatIntervalPixels);
        int lastBeat = (int)Math.Ceiling((viewRight - offsetPixels) / beatIntervalPixels);

        using (context.PushTransform(Matrix.CreateTranslation(_offset.X, _offset.Y)))
        {
            for (int i = firstBeat; i <= lastBeat; i++)
            {
                double beatX = offsetPixels + i * beatIntervalPixels - viewLeft;

                if (beatX >= -1 && beatX <= _viewport.Width + 1)
                {
                    var top = new Point(beatX, 0);
                    var bottom = new Point(beatX, height);
                    context.DrawLine(_beatPen, top, bottom);
                }

                if (drawSubdivisions)
                {
                    for (int s = 1; s < subdivisions; s++)
                    {
                        double subX = beatX + s * subdivisionIntervalPixels;
                        if (subX >= -1 && subX <= _viewport.Width + 1)
                        {
                            var top = new Point(subX, 0);
                            var bottom = new Point(subX, height);
                            context.DrawLine(_subdivisionPen!, top, bottom);
                        }
                    }
                }
            }
        }
    }
}
