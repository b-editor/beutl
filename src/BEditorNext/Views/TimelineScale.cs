using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;

namespace BEditorNext.Views;

public sealed class TimelineScale : Control
{
    public static readonly StyledProperty<float> ScaleProperty = AvaloniaProperty.Register<TimelineScale, float>(nameof(Scale), 1);
    private static readonly Typeface s_typeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.Medium);
    private readonly Pen _pen = new()
    {
        Brush = (IBrush)Application.Current.FindResource("TextControlForeground")!
    };
    private readonly FormattedText _text = new()
    {
        Typeface = s_typeface,
        FontSize = 13
    };

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
        ScrollViewer scroll = this.FindLogicalAncestorOfType<ScrollViewer>();
        var viewport = new Rect(new Point(scroll.Offset.X, scroll.Offset.Y), scroll.Viewport);

        double recentPix = 0d;
        double inc = Helper.SecondPixels;
        // 分割数: 30
        double wf = Helper.SecondPixels / 30;
        double l = viewport.Width + viewport.X;

        for (double x = Math.Floor(viewport.X / inc) * inc; x < l; x += inc)
        {
            var time = x.ToTimeSpan(Scale);

            if (viewport.Contains(new Point(x, Bounds.Height)))
            {
                context.DrawLine(_pen, new(x, 5), new(x, height));
            }

            _text.Text = time.ToString("hh\\:mm\\:ss\\.ff");
            Rect textbounds = _text.Bounds.WithX(x + 8);

            if (viewport.Intersects(textbounds) && (recentPix == 0d || (x + 8) > recentPix))
            {
                recentPix = textbounds.Right;
                context.DrawText(_pen.Brush, new(x + 8, 0), _text);
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
