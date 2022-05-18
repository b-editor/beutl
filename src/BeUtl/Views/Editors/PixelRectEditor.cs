using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BeUtl.ViewModels.Editors;

namespace BeUtl.Views.Editors;

public sealed class PixelRectEditor : BaseVector4Editor<Media.PixelRect>
{
    private static readonly DynamicResourceExtension s_xResource = new("S.Editors.PixelRect.X");
    private static readonly DynamicResourceExtension s_yResource = new("S.Editors.PixelRect.Y");
    private static readonly DynamicResourceExtension s_zResource = new("S.Editors.PixelRect.Z");
    private static readonly DynamicResourceExtension s_wResource = new("S.Editors.PixelRect.W");
    private static readonly Binding s_x = new("Value.Value.X", BindingMode.OneWay);
    private static readonly Binding s_y = new("Value.Value.Y", BindingMode.OneWay);
    private static readonly Binding s_z = new("Value.Value.Width", BindingMode.OneWay);
    private static readonly Binding s_w = new("Value.Value.Height", BindingMode.OneWay);

    public PixelRectEditor()
    {
        xText[!TextBlock.TextProperty] = s_xResource;
        yText[!TextBlock.TextProperty] = s_yResource;
        zText[!TextBlock.TextProperty] = s_zResource;
        wText[!TextBlock.TextProperty] = s_wResource;

        xTextBox[!TextBox.TextProperty] = s_x;
        yTextBox[!TextBox.TextProperty] = s_y;
        zTextBox[!TextBox.TextProperty] = s_z;
        wTextBox[!TextBox.TextProperty] = s_w;
    }

    protected override Media.PixelRect Clamp(Media.PixelRect value)
    {
        if (DataContext is PixelRectEditorViewModel vm)
        {
            return new Media.PixelRect(
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

    protected override Media.PixelRect IncrementX(Media.PixelRect value, int increment)
    {
        return value.WithX(value.X + increment);
    }

    protected override Media.PixelRect IncrementY(Media.PixelRect value, int increment)
    {
        return value.WithY(value.Y + increment);
    }

    protected override Media.PixelRect IncrementZ(Media.PixelRect value, int increment)
    {
        return value.WithWidth(value.Width + increment);
    }

    protected override Media.PixelRect IncrementW(Media.PixelRect value, int increment)
    {
        return value.WithHeight(value.Height + increment);
    }

    protected override bool TryParse(string? x, string? y, string? z, string? w, out Media.PixelRect value)
    {
        if (int.TryParse(x, out int xi) &&
            int.TryParse(y, out int yi) &&
            int.TryParse(z, out int zi) &&
            int.TryParse(w, out int wi))
        {
            value = new Media.PixelRect(xi, yi, zi, wi);
            return true;
        }
        else
        {
            value = default;
            return false;
        }
    }
}
