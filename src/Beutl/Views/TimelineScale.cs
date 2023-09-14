using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

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

    private static readonly Typeface s_typeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.Medium);
    private readonly Pen _pen;
    private IBrush _brush = Brushes.White;
    private float _scale = 1;
    private Vector _offset;
    private Thickness _endingBarMargin;
    private Thickness _seekBarMargin;
    private IDisposable? _disposable;

    static TimelineScale()
    {
        AffectsRender<TimelineScale>(ScaleProperty, OffsetProperty, EndingBarMarginProperty, SeekBarMarginProperty);
    }

    public TimelineScale()
    {
        ClipToBounds = true;
        _pen = new Pen(_brush, 1);
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

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _disposable = this.GetResourceObservable("TextControlForeground").Subscribe(b =>
        {
            if (b is IBrush brush)
            {
                _brush = brush;
                _pen.Brush = brush;
                InvalidateVisual();
            }
        });
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        _disposable?.Dispose();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

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

                using var text = new TextLayout(time.ToString("hh\\:mm\\:ss\\.ff"), s_typeface, 13, _brush);
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

            var size = new Size(1.25, height);
            var seekbar = new Point(_seekBarMargin.Left, 0);
            var endingbar = new Point(_endingBarMargin.Left, 0);
            var bottom = new Point(0, height);

            context.DrawLine(TimelineSharedObject.RedPen, seekbar, seekbar + bottom);

            context.DrawLine(TimelineSharedObject.BluePen, endingbar, endingbar + bottom);
        }
    }
}
