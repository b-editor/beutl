using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BeUtl.ViewModels.AnimationEditors;

namespace BeUtl.Views.AnimationEditors;

public sealed class PixelPointAnimationEditor : BaseVector2AnimationEditor<Media.PixelPoint>
{
    private static readonly DynamicResourceExtension s_xResource = new("S.Editors.PixelPoint.X");
    private static readonly DynamicResourceExtension s_yResource = new("S.Editors.PixelPoint.Y");
    private static readonly Binding s_prevX = new("Animation.Previous.X", BindingMode.OneWay);
    private static readonly Binding s_prevY = new("Animation.Previous.Y", BindingMode.OneWay);
    private static readonly Binding s_nextX = new("Animation.Next.X", BindingMode.OneWay);
    private static readonly Binding s_nextY = new("Animation.Next.Y", BindingMode.OneWay);

    public PixelPointAnimationEditor()
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

    protected override Media.PixelPoint Clamp(Media.PixelPoint value)
    {
        if (DataContext is PixelPointAnimationEditorViewModel vm)
        {
            return new Media.PixelPoint(
                Math.Clamp(value.X, vm.Minimum.X, vm.Maximum.X),
                Math.Clamp(value.Y, vm.Minimum.Y, vm.Maximum.Y));
        }
        else
        {
            return value;
        }
    }

    protected override Media.PixelPoint IncrementX(Media.PixelPoint value, int increment)
    {
        return value.WithX(value.X + increment);
    }

    protected override Media.PixelPoint IncrementY(Media.PixelPoint value, int increment)
    {
        return value.WithY(value.Y + increment);
    }

    protected override bool TryParse(string? x, string? y, out Media.PixelPoint value)
    {
        if (int.TryParse(x, out int xi) && int.TryParse(y, out int yi))
        {
            value = new Media.PixelPoint(xi, yi);
            return true;
        }
        else
        {
            value = default;
            return false;
        }
    }
}
