using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BeUtl.ViewModels.Editors;

namespace BeUtl.Views.Editors;

public sealed class PixelSizeEditor : BaseVector2Editor<Media.PixelSize>
{
    private static readonly DynamicResourceExtension s_xResource = new("S.Editors.PixelSize.X");
    private static readonly DynamicResourceExtension s_yResource = new("S.Editors.PixelSize.Y");
    private static readonly Binding s_x = new("Value.Value.Width", BindingMode.OneWay);
    private static readonly Binding s_y = new("Value.Value.Height", BindingMode.OneWay);

    public PixelSizeEditor()
    {
        xText[!TextBlock.TextProperty] = s_xResource;
        yText[!TextBlock.TextProperty] = s_yResource;

        xTextBox[!TextBox.TextProperty] = s_x;
        yTextBox[!TextBox.TextProperty] = s_y;
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
