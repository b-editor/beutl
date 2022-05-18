using System.Numerics;

using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BeUtl.ViewModels.AnimationEditors;

namespace BeUtl.Views.AnimationEditors;

public sealed class Vector2AnimationEditor : BaseVector2AnimationEditor<Vector2>
{
    private static readonly DynamicResourceExtension s_xResource = new("S.Editors.Vector2.X");
    private static readonly DynamicResourceExtension s_yResource = new("S.Editors.Vector2.Y");
    private static readonly Binding s_prevX = new("Animation.Previous.X", BindingMode.OneWay);
    private static readonly Binding s_prevY = new("Animation.Previous.Y", BindingMode.OneWay);
    private static readonly Binding s_nextX = new("Animation.Next.X", BindingMode.OneWay);
    private static readonly Binding s_nextY = new("Animation.Next.Y", BindingMode.OneWay);

    public Vector2AnimationEditor()
    {
        prevXText[!TextBlock.TextProperty] = s_xResource;
        prevYText[!TextBlock.TextProperty] = s_yResource;
        nextXText[!TextBlock.TextProperty] = s_xResource;
        nextYText[!TextBlock.TextProperty] = s_yResource;

        prevXTextBox[!TextBox.TextProperty] = s_prevX;
        prevYTextBox[!TextBox.TextProperty] = s_prevY;
        nextXTextBox[!TextBox.TextProperty] = s_nextX;
        nextYTextBox[!TextBox.TextProperty] = s_nextY;
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
