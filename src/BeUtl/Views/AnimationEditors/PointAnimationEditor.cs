using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BeUtl.ViewModels.AnimationEditors;

namespace BeUtl.Views.AnimationEditors;

public sealed class PointAnimationEditor : BaseVector2AnimationEditor<Graphics.Point>
{
    private static readonly DynamicResourceExtension s_xResource = new("S.Editors.Point.X");
    private static readonly DynamicResourceExtension s_yResource = new("S.Editors.Point.Y");
    private static readonly Binding s_prevX = new("Animation.Previous.X", BindingMode.OneWay);
    private static readonly Binding s_prevY = new("Animation.Previous.Y", BindingMode.OneWay);
    private static readonly Binding s_nextX = new("Animation.Next.X", BindingMode.OneWay);
    private static readonly Binding s_nextY = new("Animation.Next.Y", BindingMode.OneWay);
    public PointAnimationEditor()
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

    protected override Graphics.Point Clamp(Graphics.Point value)
    {
        if (DataContext is PointAnimationEditorViewModel vm)
        {
            return new Graphics.Point(
                Math.Clamp(value.X, vm.Minimum.X, vm.Maximum.X),
                Math.Clamp(value.Y, vm.Minimum.Y, vm.Maximum.Y));
        }
        else
        {
            return value;
        }
    }

    protected override Graphics.Point IncrementX(Graphics.Point value, int increment)
    {
        return value.WithX(value.X + increment);
    }

    protected override Graphics.Point IncrementY(Graphics.Point value, int increment)
    {
        return value.WithY(value.Y + increment);
    }

    protected override bool TryParse(string? x, string? y, out Graphics.Point value)
    {
        if (float.TryParse(x, out float xi) && float.TryParse(y, out float yi))
        {
            value = new Graphics.Point(xi, yi);
            return true;
        }
        else
        {
            value = default;
            return false;
        }
    }
}
