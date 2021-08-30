using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;

using BEditor.Data;
using BEditor.Extensions;
using BEditor.Models;

namespace BEditor.Views.Timelines
{
    public sealed class TimelineScale : Control
    {
        public static readonly StyledProperty<float> ScaleProperty = AvaloniaProperty.Register<TimelineScale, float>(nameof(Scale), 0.75f);
        public static readonly StyledProperty<int> RateProperty = AvaloniaProperty.Register<TimelineScale, int>(nameof(Rate), 30);
        private static readonly Typeface _typeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.Medium);
        private readonly Pen _pen = new()
        {
            Brush = (IBrush)Application.Current.FindResource("TextControlForeground")!
        };
        private readonly FormattedText _text = new()
        {
            Typeface = _typeface,
            FontSize = 13
        };

        public float Scale
        {
            get => GetValue(ScaleProperty);
            set => SetValue(ScaleProperty, value);
        }

        public int Rate
        {
            get => GetValue(RateProperty);
            set => SetValue(RateProperty, value);
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            const int top = 16;//15
            double PixelToSec(double pixel)
            {
                return pixel / (ConstantSettings.WidthOf1Frame * Scale) / Rate;
            }

            var height = Bounds.Height;
            var scroll = this.FindLogicalAncestorOfType<ScrollViewer>();
            var viewport = new Rect(new Point(scroll.Offset.X, scroll.Offset.Y), scroll.Viewport);
            var wf = ConstantSettings.WidthOf1Frame;

            var recentPix = 0d;
            var inc = wf * 30;
            var l = viewport.Width + viewport.X;

            for (var x = Math.Floor(viewport.X / inc) * inc; x < l; x += inc)
            {
                var s = PixelToSec(x);

                if (viewport.Contains(new Point(x, Bounds.Height)))
                {
                    context.DrawLine(_pen, new(x, 5), new(x, height));
                }

                var time = TimeSpan.FromSeconds(s);
                _text.Text = time.ToString("hh\\:mm\\:ss\\.ff");
                var textbounds = _text.Bounds.WithX(x + 8);

                if (viewport.Intersects(textbounds) && (recentPix == 0d || (x + 8) > recentPix))
                {
                    recentPix = textbounds.Right;
                    context.DrawText(_pen.Brush, new(x + 8, 0), _text);
                }

                var ll = x + inc;
                for (var xx = x + wf; xx < ll; xx += wf)
                {
                    if (!viewport.Contains(new Point(xx, Bounds.Height))) continue;

                    if (Width < xx) return;

                    context.DrawLine(_pen, new(xx, top), new(xx, height));
                }
            }
        }
    }
}