using System.Numerics;

using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BEditorNext.ViewModels.AnimationEditors;

namespace BEditorNext.Views.AnimationEditors;

public sealed class Vector3AnimationEditor : BaseVector3AnimationEditor<Vector3>
{
    public Vector3AnimationEditor()
    {
        var xres = new DynamicResourceExtension("XString");
        var yres = new DynamicResourceExtension("YString");
        var zres = new DynamicResourceExtension("ZString");
        prevXText[!TextBlock.TextProperty] = xres;
        prevYText[!TextBlock.TextProperty] = yres;
        prevZText[!TextBlock.TextProperty] = zres;

        nextXText[!TextBlock.TextProperty] = xres;
        nextYText[!TextBlock.TextProperty] = yres;
        nextZText[!TextBlock.TextProperty] = zres;

        prevXTextBox[!TextBox.TextProperty] = new Binding("Animation.Previous.X", BindingMode.OneWay);
        prevYTextBox[!TextBox.TextProperty] = new Binding("Animation.Previous.Y", BindingMode.OneWay);
        prevZTextBox[!TextBox.TextProperty] = new Binding("Animation.Previous.Z", BindingMode.OneWay);

        nextXTextBox[!TextBox.TextProperty] = new Binding("Animation.Next.X", BindingMode.OneWay);
        nextYTextBox[!TextBox.TextProperty] = new Binding("Animation.Next.Y", BindingMode.OneWay);
        nextZTextBox[!TextBox.TextProperty] = new Binding("Animation.Next.Z", BindingMode.OneWay);
    }

    protected override Vector3 Clamp(Vector3 value)
    {
        if (DataContext is Vector3AnimationEditorViewModel vm)
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
