using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BeUtl.Graphics;
using BeUtl.ViewModels.Editors;

namespace BeUtl.Views.Editors;

public sealed class VectorEditor : BaseVector2Editor<Vector>
{
    private static readonly DynamicResourceExtension s_xResource = new("S.Editors.Vector.X");
    private static readonly DynamicResourceExtension s_yResource = new("S.Editors.Vector.Y");
    private static readonly Binding s_x = new("Value.Value.X", BindingMode.OneWay);
    private static readonly Binding s_y = new("Value.Value.Y", BindingMode.OneWay);

    public VectorEditor()
    {
        xText[!TextBlock.TextProperty] = s_xResource;
        yText[!TextBlock.TextProperty] = s_yResource;

        xTextBox[!TextBox.TextProperty] = s_x;
        yTextBox[!TextBox.TextProperty] = s_y;
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
