using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BeUtl.ViewModels.AnimationEditors;

namespace BeUtl.Views.AnimationEditors;

public sealed class SizeAnimationEditor : BaseVector2AnimationEditor<Graphics.Size>
{
    public SizeAnimationEditor()
    {
        var xres = new DynamicResourceExtension("S.Editors.Size.X");
        var yres = new DynamicResourceExtension("S.Editors.Size.Y");
        prevXText[!TextBlock.TextProperty] = xres;
        prevYText[!TextBlock.TextProperty] = yres;
        nextXText[!TextBlock.TextProperty] = xres;
        nextYText[!TextBlock.TextProperty] = yres;

        prevXTextBox[!TextBox.TextProperty] = new Binding("Animation.Previous.Width", BindingMode.OneWay);
        prevYTextBox[!TextBox.TextProperty] = new Binding("Animation.Previous.Height", BindingMode.OneWay);
        nextXTextBox[!TextBox.TextProperty] = new Binding("Animation.Next.Width", BindingMode.OneWay);
        nextYTextBox[!TextBox.TextProperty] = new Binding("Animation.Next.Height", BindingMode.OneWay);
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
