using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BeUtl.ViewModels.AnimationEditors;

namespace BeUtl.Views.AnimationEditors;

public sealed class PointAnimationEditor : BaseVector2AnimationEditor<Graphics.Point>
{
    public PointAnimationEditor()
    {
        var xres = new DynamicResourceExtension("S.Editors.Point.X");
        var yres = new DynamicResourceExtension("S.Editors.Point.Y");
        prevXText[!TextBlock.TextProperty] = xres;
        prevYText[!TextBlock.TextProperty] = yres;
        nextXText[!TextBlock.TextProperty] = xres;
        nextYText[!TextBlock.TextProperty] = yres;

        // Todo: Bindingをキャッシュする
        prevXTextBox[!TextBox.TextProperty] = new Binding("Animation.Previous.X", BindingMode.OneWay);
        prevYTextBox[!TextBox.TextProperty] = new Binding("Animation.Previous.Y", BindingMode.OneWay);
        nextXTextBox[!TextBox.TextProperty] = new Binding("Animation.Next.X", BindingMode.OneWay);
        nextYTextBox[!TextBox.TextProperty] = new Binding("Animation.Next.Y", BindingMode.OneWay);
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
