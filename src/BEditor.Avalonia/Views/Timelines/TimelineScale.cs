using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using BEditor.Models;

namespace BEditor.Views.Timelines
{
    public class TimelineScale : Control
    {
        public static readonly StyledProperty<float> ScaleProperty = AvaloniaProperty.Register<TimelineScale, float>(nameof(Scale), 150);
        public static readonly StyledProperty<int> RateProperty = AvaloniaProperty.Register<TimelineScale, int>(nameof(Rate), 30);
        public static readonly StyledProperty<int> MaxProperty = AvaloniaProperty.Register<TimelineScale, int>(nameof(Max), 1500);
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

        public int Max
        {
            get => GetValue(MaxProperty);
            set => SetValue(MaxProperty, value);
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

            var length = Max / Rate;
            var rate = Rate;
            var scale = Scale;
            var height = Bounds.Height;

            if (scale is >= 50 and <= 200f)
            {
                //sは秒数
                for (var s = 0; s < length; s++)
                {
                    //一秒毎
                    var x = ToPixel((s * rate) - 1);
                    context.DrawLine(_pen, new(x, 5), new(x, height));

                    if (s is not 0)
                    {
                        _text.Text = s.ToString() + " sec";
                        context.DrawText(_pen.Brush, new(x + 8, 0), _text);
                    }

                    int value;

                    if (scale is <= 200 and >= 150)
                    {
                        value = 1;
                    }
                    else if (scale is < 150 and >= 100)
                    {
                        value = 2;
                    }
                    else if (scale is < 100 and >= 50)
                    {
                        value = 4;
                    }
                    else
                    {
                        return;
                    }

                    //以下はフレーム
                    for (var frame = 1; frame < rate / value; frame++)
                    {
                        var xx = ToPixel(frame * value) + x;
                        context.DrawLine(_pen, new(xx, top), new(xx, height));
                    }
                }
            }
            else
            {
                //min は分数
                //最大の分
                for (var min = 0; min < length; min++)
                {
                    var x = MinToPixel(min);
                    context.DrawLine(_pen, new(x, 5), new(x, height));

                    if (min is not 0)
                    {
                        _text.Text = min.ToString() + " min";
                        context.DrawText(_pen.Brush, new(x + 8, 0), _text);
                    }

                    int value;

                    if (scale is <= 50 and >= 40)
                    {
                        value = 1;
                    }
                    else if (scale is < 40 and >= 30)
                    {
                        value = 2;
                    }
                    else if (scale is < 30 and >= 20)
                    {
                        value = 3;
                    }
                    else if (scale is < 20 and >= 10)
                    {
                        value = 4;
                    }
                    else if (scale is < 10 and >= 0)
                    {
                        value = 5;
                    }
                    else
                    {
                        return;
                    }

                    for (var s = 1; s < 60 / value; s++)
                    {
                        var xx = SecToPixel(s * value) + x;
                        context.DrawLine(_pen, new(xx, top), new(xx, height));
                    }
                }
            }
        }
    }
}
