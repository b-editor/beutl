using System.Numerics;

using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BEditorNext.ViewModels.Editors;

namespace BEditorNext.Views.Editors;

public sealed class Vector2Editor : BaseVector2Editor<Vector2>
{
    public Vector2Editor()
    {
        xText[!TextBlock.TextProperty] = new DynamicResourceExtension("XString");
        yText[!TextBlock.TextProperty] = new DynamicResourceExtension("YString");
        xTextBox[!TextBox.TextProperty] = new Binding("Value.Value.X", BindingMode.OneWay);
        yTextBox[!TextBox.TextProperty] = new Binding("Value.Value.Y", BindingMode.OneWay);
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
