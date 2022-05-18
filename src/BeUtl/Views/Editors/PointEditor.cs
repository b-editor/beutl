using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BeUtl.ViewModels.Editors;

namespace BeUtl.Views.Editors;

public sealed class PointEditor : BaseVector2Editor<Graphics.Point>
{
    private static readonly DynamicResourceExtension s_xResource = new("S.Editors.Point.X");
    private static readonly DynamicResourceExtension s_yResource = new("S.Editors.Point.Y");
    private static readonly Binding s_x = new("Value.Value.X", BindingMode.OneWay);
    private static readonly Binding s_y = new("Value.Value.Y", BindingMode.OneWay);

    public PointEditor()
    {
        xText[!TextBlock.TextProperty] = s_xResource;
        yText[!TextBlock.TextProperty] = s_yResource;

        xTextBox[!TextBox.TextProperty] = s_x;
        yTextBox[!TextBox.TextProperty] = s_y;
    }

    protected override Graphics.Point Clamp(Graphics.Point value)
    {
        if (DataContext is PointEditorViewModel vm)
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
