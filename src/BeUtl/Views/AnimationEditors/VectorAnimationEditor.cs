using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BeUtl.Graphics;
using BeUtl.ViewModels.AnimationEditors;

namespace BeUtl.Views.AnimationEditors;

public sealed class VectorAnimationEditor : BaseVector2AnimationEditor<Vector>
{
    private static readonly DynamicResourceExtension s_xResource = new("S.Editors.Vector.X");
    private static readonly DynamicResourceExtension s_yResource = new("S.Editors.Vector.Y");
    private static readonly Binding s_prevX = new("Animation.Previous.X", BindingMode.OneWay);
    private static readonly Binding s_prevY = new("Animation.Previous.Y", BindingMode.OneWay);
    private static readonly Binding s_nextX = new("Animation.Next.X", BindingMode.OneWay);
    private static readonly Binding s_nextY = new("Animation.Next.Y", BindingMode.OneWay);

    public VectorAnimationEditor()
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
