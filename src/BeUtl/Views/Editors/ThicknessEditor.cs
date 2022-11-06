using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;

using Beutl.Controls;

using FluentIcons.Common;
using FluentIcons.FluentAvalonia;

namespace Beutl.Views.Editors;

public sealed class ThicknessEditor : BaseVector4Editor<Graphics.Thickness>
{
    private static readonly Binding s_x = new("Value.Value.Left", BindingMode.OneWay);
    private static readonly Binding s_y = new("Value.Value.Top", BindingMode.OneWay);
    private static readonly Binding s_z = new("Value.Value.Right", BindingMode.OneWay);
    private static readonly Binding s_w = new("Value.Value.Bottom", BindingMode.OneWay);

    public ThicknessEditor()
    {
        static object CreateIcon(Symbol icon)
        {
            return new SymbolIcon
            {
                Symbol = icon,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        xTextBox[!TextBox.TextProperty] = s_x;
        yTextBox[!TextBox.TextProperty] = s_y;
        zTextBox[!TextBox.TextProperty] = s_z;
        wTextBox[!TextBox.TextProperty] = s_w;
        xTextBox.InnerLeftContent = CreateIcon(Symbol.ArrowLeft);
        yTextBox.InnerLeftContent = CreateIcon(Symbol.ArrowUp);
        zTextBox.InnerLeftContent = CreateIcon(Symbol.ArrowRight);
        wTextBox.InnerLeftContent = CreateIcon(Symbol.ArrowDown);
    }

    protected override Graphics.Thickness IncrementX(Graphics.Thickness value, int increment)
    {
        return value.WithLeft(value.Left + increment);
    }

    protected override Graphics.Thickness IncrementY(Graphics.Thickness value, int increment)
    {
        return value.WithTop(value.Top + increment);
    }

    protected override Graphics.Thickness IncrementZ(Graphics.Thickness value, int increment)
    {
        return value.WithRight(value.Right + increment);
    }

    protected override Graphics.Thickness IncrementW(Graphics.Thickness value, int increment)
    {
        return value.WithBottom(value.Bottom + increment);
    }

    protected override bool TryParse(string? x, string? y, string? z, string? w, out Graphics.Thickness value)
    {
        if (float.TryParse(x, out float xi) &&
            float.TryParse(y, out float yi) &&
            float.TryParse(z, out float zi) &&
            float.TryParse(w, out float wi))
        {
            value = new Graphics.Thickness(xi, yi, zi, wi);
            return true;
        }
        else
        {
            value = default;
            return false;
        }
    }
}
