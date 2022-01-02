using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BEditorNext.Graphics;
using BEditorNext.ViewModels.Editors;

namespace BEditorNext.Views.Editors;

public sealed class VectorEditor : BaseVector2Editor<Vector>
{
    public VectorEditor()
    {
        xText[!TextBlock.TextProperty] = new DynamicResourceExtension("XString");
        yText[!TextBlock.TextProperty] = new DynamicResourceExtension("YString");
        xTextBox[!TextBox.TextProperty] = new Binding("Value.Value.X", BindingMode.OneWay);
        yTextBox[!TextBox.TextProperty] = new Binding("Value.Value.Y", BindingMode.OneWay);
    }

    protected override Vector Clamp(Vector value)
    {
        if (DataContext is VectorEditorViewModel vm)
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
