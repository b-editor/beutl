using System.Numerics;

using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BeUtl.ViewModels.Editors;

namespace BeUtl.Views.Editors;

public sealed class Vector2Editor : BaseVector2Editor<Vector2>
{
    private static readonly DynamicResourceExtension s_xResource = new("S.Editors.Vector2.X");
    private static readonly DynamicResourceExtension s_yResource = new("S.Editors.Vector2.Y");
    private static readonly Binding s_x = new("Value.Value.X", BindingMode.OneWay);
    private static readonly Binding s_y = new("Value.Value.Y", BindingMode.OneWay);

    public Vector2Editor()
    {
        xText[!TextBlock.TextProperty] = s_xResource;
        yText[!TextBlock.TextProperty] = s_yResource;

        xTextBox[!TextBox.TextProperty] = s_x;
        yTextBox[!TextBox.TextProperty] = s_y;
    }

    protected override Vector2 Clamp(Vector2 value)
    {
        if (DataContext is Vector2EditorViewModel vm)
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
