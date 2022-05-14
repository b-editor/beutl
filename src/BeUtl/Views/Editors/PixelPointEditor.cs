using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BeUtl.ViewModels.Editors;

namespace BeUtl.Views.Editors;

public sealed class PixelPointEditor : BaseVector2Editor<Media.PixelPoint>
{
    public PixelPointEditor()
    {
        xText[!TextBlock.TextProperty] = new DynamicResourceExtension("S.Editors.PixelPoint.X");
        yText[!TextBlock.TextProperty] = new DynamicResourceExtension("S.Editors.PixelPoint.Y");
        // Todo: Bindingをキャッシュする
        xTextBox[!TextBox.TextProperty] = new Binding("Value.Value.X", BindingMode.OneWay);
        yTextBox[!TextBox.TextProperty] = new Binding("Value.Value.Y", BindingMode.OneWay);
    }

    protected override Media.PixelPoint Clamp(Media.PixelPoint value)
    {
        if (DataContext is PixelPointEditorViewModel vm)
        {
            return new Media.PixelPoint(
                Math.Clamp(value.X, vm.Minimum.X, vm.Maximum.X),
                Math.Clamp(value.Y, vm.Minimum.Y, vm.Maximum.Y));
        }
        else
        {
            return value;
        }
    }

    protected override Media.PixelPoint IncrementX(Media.PixelPoint value, int increment)
    {
        return value.WithX(value.X + increment);
    }

    protected override Media.PixelPoint IncrementY(Media.PixelPoint value, int increment)
    {
        return value.WithY(value.Y + increment);
    }

    protected override bool TryParse(string? x, string? y, out Media.PixelPoint value)
    {
        if (int.TryParse(x, out int xi) && int.TryParse(y, out int yi))
        {
            value = new Media.PixelPoint(xi, yi);
            return true;
        }
        else
        {
            value = default;
            return false;
        }
    }
}
