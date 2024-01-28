using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;

using static Beutl.ViewModels.BufferStatusViewModel;

namespace Beutl.Views;

public sealed class TimelineScale : Control
{
    public static readonly DirectProperty<TimelineScale, float> ScaleProperty
        = AvaloniaProperty.RegisterDirect<TimelineScale, float>(
            nameof(Scale),
            o => o.Scale, (o, v) => o.Scale = v,
            1);

    public static readonly DirectProperty<TimelineScale, Vector> OffsetProperty
        = AvaloniaProperty.RegisterDirect<TimelineScale, Vector>(
            nameof(Offset), o => o.Offset, (o, v) => o.Offset = v);

    public static readonly DirectProperty<TimelineScale, Thickness> EndingBarMarginProperty
        = AvaloniaProperty.RegisterDirect<TimelineScale, Thickness>(
            nameof(EndingBarMargin), o => o.EndingBarMargin, (o, v) => o.EndingBarMargin = v);

    public static readonly DirectProperty<TimelineScale, Thickness> SeekBarMarginProperty
        = AvaloniaProperty.RegisterDirect<TimelineScale, Thickness>(
            nameof(SeekBarMargin), o => o.SeekBarMargin, (o, v) => o.SeekBarMargin = v);

    public static readonly StyledProperty<double> BufferStartProperty
        = AvaloniaProperty.Register<TimelineScale, double>(nameof(BufferStart));

    public static readonly StyledProperty<double> BufferEndProperty
        = AvaloniaProperty.Register<TimelineScale, double>(nameof(BufferEnd));

    public static readonly StyledProperty<CacheBlock[]?> CacheBlocksProperty
        = AvaloniaProperty.Register<TimelineScale, CacheBlock[]?>(nameof(CacheBlocks));

    public static readonly StyledProperty<CacheBlock?> HoveredCacheBlockProperty
        = AvaloniaProperty.Register<TimelineScale, CacheBlock?>(nameof(HoveredCacheBlock));

    public static readonly StyledProperty<IBrush?> ScaleBrushProperty
        = AvaloniaProperty.Register<TimelineScale, IBrush?>(nameof(ScaleBrush));

    public static readonly StyledProperty<IBrush?> SeekBarBrushProperty
        = AvaloniaProperty.Register<TimelineScale, IBrush?>(nameof(SeekBarBrush));

    public static readonly StyledProperty<IBrush?> EndingBarBrushProperty
        = AvaloniaProperty.Register<TimelineScale, IBrush?>(nameof(EndingBarBrush));

    public static readonly StyledProperty<IBrush?> CacheBlockBrushProperty
        = AvaloniaProperty.Register<TimelineScale, IBrush?>(nameof(CacheBlockBrush));

    public static readonly StyledProperty<IBrush?> LockedCacheBlockBrushProperty
        = AvaloniaProperty.Register<TimelineScale, IBrush?>(nameof(LockedCacheBlockBrush));

    public static readonly StyledProperty<IBrush?> BufferBrushProperty
        = AvaloniaProperty.Register<TimelineScale, IBrush?>(nameof(BufferBrush));

    private static readonly Typeface s_typeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.Medium);
    private float _scale = 1;
    private Vector _offset;
    private Thickness _endingBarMargin;
    private Thickness _seekBarMargin;
    private ImmutablePen? _pen;
    private ImmutablePen? _seekBarPen;
    private ImmutablePen? _endingBarPen;

    static TimelineScale()
    {
        AffectsRender<TimelineScale>(
            ScaleProperty,
            OffsetProperty,
            EndingBarMarginProperty,
            SeekBarMarginProperty,
            BufferStartProperty,
            BufferEndProperty,
            CacheBlocksProperty,
            HoveredCacheBlockProperty,
            ScaleBrushProperty,
            SeekBarBrushProperty,
            EndingBarBrushProperty,
            CacheBlockBrushProperty,
            LockedCacheBlockBrushProperty,
            BufferBrushProperty);
    }

    public TimelineScale()
    {
        ClipToBounds = true;
    }

    public float Scale
    {
        get => _scale;
        set => SetAndRaise(ScaleProperty, ref _scale, value);
    }

    public Vector Offset
    {
        get => _offset;
        set => SetAndRaise(OffsetProperty, ref _offset, value);
    }

    public Thickness EndingBarMargin
    {
        get => _endingBarMargin;
        set => SetAndRaise(EndingBarMarginProperty, ref _endingBarMargin, value);
    }

    public Thickness SeekBarMargin
    {
        get => _seekBarMargin;
        set => SetAndRaise(SeekBarMarginProperty, ref _seekBarMargin, value);
    }

    public double BufferStart
    {
        get => GetValue(BufferStartProperty);
        set => SetValue(BufferStartProperty, value);
    }

    public double BufferEnd
    {
        get => GetValue(BufferEndProperty);
        set => SetValue(BufferEndProperty, value);
    }

    public CacheBlock[]? CacheBlocks
    {
        get => GetValue(CacheBlocksProperty);
        set => SetValue(CacheBlocksProperty, value);
    }

    public CacheBlock? HoveredCacheBlock
    {
        get => GetValue(HoveredCacheBlockProperty);
        set => SetValue(HoveredCacheBlockProperty, value);
    }

    public IBrush? ScaleBrush
    {
        get => GetValue(ScaleBrushProperty);
        set => SetValue(ScaleBrushProperty, value);
    }

    public IBrush? SeekBarBrush
    {
        get => GetValue(SeekBarBrushProperty);
        set => SetValue(SeekBarBrushProperty, value);
    }

    public IBrush? EndingBarBrush
    {
        get => GetValue(EndingBarBrushProperty);
        set => SetValue(EndingBarBrushProperty, value);
    }

    public IBrush? CacheBlockBrush
    {
        get => GetValue(CacheBlockBrushProperty);
        set => SetValue(CacheBlockBrushProperty, value);
    }

    public IBrush? LockedCacheBlockBrush
    {
        get => GetValue(LockedCacheBlockBrushProperty);
        set => SetValue(LockedCacheBlockBrushProperty, value);
    }

    public IBrush? BufferBrush
    {
        get => GetValue(BufferBrushProperty);
        set => SetValue(BufferBrushProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ScaleBrushProperty)
        {
            _pen = new ImmutablePen(ScaleBrush?.ToImmutable(), 1);
        }
        else if (change.Property == SeekBarBrushProperty)
        {
            _seekBarPen = new ImmutablePen(SeekBarBrush?.ToImmutable(), 1.25);
        }
        else if (change.Property == EndingBarBrushProperty)
        {
            _endingBarPen = new ImmutablePen(EndingBarBrush?.ToImmutable(), 1.25);
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        _pen ??= new ImmutablePen(ScaleBrush?.ToImmutable(), 1);
        _seekBarPen ??= new ImmutablePen(SeekBarBrush?.ToImmutable(), 1.25);
        _endingBarPen ??= new ImmutablePen(EndingBarBrush?.ToImmutable(), 1.25);

        const int top = 16;

        double width = Bounds.Width;
        double height = Bounds.Height;
        var viewport = new Rect(new Point(Offset.X, 0), new Size(width, height));

        double recentPix = 0d;
        double inc = FrameNumberHelper.SecondWidth;
        // 分割数: 30
        double wf = FrameNumberHelper.SecondWidth / 30;
        double l = viewport.Width + viewport.X;

        double originX = Math.Floor(viewport.X / inc) * inc;
        using (context.PushTransform(Matrix.CreateTranslation(-viewport.X, 0)))
        {
            context.FillRectangle(Brushes.Transparent, viewport);
            for (double x = originX; x < l; x += inc)
            {
                var time = x.ToTimeSpan(Scale);

                if (viewport.Contains(new Point(x, height)))
                {
                    context.DrawLine(_pen, new(x, 5), new(x, height));
                }

                using var text = new TextLayout(time.ToString("hh\\:mm\\:ss\\.ff"), s_typeface, 13, ScaleBrush);
                var textbounds = new Rect(x + 8, 0, text.Width, text.Height);

                if (viewport.Intersects(textbounds) && (recentPix == 0d || (x + 8) > recentPix))
                {
                    recentPix = textbounds.Right;
                    text.Draw(context, new(x + 8, 0));
                }

                double ll = x + inc;
                for (double xx = x + wf; xx < ll; xx += wf)
                {
                    if (!viewport.Contains(new Point(xx, height))) continue;

                    if (viewport.Right < xx) return;

                    context.DrawLine(_pen, new(xx, top), new(xx, height));
                }
            }

            if (BufferEnd != BufferStart)
            {
                context.DrawRectangle(
                    BufferBrush, null,
                    new RoundedRect(new Rect(BufferStart, Height - 4, BufferEnd - BufferStart, 4)));
            }

            if (CacheBlocks != null)
            {
                TimeSpan left = originX.ToTimeSpan(Scale);
                TimeSpan right = l.ToTimeSpan(Scale);

                foreach (CacheBlock item in CacheBlocks)
                {
                    TimeSpan end = item.Start + item.Length;
                    if (end < left || item.Start > right)
                    {
                        continue;
                    }

                    context.DrawRectangle(
                        item.IsLocked ? LockedCacheBlockBrush : CacheBlockBrush, null,
                        new RoundedRect(new Rect(item.Start.ToPixel(Scale), Height - 4, item.Length.ToPixel(Scale), 4)));
                }
            }

            if (HoveredCacheBlock is { } hover)
            {
                context.DrawRectangle(
                    hover.IsLocked ? LockedCacheBlockBrush : CacheBlockBrush, null,
                    new RoundedRect(new Rect(hover.Start.ToPixel(Scale), Height - 6, hover.Length.ToPixel(Scale), 6)));
            }

            var size = new Size(1.25, height);
            var seekbar = new Point(_seekBarMargin.Left, 0);
            var endingbar = new Point(_endingBarMargin.Left, 0);
            var bottom = new Point(0, height);

            context.DrawLine(_seekBarPen, seekbar, seekbar + bottom);

            context.DrawLine(_endingBarPen, endingbar, endingbar + bottom);
        }
    }
}
