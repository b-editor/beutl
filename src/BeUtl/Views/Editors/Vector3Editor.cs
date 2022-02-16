using System.Numerics;

using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BeUtl.ViewModels.Editors;

namespace BeUtl.Views.Editors;

public sealed class Vector3Editor : BaseVector3Editor<Vector3>
{
    public Vector3Editor()
    {
        xText[!TextBlock.TextProperty] = new DynamicResourceExtension("S.Editors.Vector3.X");
        yText[!TextBlock.TextProperty] = new DynamicResourceExtension("S.Editors.Vector3.Y");
        zText[!TextBlock.TextProperty] = new DynamicResourceExtension("S.Editors.Vector3.Z");
        xTextBox[!TextBox.TextProperty] = new Binding("Value.Value.X", BindingMode.OneWay);
        yTextBox[!TextBox.TextProperty] = new Binding("Value.Value.Y", BindingMode.OneWay);
        zTextBox[!TextBox.TextProperty] = new Binding("Value.Value.Z", BindingMode.OneWay);
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
