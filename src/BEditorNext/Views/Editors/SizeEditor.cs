using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BEditorNext.ViewModels.Editors;

namespace BEditorNext.Views.Editors;

public sealed class SizeEditor : BaseVector2Editor<Graphics.Size>
{
    public SizeEditor()
    {
        xText[!TextBlock.TextProperty] = new DynamicResourceExtension("WidthString");
        yText[!TextBlock.TextProperty] = new DynamicResourceExtension("HeightString");
        xTextBox[!TextBox.TextProperty] = new Binding("Value.Value.Width", BindingMode.OneWay);
        yTextBox[!TextBox.TextProperty] = new Binding("Value.Value.Height", BindingMode.OneWay);
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
