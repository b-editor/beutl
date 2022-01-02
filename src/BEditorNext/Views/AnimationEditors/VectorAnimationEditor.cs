using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BEditorNext.Graphics;
using BEditorNext.ViewModels.AnimationEditors;

namespace BEditorNext.Views.AnimationEditors;

public sealed class VectorAnimationEditor : BaseVector2AnimationEditor<Vector>
{
    public VectorAnimationEditor()
    {
        var xres = new DynamicResourceExtension("XString");
        var yres = new DynamicResourceExtension("YString");
        prevXText[!TextBlock.TextProperty] = xres;
        prevYText[!TextBlock.TextProperty] = yres;
        nextXText[!TextBlock.TextProperty] = xres;
        nextYText[!TextBlock.TextProperty] = yres;

        prevXTextBox[!TextBox.TextProperty] = new Binding("Animation.Previous.X", BindingMode.OneWay);
        prevYTextBox[!TextBox.TextProperty] = new Binding("Animation.Previous.Y", BindingMode.OneWay);
        nextXTextBox[!TextBox.TextProperty] = new Binding("Animation.Next.X", BindingMode.OneWay);
        nextYTextBox[!TextBox.TextProperty] = new Binding("Animation.Next.Y", BindingMode.OneWay);
    }

    protected override Vector Clamp(Vector value)
    {
        if (DataContext is VectorAnimationEditorViewModel vm)
        {
            return new Vector(Math.Clamp(value.X, vm.Minimum.X, vm.Maximum.X), Math.Clamp(value.Y, vm.Minimum.Y, vm.Maximum.Y));
        }
        else
        {
            return value;
        }
    }

    protected override Vector IncrementX(Vector value, int increment)
    {
        return value.WithX(value.X + increment);
    }

    protected override Vector IncrementY(Vector value, int increment)
    {
        return value.WithY(value.Y + increment);
    }

    protected override bool TryParse(string? x, string? y, out Vector value)
    {
        if (float.TryParse(x, out float xi) && float.TryParse(y, out float yi))
        {
            value = new Vector(xi, yi);
            return true;
        }
        else
        {
            value = default;
            return false;
        }
    }
}
