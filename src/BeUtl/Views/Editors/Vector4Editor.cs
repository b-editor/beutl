using System.Numerics;

using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BeUtl.ViewModels.Editors;

namespace BeUtl.Views.Editors;

public sealed class Vector4Editor : BaseVector4Editor<Vector4>
{
    private static readonly DynamicResourceExtension s_xResource = new("S.Editors.Vector4.X");
    private static readonly DynamicResourceExtension s_yResource = new("S.Editors.Vector4.Y");
    private static readonly DynamicResourceExtension s_zResource = new("S.Editors.Vector4.Z");
    private static readonly DynamicResourceExtension s_wResource = new("S.Editors.Vector4.W");
    private static readonly Binding s_x = new("Value.Value.X", BindingMode.OneWay);
    private static readonly Binding s_y = new("Value.Value.Y", BindingMode.OneWay);
    private static readonly Binding s_z = new("Value.Value.Z", BindingMode.OneWay);
    private static readonly Binding s_w = new("Value.Value.W", BindingMode.OneWay);

    public Vector4Editor()
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
