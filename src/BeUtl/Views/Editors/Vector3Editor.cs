using System.Numerics;

using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BeUtl.ViewModels.Editors;

namespace BeUtl.Views.Editors;

public sealed class Vector3Editor : BaseVector3Editor<Vector3>
{
    private static readonly DynamicResourceExtension s_xResource = new("S.Editors.Vector3.X");
    private static readonly DynamicResourceExtension s_yResource = new("S.Editors.Vector3.Y");
    private static readonly DynamicResourceExtension s_zResource = new("S.Editors.Vector3.Z");
    private static readonly Binding s_x = new("Value.Value.X", BindingMode.OneWay);
    private static readonly Binding s_y = new("Value.Value.Y", BindingMode.OneWay);
    private static readonly Binding s_z = new("Value.Value.Z", BindingMode.OneWay);

    public Vector3Editor()
    {
        xText[!TextBlock.TextProperty] = s_xResource;
        yText[!TextBlock.TextProperty] = s_yResource;
        zText[!TextBlock.TextProperty] = s_zResource;

        xTextBox[!TextBox.TextProperty] = s_x;
        yTextBox[!TextBox.TextProperty] = s_y;
        zTextBox[!TextBox.TextProperty] = s_z;
    }

    protected override Vector3 Clamp(Vector3 value)
    {
        if (DataContext is Vector3EditorViewModel vm)
        {
            return Vector3.Clamp(value, vm.Minimum, vm.Maximum);
        }
        else
        {
            return value;
        }
    }

    protected override Vector3 IncrementX(Vector3 value, int increment)
    {
        value.X += increment;
        return value;
    }

    protected override Vector3 IncrementY(Vector3 value, int increment)
    {
        value.Y += increment;
        return value;
    }

    protected override Vector3 IncrementZ(Vector3 value, int increment)
    {
        value.Z += increment;
        return value;
    }

    protected override bool TryParse(string? x, string? y, string? z, out Vector3 value)
    {
        if (float.TryParse(x, out float xi) &&
            float.TryParse(y, out float yi) &&
            float.TryParse(z, out float zi))
        {
            value = new Vector3(xi, yi, zi);
            return true;
        }
        else
        {
            value = default;
            return false;
        }
    }
}
