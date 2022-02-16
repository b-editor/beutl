using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BeUtl.ViewModels.AnimationEditors;

namespace BeUtl.Views.AnimationEditors;

public sealed class PixelSizeAnimationEditor : BaseVector2AnimationEditor<Media.PixelSize>
{
    public PixelSizeAnimationEditor()
    {
        var xres = new DynamicResourceExtension("S.Editors.PixelSize.X");
        var yres = new DynamicResourceExtension("S.Editors.PixelSize.Y");
        prevXText[!TextBlock.TextProperty] = xres;
        prevYText[!TextBlock.TextProperty] = yres;
        nextXText[!TextBlock.TextProperty] = xres;
        nextYText[!TextBlock.TextProperty] = yres;

        prevXTextBox[!TextBox.TextProperty] = new Binding("Animation.Previous.Width", BindingMode.OneWay);
        prevYTextBox[!TextBox.TextProperty] = new Binding("Animation.Previous.Height", BindingMode.OneWay);
        nextXTextBox[!TextBox.TextProperty] = new Binding("Animation.Next.Width", BindingMode.OneWay);
        nextYTextBox[!TextBox.TextProperty] = new Binding("Animation.Next.Height", BindingMode.OneWay);
    }

    protected override Media.PixelSize Clamp(Media.PixelSize value)
    {
        if (DataContext is PixelSizeAnimationEditorViewModel vm)
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
