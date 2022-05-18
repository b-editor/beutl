using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BeUtl.ViewModels.Editors;

namespace BeUtl.Views.Editors;

public sealed class RectEditor : BaseVector4Editor<Graphics.Rect>
{
    private static readonly DynamicResourceExtension s_xResource = new("S.Editors.Rect.X");
    private static readonly DynamicResourceExtension s_yResource = new("S.Editors.Rect.Y");
    private static readonly DynamicResourceExtension s_zResource = new("S.Editors.Rect.Z");
    private static readonly DynamicResourceExtension s_wResource = new("S.Editors.Rect.W");
    private static readonly Binding s_x = new("Value.Value.X", BindingMode.OneWay);
    private static readonly Binding s_y = new("Value.Value.Y", BindingMode.OneWay);
    private static readonly Binding s_z = new("Value.Value.Width", BindingMode.OneWay);
    private static readonly Binding s_w = new("Value.Value.Height", BindingMode.OneWay);

    public RectEditor()
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

    protected override Graphics.Rect Clamp(Graphics.Rect value)
    {
        if (DataContext is RectEditorViewModel vm)
        {
            return new Graphics.Rect(
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

    protected override Graphics.Rect IncrementX(Graphics.Rect value, int increment)
    {
        return value.WithX(value.X + increment);
    }

    protected override Graphics.Rect IncrementY(Graphics.Rect value, int increment)
    {
        return value.WithY(value.Y + increment);
    }

    protected override Graphics.Rect IncrementZ(Graphics.Rect value, int increment)
    {
        return value.WithWidth(value.Width + increment);
    }

    protected override Graphics.Rect IncrementW(Graphics.Rect value, int increment)
    {
        return value.WithHeight(value.Height + increment);
    }

    protected override bool TryParse(string? x, string? y, string? z, string? w, out Graphics.Rect value)
    {
        if (float.TryParse(x, out float xi) &&
            float.TryParse(y, out float yi) &&
            float.TryParse(z, out float zi) &&
            float.TryParse(w, out float wi))
        {
            value = new Graphics.Rect(xi, yi, zi, wi);
            return true;
        }
        else
        {
            value = default;
            return false;
        }
    }
}
