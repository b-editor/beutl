using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BeUtl.Graphics;
using BeUtl.ViewModels.Editors;

namespace BeUtl.Views.Editors;

public sealed class RelativePointEditor : BaseVector2Editor<RelativePoint>
{
    private static readonly DynamicResourceExtension s_xResource = new("S.Editors.RelativePoint.X");
    private static readonly DynamicResourceExtension s_yResource = new("S.Editors.RelativePoint.Y");
    private static readonly Binding s_x = new("Value.Value", BindingMode.OneWay)
    {
        Converter = new FuncValueConverter<RelativePoint, string>(p =>
        {
            return p.Unit switch
            {
                RelativeUnit.Relative => $"{p.Point.X * 100:f}%",
                RelativeUnit.Absolute => p.Point.X.ToString(),
                _ => p.Point.X.ToString(),
            };
        })
    };
    private static readonly Binding s_y = new("Value.Value", BindingMode.OneWay)
    {
        Converter = new FuncValueConverter<RelativePoint, string>(p =>
        {
            return p.Unit switch
            {
                RelativeUnit.Relative => $"{p.Point.Y * 100:f}%",
                RelativeUnit.Absolute => p.Point.Y.ToString(),
                _ => p.Point.Y.ToString(),
            };
        })
    };

    public RelativePointEditor()
    {
        xText[!TextBlock.TextProperty] = s_xResource;
        yText[!TextBlock.TextProperty] = s_yResource;
        xTextBox[!TextBox.TextProperty] = s_x;
        yTextBox[!TextBox.TextProperty] = s_y;
    }

    protected override RelativePoint Clamp(RelativePoint value)
    {
        if (DataContext is RelativePointEditorViewModel vm)
        {
            return new RelativePoint(
                Math.Clamp(value.Point.X, vm.Minimum.Point.X, vm.Maximum.Point.X),
                Math.Clamp(value.Point.Y, vm.Minimum.Point.Y, vm.Maximum.Point.Y),
                value.Unit);
        }
        else
        {
            return value;
        }
    }

    protected override RelativePoint IncrementX(RelativePoint value, int increment)
    {
        float a = value.Unit == RelativeUnit.Relative ? 100f : 1;
        Point point = value.Point.WithX(value.Point.X + (increment / a));

        return new RelativePoint(point, value.Unit);
    }

    protected override RelativePoint IncrementY(RelativePoint value, int increment)
    {
        float a = value.Unit == RelativeUnit.Relative ? 100f : 1;
        Point point = value.Point.WithY(value.Point.Y + (increment / a));

        return new RelativePoint(point, value.Unit);
    }

    protected override bool TryParse(string? x, string? y, out RelativePoint value)
    {
        try
        {
            value = RelativePoint.Parse($"{x}, {y}");
            return true;
        }
        catch
        {
            value = default;
            return false;
        }
    }
}
