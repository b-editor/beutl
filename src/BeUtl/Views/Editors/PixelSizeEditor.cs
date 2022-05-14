using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BeUtl.ViewModels.Editors;

namespace BeUtl.Views.Editors;

public sealed class PixelSizeEditor : BaseVector2Editor<Media.PixelSize>
{
    public PixelSizeEditor()
    {
        xText[!TextBlock.TextProperty] = new DynamicResourceExtension("S.Editors.PixelSize.X");
        yText[!TextBlock.TextProperty] = new DynamicResourceExtension("S.Editors.PixelSize.Y");
        // Todo: Bindingをキャッシュする
        xTextBox[!TextBox.TextProperty] = new Binding("Value.Value.Width", BindingMode.OneWay);
        yTextBox[!TextBox.TextProperty] = new Binding("Value.Value.Height", BindingMode.OneWay);
    }

    protected override Media.PixelSize Clamp(Media.PixelSize value)
    {
        if (DataContext is PixelSizeEditorViewModel vm)
        {
            return new Media.PixelSize(
                Math.Clamp(value.Width, vm.Minimum.Width, vm.Maximum.Width),
                Math.Clamp(value.Height, vm.Minimum.Height, vm.Maximum.Height));
        }
        else
        {
            return value;
        }
    }

    protected override Media.PixelSize IncrementX(Media.PixelSize value, int increment)
    {
        return value.WithWidth(value.Width + increment);
    }

    protected override Media.PixelSize IncrementY(Media.PixelSize value, int increment)
    {
        return value.WithHeight(value.Height + increment);
    }

    protected override bool TryParse(string? x, string? y, out Media.PixelSize value)
    {
        if (int.TryParse(x, out int xi) && int.TryParse(y, out int yi))
        {
            value = new Media.PixelSize(xi, yi);
            return true;
        }
        else
        {
            value = default;
            return false;
        }
    }
}
