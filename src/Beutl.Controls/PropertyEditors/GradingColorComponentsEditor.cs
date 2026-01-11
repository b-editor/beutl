using Avalonia;
using Beutl.Language;
using Beutl.Media;
using FluentAvalonia.UI.Media;
using UnboundedHsv = (float H, float S, float V);

#nullable enable

namespace Beutl.Controls.PropertyEditors;

public class GradingColorComponentsEditor : Vector3Editor<float>
{
    public static readonly DirectProperty<GradingColorComponentsEditor, GradingColor> ColorProperty =
        AvaloniaProperty.RegisterDirect<GradingColorComponentsEditor, GradingColor>(nameof(Color),
            x => x.Color, (x, v) => x.Color = v);

    public static readonly DirectProperty<ColorComponentsEditor, bool> RgbProperty =
        AvaloniaProperty.RegisterDirect<ColorComponentsEditor, bool>(nameof(Rgb),
            x => x.Rgb, (x, v) => x.Rgb = v, unsetValue: true);

    private GradingColor _color = new GradingColor(1, 1, 1);
    private bool _rgb = true;

    static GradingColorComponentsEditor()
    {
        ValueChangedEvent.AddClassHandler<GradingColorComponentsEditor>((t, args) =>
        {
            if (args is PropertyEditorValueChangedEventArgs<(float, float, float)> e)
            {
                t.Color = t.ToGradingColorFromTuple(e.NewValue);
                t.UpdateProperties();
            }
        });
    }

    public GradingColorComponentsEditor()
    {
        UpdateHeaders();
        UpdateProperties();
    }

    public GradingColor Color
    {
        get => _color;
        set
        {
            if (SetAndRaise(ColorProperty, ref _color, value))
            {
                UpdateProperties();
            }
        }
    }

    public bool Rgb
    {
        get => _rgb;
        set
        {
            if (SetAndRaise(RgbProperty, ref _rgb, value))
            {
                UpdateHeaders();
                UpdateProperties();
            }
        }
    }

    private void UpdateHeaders()
    {
        if (Rgb)
        {
            FirstHeader = Strings.Red;
            SecondHeader = Strings.Green;
            ThirdHeader = Strings.Blue;
        }
        else
        {
            FirstHeader = Strings.Hue;
            SecondHeader = Strings.Saturation;
            ThirdHeader = Strings.Brightness;
        }
    }

    private void UpdateProperties()
    {
        if (Rgb)
        {
            FirstValue = Color.R * 100f;
            SecondValue = Color.G * 100f;
            ThirdValue = Color.B * 100f;
        }
        else
        {
            var color2 = GradingColorHelper.GetUnboundedHsv(Color);
            FirstValue = color2.H;
            SecondValue = color2.S * 100f;
            ThirdValue = color2.V * 100f;
        }
    }

    public GradingColor ToGradingColorFromTuple((float, float, float) t)
    {
        if (Rgb)
        {
            return new GradingColor(
                t.Item1 / 100f,
                t.Item2 / 100f,
                t.Item3 / 100f);
        }
        else
        {
            (float h, float s, float v) = t;
            h %= 360;
            if (h < 0)
            {
                h += 360;
            }

            var hsv = (h, s / 100f, v / 100f);
            return GradingColorHelper.GetColor(hsv);
        }
    }

    public (GradingColor?, UnboundedHsv?) GetGradingColorOrUnboundedHsv((float, float, float) t)
    {
        if (Rgb)
        {
            return (new GradingColor(
                t.Item1 / 100f,
                t.Item2 / 100f,
                t.Item3 / 100f),
                null);
        }
        else
        {
            (float h, float s, float v) = t;
            h %= 360;
            if (h < 0)
            {
                h += 360;
            }

            return (null, (h, s / 100f, v / 100f));
        }
    }
}
