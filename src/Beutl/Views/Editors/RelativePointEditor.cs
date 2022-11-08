using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Beutl.Views.Editors;

public sealed class RelativePointEditor : BaseVector2Editor<Graphics.RelativePoint>
{
    private static readonly Binding s_x = new("Value.Value", BindingMode.OneWay)
    {
        Converter = new FuncValueConverter<Graphics.RelativePoint, string>(p =>
        {
            return p.Unit switch
            {
                Graphics.RelativeUnit.Relative => $"{p.Point.X * 100:f}%",
                Graphics.RelativeUnit.Absolute => p.Point.X.ToString(),
                _ => p.Point.X.ToString(),
            };
        })
    };
    private static readonly Binding s_y = new("Value.Value", BindingMode.OneWay)
    {
        Converter = new FuncValueConverter<Graphics.RelativePoint, string>(p =>
        {
            return p.Unit switch
            {
                Graphics.RelativeUnit.Relative => $"{p.Point.Y * 100:f}%",
                Graphics.RelativeUnit.Absolute => p.Point.Y.ToString(),
                _ => p.Point.Y.ToString(),
            };
        })
    };

    public RelativePointEditor()
    {
        xText.Text = "X";
        yText.Text = "Y";
        xTextBox[!TextBox.TextProperty] = s_x;
        yTextBox[!TextBox.TextProperty] = s_y;
    }

    protected override Graphics.RelativePoint IncrementX(Graphics.RelativePoint value, int increment)
    {
        float a = value.Unit == Graphics.RelativeUnit.Relative ? 100f : 1;
        Graphics.Point point = value.Point.WithX(value.Point.X + (increment / a));

        return new Graphics.RelativePoint(point, value.Unit);
    }

    protected override Graphics.RelativePoint IncrementY(Graphics.RelativePoint value, int increment)
    {
        float a = value.Unit == Graphics.RelativeUnit.Relative ? 100f : 1;
        Graphics.Point point = value.Point.WithY(value.Point.Y + (increment / a));

        return new Graphics.RelativePoint(point, value.Unit);
    }

    protected override bool TryParse(string? x, string? y, out Graphics.RelativePoint value)
    {
        try
        {
            value = Graphics.RelativePoint.Parse($"{x}, {y}");
            return true;
        }
        catch
        {
            value = default;
            return false;
        }
    }
}
