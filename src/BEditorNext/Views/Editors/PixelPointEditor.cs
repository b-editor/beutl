using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BEditorNext.Media;
using BEditorNext.ViewModels.Editors;

namespace BEditorNext.Views.Editors;

public sealed class PixelPointEditor : BaseVector2Editor<PixelPoint>
{
    public PixelPointEditor()
    {
        xText[!TextBlock.TextProperty] = new DynamicResourceExtension("XString");
        yText[!TextBlock.TextProperty] = new DynamicResourceExtension("YString");
        xTextBox[!TextBox.TextProperty] = new Binding("Value.Value.X", BindingMode.OneWay);
        yTextBox[!TextBox.TextProperty] = new Binding("Value.Value.Y", BindingMode.OneWay);
    }

    protected override PixelPoint Clamp(PixelPoint value)
    {
        if (DataContext is PixelPointEditorViewModel vm)
        {
            return new PixelPoint(
                Math.Clamp(value.X, vm.Minimum.X, vm.Maximum.X),
                Math.Clamp(value.Y, vm.Minimum.Y, vm.Maximum.Y));
        }
        else
        {
            return value;
        }
    }

    protected override PixelPoint IncrementX(PixelPoint value, int increment)
    {
        return value.WithX(value.X + increment);
    }

    protected override PixelPoint IncrementY(PixelPoint value, int increment)
    {
        return value.WithY(value.Y + increment);
    }

    protected override bool TryParse(string? x, string? y, out PixelPoint value)
    {
        if (int.TryParse(x, out int xi) && int.TryParse(y, out int yi))
        {
            value = new PixelPoint(xi, yi);
            return true;
        }
        else
        {
            value = default;
            return false;
        }
    }
}
