using Avalonia;

using Beutl.Language;

using FluentAvalonia.UI.Media;

namespace Beutl.Controls.PropertyEditors;

// ColorPickerComponent.cs
public class ColorComponentsEditor : Vector3Editor<int>
{
    // ColorPickerComponent.properties.cs
    public static readonly DirectProperty<ColorComponentsEditor, Color2> ColorProperty =
        AvaloniaProperty.RegisterDirect<ColorComponentsEditor, Color2>(nameof(Color),
            x => x.Color, (x, v) => x.Color = v);

    public static readonly DirectProperty<ColorComponentsEditor, bool> RgbProperty =
        AvaloniaProperty.RegisterDirect<ColorComponentsEditor, bool>(nameof(Rgb),
            x => x.Rgb, (x, v) => x.Rgb = v, unsetValue: true);

    private Color2 _color = Color2.FromHSVf(71, 0.54f, .5f);
    private bool _rgb = true;

    static ColorComponentsEditor()
    {
        ValueChangedEvent.AddClassHandler<ColorComponentsEditor>((t, args) =>
        {
            if (args is PropertyEditorValueChangedEventArgs<(int, int, int)> e)
            {
                if (t.Rgb)
                {
                    (byte r, byte g, byte b) = (
                        (byte)Math.Clamp(e.NewValue.Item1, 0, 255),
                        (byte)Math.Clamp(e.NewValue.Item2, 0, 255),
                        (byte)Math.Clamp(e.NewValue.Item3, 0, 255));
                    t.Color = Color2.FromRGB(r, g, b);
                }
                else
                {
                    (int h, int s, int v) = e.NewValue;
                    h %= 360;
                    if (h < 0)
                    {
                        h += 360;
                    }

                    t.Color = Color2.FromHSV(h, s, v);
                }

                t.UpdateProperties();
            }
        });
    }

    public ColorComponentsEditor()
    {
        UpdateHeaders();
    }

    public Color2 Color
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
            FirstValue = Color.R;
            SecondValue = Color.G;
            ThirdValue = Color.B;
        }
        else
        {
            FirstValue = Color.Hue;
            SecondValue = Color.Saturation;
            ThirdValue = Color.Value;
        }
    }
}
