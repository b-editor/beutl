using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Beutl.Editor.Components.GraphEditorTab.Views;

public sealed class GraphEditorScale : Control
{
    public static readonly DirectProperty<GraphEditorScale, double> ScaleProperty
        = AvaloniaProperty.RegisterDirect<GraphEditorScale, double>(
            nameof(Scale),
            o => o.Scale, (o, v) => o.Scale = v,
            1d);

    public static readonly DirectProperty<GraphEditorScale, double> BaselineProperty
        = AvaloniaProperty.RegisterDirect<GraphEditorScale, double>(
            nameof(Baseline),
            o => o.Baseline, (o, v) => o.Baseline = v);

    public static readonly DirectProperty<GraphEditorScale, Vector> OffsetProperty
        = AvaloniaProperty.RegisterDirect<GraphEditorScale, Vector>(
            nameof(Offset), o => o.Offset, (o, v) => o.Offset = v);

    private static readonly Typeface s_typeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.Medium);
    private readonly Pen _pen;
    private IBrush _brush = Brushes.White;
    private double _scale = 1;
    private double _baseline;
    private Vector _offset;
    private IDisposable? _disposable;

    static GraphEditorScale()
    {
        AffectsRender<GraphEditorScale>(ScaleProperty, BaselineProperty, OffsetProperty);
    }

    public GraphEditorScale()
    {
        ClipToBounds = false;
        _pen = new Pen()
        {
            Brush = _brush
        };
    }

    public double Scale
    {
        get => _scale;
        set => SetAndRaise(ScaleProperty, ref _scale, value);
    }

    public double Baseline
    {
        get => _baseline;
        set => SetAndRaise(BaselineProperty, ref _baseline, value);
    }

    public Vector Offset
    {
        get => _offset;
        set => SetAndRaise(OffsetProperty, ref _offset, value);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _disposable = this.GetResourceObservable("GraphEditorScaleTextBrush").Subscribe(b =>
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
        const int left = 8;

        double width = Bounds.Width;
        double height = Bounds.Height;
        var viewport = new Rect(new Point(0, Offset.Y), new Size(width, height));

        double PixelsPer1 = 1 * Math.Clamp(_scale, 1, 1.75);
        double PixelsPer5 = PixelsPer1 * 5;
        double PixelsPer100 = PixelsPer5 * 20;
        double l = viewport.Height + viewport.Y;

        double originY = _baseline;

        using (context.PushClip(new Rect(0, 0, 64, Bounds.Height)))
        using (context.PushTransform(Matrix.CreateTranslation(0, -viewport.Y)))
        {
            context.FillRectangle(Brushes.Transparent, viewport);
            double value = PixelsPer100;

            for (double y = originY - PixelsPer100; y >= viewport.Y - PixelsPer100; y -= PixelsPer100)
            {
                if (viewport.Contains(new Point(width, y)))
                {
                    context.DrawLine(_pen, new(2, y), new(width, y));
                }

                double ll = y + PixelsPer100;
                for (double yy = y + PixelsPer5; yy < ll; yy += PixelsPer5)
                {
                    if (!viewport.Contains(new Point(width, yy))) continue;

                    if (viewport.Bottom < yy) return;

                    context.DrawLine(_pen, new(left, yy), new(width, yy));
                }

                var text = new TextLayout((value / _scale).ToString("F"), s_typeface, 13, _brush);
                text.Draw(context, new(0, y));

                value += PixelsPer100;
            }

            value = 0;

            for (double y = originY; y < l; y += PixelsPer100)
            {
                if (viewport.Contains(new Point(width, y)))
                {
                    context.DrawLine(_pen, new(2, y), new(width, y));
                }

                double ll = y + PixelsPer100;
                for (double yy = y + PixelsPer5; yy < ll; yy += PixelsPer5)
                {
                    if (!viewport.Contains(new Point(width, yy))) continue;

                    if (viewport.Bottom < yy) return;

                    context.DrawLine(_pen, new(left, yy), new(width, yy));
                }

                var text = new TextLayout((value / _scale).ToString("F"), s_typeface, 13, _brush);
                text.Draw(context, new(0, y));

                value -= PixelsPer100;
            }
        }
    }
}
