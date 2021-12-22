using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BEditorNext.ViewModels.Editors;

namespace BEditorNext.Views.Editors;

public sealed class RectEditor : BaseVector4Editor<Graphics.Rect>
{
    public RectEditor()
    {
        xText[!TextBlock.TextProperty] = new DynamicResourceExtension("XString");
        yText[!TextBlock.TextProperty] = new DynamicResourceExtension("YString");
        zText[!TextBlock.TextProperty] = new DynamicResourceExtension("WidthString");
        wText[!TextBlock.TextProperty] = new DynamicResourceExtension("HeightString");
        xTextBox[!TextBox.TextProperty] = new Binding("Value.Value.X", BindingMode.OneWay);
        yTextBox[!TextBox.TextProperty] = new Binding("Value.Value.Y", BindingMode.OneWay);
        zTextBox[!TextBox.TextProperty] = new Binding("Value.Value.Width", BindingMode.OneWay);
        wTextBox[!TextBox.TextProperty] = new Binding("Value.Value.Height", BindingMode.OneWay);
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
