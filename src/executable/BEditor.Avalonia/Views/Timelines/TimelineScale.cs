
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using BEditor.Models;

namespace BEditor.Views.Timelines
{
    public class TimelineScale : Control
    {
        public static readonly StyledProperty<float> ScaleProperty = AvaloniaProperty.Register<TimelineScale, float>(nameof(Scale), 150);
        public static readonly StyledProperty<int> RateProperty = AvaloniaProperty.Register<TimelineScale, int>(nameof(Rate), 30);
        private static readonly Typeface _typeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.Medium);
        private readonly Pen _pen = new()
        {
            Brush = (IBrush)Application.Current.FindResource("SystemControlForegroundBaseMediumBrush")!
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
            double ToPixel(int frame)
            {
                return ConstantSettings.WidthOf1Frame * (Scale / 200) * frame;
            }

            double SecToPixel(float sec)
            {
                return ToPixel((int)(sec * Rate));
            }

            double MinToPixel(float min)
            {
                return SecToPixel(min * 60);
            }

            int ToFrame(double pixel)
            {
                return (int)(pixel / (ConstantSettings.WidthOf1Frame * (Scale / 200)));
            }

            float PixelToSec(double pixel)
            {
                return ToFrame(pixel) / Rate;
            }

            var height = Bounds.Height;
            var scroll = (ScrollViewer)Parent!.Parent!;
            var viewport = new Rect(new Point(scroll.Offset.X, scroll.Offset.Y), scroll.Viewport);
            var totalSec = (int)PixelToSec(viewport.Width) + 2;
            var startSec = (int)PixelToSec(viewport.X);
            //var totalSec = Max / Rate;

            var rate = Rate;
            var scale = Scale;

            if (scale is >= 50 and <= 200f)
            {
                //sは秒数
                for (var s = startSec; s < totalSec + startSec; s++)
                {
                    //一秒毎
                    var x = ToPixel((s * rate) - 1);
                    if (viewport.Contains(new Point(x, Bounds.Height)))
                    {
                        context.DrawLine(_pen, new(x, 5), new(x, height));

                        if (s is not 0)
                        {
                            _text.Text = s.ToString() + " s";
                            context.DrawText(_pen.Brush, new(x + 8, 0), _text);
                        }
                    }

                    int value;

                    if (scale is <= 200f and >= 150f) value = 1;
                    else if (scale is <= 150f and >= 100f) value = 2;
                    else if (scale is <= 100f and >= 50f) value = 6;
                    else return;

                    //以下はフレーム
                    for (var frame = 1; frame < rate / value; frame++)
                    {
                        var xx = ToPixel(frame * value) + x;
                        if (!viewport.Contains(new Point(xx, Bounds.Height))) continue;

                        if (Width < xx) return;

                        context.DrawLine(_pen, new(xx, top), new(xx, height));
                    }
                }
            }
            else
            {
                //min は分数
                //最大の分
                for (var min = startSec / 60; min < ((totalSec + startSec) / 60) + 1; min++)
                {
                    var x = MinToPixel(min);
                    if (viewport.Contains(new Point(x, Bounds.Height)))
                    {
                        context.DrawLine(_pen, new(x, 5), new(x, height));

                        if (min is not 0)
                        {
                            _text.Text = min.ToString() + " m";
                            context.DrawText(_pen.Brush, new(x + 8, 0), _text);
                        }
                    }

                    int value;

                    if (scale is <= 50f and >= 40f) value = 1;
                    else if (scale is <= 40f and >= 30f) value = 2;
                    else if (scale is <= 30f and >= 20f) value = 4;
                    else if (scale is <= 20f and >= 10f) value = 6;
                    else if (scale is <= 10f and >= 0f) value = 15;
                    else return;

                    for (var s = 1; s < 60 / value; s++)
                    {
                        var xx = SecToPixel(s * value) + x;
                        if (!viewport.Contains(new Point(xx, Bounds.Height))) continue;

                        if (Width < xx) return;

                        context.DrawLine(_pen, new(xx, top), new(xx, height));

                        if (value is 2 or 4 or 6)
                        {
                            _text.Text = s.ToString() + " s";
                            context.DrawText(_pen.Brush, new(xx + 8, 0), _text);
                        }
                    }
                }
            }
        }
    }
}