using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BeUtl.ViewModels.Editors;

namespace BeUtl.Views.Editors;

public sealed class SizeEditor : BaseVector2Editor<Graphics.Size>
{
    private static readonly DynamicResourceExtension s_xResource = new("S.Editors.Size.X");
    private static readonly DynamicResourceExtension s_yResource = new("S.Editors.Size.Y");
    private static readonly Binding s_x = new("Value.Value.Width", BindingMode.OneWay);
    private static readonly Binding s_y = new("Value.Value.Height", BindingMode.OneWay);

    public SizeEditor()
    {
        xText[!TextBlock.TextProperty] = s_xResource;
        yText[!TextBlock.TextProperty] = s_yResource;

        xTextBox[!TextBox.TextProperty] = s_x;
        yTextBox[!TextBox.TextProperty] = s_y;
    }

    protected override Graphics.Size Clamp(Graphics.Size value)
    {
        if (DataContext is SizeEditorViewModel vm)
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
