using System.Numerics;

using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BeUtl.ViewModels.Editors;

namespace BeUtl.Views.Editors;

public sealed class Vector4Editor : BaseVector4Editor<Vector4>
{
    public Vector4Editor()
    {
        xText[!TextBlock.TextProperty] = new DynamicResourceExtension("XString");
        yText[!TextBlock.TextProperty] = new DynamicResourceExtension("YString");
        zText[!TextBlock.TextProperty] = new DynamicResourceExtension("ZString");
        wText[!TextBlock.TextProperty] = new DynamicResourceExtension("WString");
        xTextBox[!TextBox.TextProperty] = new Binding("Value.Value.X", BindingMode.OneWay);
        yTextBox[!TextBox.TextProperty] = new Binding("Value.Value.Y", BindingMode.OneWay);
        zTextBox[!TextBox.TextProperty] = new Binding("Value.Value.Z", BindingMode.OneWay);
        wTextBox[!TextBox.TextProperty] = new Binding("Value.Value.W", BindingMode.OneWay);
    }

    protected override Vector4 Clamp(Vector4 value)
    {
        if (DataContext is Vector4EditorViewModel vm)
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
