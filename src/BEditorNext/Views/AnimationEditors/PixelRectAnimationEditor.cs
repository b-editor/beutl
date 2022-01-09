using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BEditorNext.Media;
using BEditorNext.ViewModels.AnimationEditors;

namespace BEditorNext.Views.AnimationEditors;

public sealed class PixelRectAnimationEditor : BaseVector4AnimationEditor<PixelRect>
{
    public PixelRectAnimationEditor()
    {
        var xres = new DynamicResourceExtension("XString");
        var yres = new DynamicResourceExtension("YString");
        var zres = new DynamicResourceExtension("WidthString");
        var wres = new DynamicResourceExtension("HeightString");
        prevXText[!TextBlock.TextProperty] = xres;
        prevYText[!TextBlock.TextProperty] = yres;
        prevZText[!TextBlock.TextProperty] = zres;
        prevWText[!TextBlock.TextProperty] = wres;

        nextXText[!TextBlock.TextProperty] = xres;
        nextYText[!TextBlock.TextProperty] = yres;
        nextZText[!TextBlock.TextProperty] = zres;
        nextWText[!TextBlock.TextProperty] = wres;

        prevXTextBox[!TextBox.TextProperty] = new Binding("Animation.Previous.X", BindingMode.OneWay);
        prevYTextBox[!TextBox.TextProperty] = new Binding("Animation.Previous.Y", BindingMode.OneWay);
        prevZTextBox[!TextBox.TextProperty] = new Binding("Animation.Previous.Width", BindingMode.OneWay);
        prevWTextBox[!TextBox.TextProperty] = new Binding("Animation.Previous.Height", BindingMode.OneWay);

        nextXTextBox[!TextBox.TextProperty] = new Binding("Animation.Next.X", BindingMode.OneWay);
        nextYTextBox[!TextBox.TextProperty] = new Binding("Animation.Next.Y", BindingMode.OneWay);
        nextZTextBox[!TextBox.TextProperty] = new Binding("Animation.Next.Width", BindingMode.OneWay);
        nextWTextBox[!TextBox.TextProperty] = new Binding("Animation.Next.Height", BindingMode.OneWay);
    }

    protected override PixelRect Clamp(PixelRect value)
    {
        if (DataContext is PixelRectAnimationEditorViewModel vm)
        {
            return new PixelRect(
                Math.Clamp(value.X, vm.Minimum.X, vm.Maximum.X),
                Math.Clamp(value.Y, vm.Minimum.Y, vm.Maximum.Y),
                Math.Clamp(value.Width, vm.Minimum.Width, vm.Maximum.Width),
                Math.Clamp(value.Height, vm.Minimum.Height, vm.Maximum.Height));
        }
        else
        {
            return value;
        }
    }

    protected override PixelRect IncrementX(PixelRect value, int increment)
    {
        return value.WithX(value.X + increment);
    }

    protected override PixelRect IncrementY(PixelRect value, int increment)
    {
        return value.WithY(value.Y + increment);
    }

    protected override PixelRect IncrementZ(PixelRect value, int increment)
    {
        return value.WithWidth(value.Width + increment);
    }

    protected override PixelRect IncrementW(PixelRect value, int increment)
    {
        return value.WithHeight(value.Height + increment);
    }

    protected override bool TryParse(string? x, string? y, string? z, string? w, out PixelRect value)
    {
        if (int.TryParse(x, out int xi) &&
            int.TryParse(y, out int yi) &&
            int.TryParse(z, out int zi) &&
            int.TryParse(w, out int wi))
        {
            value = new PixelRect(xi, yi, zi, wi);
            return true;
        }
        else
        {
            value = default;
            return false;
        }
    }
}
