using System.Numerics;

using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BeUtl.ViewModels.AnimationEditors;

namespace BeUtl.Views.AnimationEditors;

public sealed class Vector2AnimationEditor : BaseVector2AnimationEditor<Vector2>
{
    public Vector2AnimationEditor()
    {
        var xres = new DynamicResourceExtension("S.Editors.Vector2.X");
        var yres = new DynamicResourceExtension("S.Editors.Vector2.Y");
        prevXText[!TextBlock.TextProperty] = xres;
        prevYText[!TextBlock.TextProperty] = yres;
        nextXText[!TextBlock.TextProperty] = xres;
        nextYText[!TextBlock.TextProperty] = yres;

        prevXTextBox[!TextBox.TextProperty] = new Binding("Animation.Previous.X", BindingMode.OneWay);
        prevYTextBox[!TextBox.TextProperty] = new Binding("Animation.Previous.Y", BindingMode.OneWay);
        nextXTextBox[!TextBox.TextProperty] = new Binding("Animation.Next.X", BindingMode.OneWay);
        nextYTextBox[!TextBox.TextProperty] = new Binding("Animation.Next.Y", BindingMode.OneWay);
    }

    protected override Vector2 Clamp(Vector2 value)
    {
        if (DataContext is Vector2AnimationEditorViewModel vm)
        {
            return Vector2.Clamp(value, vm.Minimum, vm.Maximum);
        }
        else
        {
            return value;
        }
    }

    protected override Vector2 IncrementX(Vector2 value, int increment)
    {
        value.X += increment;
        return value;
    }

    protected override Vector2 IncrementY(Vector2 value, int increment)
    {
        value.Y += increment;
        return value;
    }

    protected override bool TryParse(string? x, string? y, out Vector2 value)
    {
        if (float.TryParse(x, out float xi) && float.TryParse(y, out float yi))
        {
            value = new Vector2(xi, yi);
            return true;
        }
        else
        {
            value = default;
            return false;
        }
    }
}
