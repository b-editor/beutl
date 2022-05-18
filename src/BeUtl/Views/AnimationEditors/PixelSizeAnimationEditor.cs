using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BeUtl.ViewModels.AnimationEditors;

namespace BeUtl.Views.AnimationEditors;

public sealed class PixelSizeAnimationEditor : BaseVector2AnimationEditor<Media.PixelSize>
{
    private static readonly DynamicResourceExtension s_xResource = new("S.Editors.PixelSize.X");
    private static readonly DynamicResourceExtension s_yResource = new("S.Editors.PixelSize.Y");
    private static readonly Binding s_prevX = new("Animation.Previous.Width", BindingMode.OneWay);
    private static readonly Binding s_prevY = new("Animation.Previous.Width", BindingMode.OneWay);
    private static readonly Binding s_nextX = new("Animation.Next.Width", BindingMode.OneWay);
    private static readonly Binding s_nextY = new("Animation.Next.Height", BindingMode.OneWay);

    public PixelSizeAnimationEditor()
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

    protected override Media.PixelSize Clamp(Media.PixelSize value)
    {
        if (DataContext is PixelSizeAnimationEditorViewModel vm)
        {
            return new Media.PixelSize(
                Math.Clamp(value.Width, vm.Minimum.Width, vm.Maximum.Width),
                Math.Clamp(value.Height, vm.Minimum.Height, vm.Maximum.Height));
        }
        else
        {
            return value;
        }
    }

    protected override Media.PixelSize IncrementX(Media.PixelSize value, int increment)
    {
        return value.WithWidth(value.Width + increment);
    }

    protected override Media.PixelSize IncrementY(Media.PixelSize value, int increment)
    {
        return value.WithHeight(value.Height + increment);
    }

    protected override bool TryParse(string? x, string? y, out Media.PixelSize value)
    {
        if (int.TryParse(x, out int xi) && int.TryParse(y, out int yi))
        {
            value = new Media.PixelSize(xi, yi);
            return true;
        }
        else
        {
            value = default;
            return false;
        }
    }
}
