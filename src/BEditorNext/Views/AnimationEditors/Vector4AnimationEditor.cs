using System.Numerics;

using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BEditorNext.ViewModels.AnimationEditors;

namespace BEditorNext.Views.AnimationEditors;

public sealed class Vector4AnimationEditor : BaseVector4AnimationEditor<Vector4>
{
    public Vector4AnimationEditor()
    {
        var xres = new DynamicResourceExtension("XString");
        var yres = new DynamicResourceExtension("YString");
        var zres = new DynamicResourceExtension("ZString");
        var wres = new DynamicResourceExtension("WString");
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
        prevZTextBox[!TextBox.TextProperty] = new Binding("Animation.Previous.Z", BindingMode.OneWay);
        prevWTextBox[!TextBox.TextProperty] = new Binding("Animation.Previous.W", BindingMode.OneWay);

        nextXTextBox[!TextBox.TextProperty] = new Binding("Animation.Next.X", BindingMode.OneWay);
        nextYTextBox[!TextBox.TextProperty] = new Binding("Animation.Next.Y", BindingMode.OneWay);
        nextZTextBox[!TextBox.TextProperty] = new Binding("Animation.Next.Z", BindingMode.OneWay);
        nextWTextBox[!TextBox.TextProperty] = new Binding("Animation.Next.W", BindingMode.OneWay);
    }

    protected override Vector4 Clamp(Vector4 value)
    {
        if (DataContext is Vector4AnimationEditorViewModel vm)
        {
            return Vector4.Clamp(value, vm.Minimum, vm.Maximum);
        }
        else
        {
            return value;
        }
    }

    protected override Vector4 IncrementX(Vector4 value, int increment)
    {
        value.X += increment;
        return value;
    }

    protected override Vector4 IncrementY(Vector4 value, int increment)
    {
        value.Y += increment;
        return value;
    }

    protected override Vector4 IncrementZ(Vector4 value, int increment)
    {
        value.Z += increment;
        return value;
    }

    protected override Vector4 IncrementW(Vector4 value, int increment)
    {
        value.W += increment;
        return value;
    }

    protected override bool TryParse(string? x, string? y, string? z, string? w, out Vector4 value)
    {
        if (float.TryParse(x, out float xi) &&
            float.TryParse(y, out float yi) &&
            float.TryParse(z, out float zi) &&
            float.TryParse(w, out float wi))
        {
            value = new Vector4(xi, yi, zi, wi);
            return true;
        }
        else
        {
            value = default;
            return false;
        }
    }
}
