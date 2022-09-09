using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace BeUtl.Views;

public sealed class TimelineScale : Control
{
    public static readonly StyledProperty<float> ScaleProperty = AvaloniaProperty.Register<TimelineScale, float>(nameof(Scale), 1);
    private static readonly Typeface s_typeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.Medium);
    private readonly IBrush _brush;
    private readonly Pen _pen;

    public TimelineScale()
    {
        _brush = (IBrush)Application.Current?.FindResource("TextControlForeground")!;
        _pen = new()
        {
            Brush = _brush
        };
    }

    public float Scale
    {
        get => GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        const int top = 16;

        double width = Bounds.Width;
        double height = Bounds.Height;
        ScrollViewer scroll = this.FindLogicalAncestorOfType<ScrollViewer>()!;
        var viewport = new Rect(new Point(scroll.Offset.X, scroll.Offset.Y), scroll.Viewport);

        double recentPix = 0d;
        double inc = Helper.SecondWidth;
        // 分割数: 30
        double wf = Helper.SecondWidth / 30;
        double l = viewport.Width + viewport.X;

        for (double x = Math.Floor(viewport.X / inc) * inc; x < l; x += inc)
        {
            var time = x.ToTimeSpan(Scale);

            if (viewport.Contains(new Point(x, Bounds.Height)))
            {
                context.DrawLine(_pen, new(x, 5), new(x, height));
            }

            var text = new TextLayout(time.ToString("hh\\:mm\\:ss\\.ff"), s_typeface, 13, _brush);
            Rect textbounds = text.Bounds.WithX(x + 8);

            if (viewport.Intersects(textbounds) && (recentPix == 0d || (x + 8) > recentPix))
            {
                recentPix = textbounds.Right;
                text.Draw(context, new(x + 8, 0));
            }

            double ll = x + inc;
            for (double xx = x + wf; xx < ll; xx += wf)
            {
                if (!viewport.Contains(new Point(xx, Bounds.Height))) continue;

                if (width < xx) return;

                context.DrawLine(_pen, new(xx, top), new(xx, height));
            }
        }
    }
}
