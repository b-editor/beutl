using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BeUtl.ViewModels.AnimationEditors;

namespace BeUtl.Views.AnimationEditors;

public sealed class SizeAnimationEditor : BaseVector2AnimationEditor<Graphics.Size>
{
    private static readonly DynamicResourceExtension s_xResource = new("S.Editors.Size.X");
    private static readonly DynamicResourceExtension s_yResource = new("S.Editors.Size.Y");
    private static readonly Binding s_prevX = new("Animation.Previous.Width", BindingMode.OneWay);
    private static readonly Binding s_prevY = new("Animation.Previous.Width", BindingMode.OneWay);
    private static readonly Binding s_nextX = new("Animation.Next.Width", BindingMode.OneWay);
    private static readonly Binding s_nextY = new("Animation.Next.Height", BindingMode.OneWay);

    public SizeAnimationEditor()
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

    protected override Graphics.Size Clamp(Graphics.Size value)
    {
        if (DataContext is SizeAnimationEditorViewModel vm)
        {
            return new Graphics.Size(
                Math.Clamp(value.Width, vm.Minimum.Width, vm.Maximum.Width),
                Math.Clamp(value.Height, vm.Minimum.Height, vm.Maximum.Height));
        }
        else
        {
            return value;
        }
    }

    protected override Graphics.Size IncrementX(Graphics.Size value, int increment)
    {
        return value.WithWidth(value.Width + increment);
    }

    protected override Graphics.Size IncrementY(Graphics.Size value, int increment)
    {
        return value.WithHeight(value.Height + increment);
    }

    protected override bool TryParse(string? x, string? y, out Graphics.Size value)
    {
        if (float.TryParse(x, out float xi) && float.TryParse(y, out float yi))
        {
            value = new Graphics.Size(xi, yi);
            return true;
        }
        else
        {
            value = default;
            return false;
        }
    }
}
