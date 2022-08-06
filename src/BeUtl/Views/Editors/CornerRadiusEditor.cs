using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

using BeUtl.Media;

using FluentIcons.Avalonia;
using FluentIcons.Common;

namespace BeUtl.Views.Editors;

public sealed class CornerRadiusEditor : BaseVector4Editor<CornerRadius>
{
    private static readonly Binding s_topLeft = new("Value.Value.TopLeft", BindingMode.OneWay);
    private static readonly Binding s_topRight = new("Value.Value.TopRight", BindingMode.OneWay);
    private static readonly Binding s_bottomLeft = new("Value.Value.BottomLeft", BindingMode.OneWay);
    private static readonly Binding s_bottomRight = new("Value.Value.BottomRight", BindingMode.OneWay);

    public CornerRadiusEditor()
    {
        static SymbolIcon CreateIcon(Symbol icon)
        {
            return new SymbolIcon
            {
                Symbol = icon,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        xTextBox[!TextBox.TextProperty] = s_topLeft;
        yTextBox[!TextBox.TextProperty] = s_topRight;
        zTextBox[!TextBox.TextProperty] = s_bottomLeft;
        wTextBox[!TextBox.TextProperty] = s_bottomRight;

        SymbolIcon topLeftIcon = CreateIcon(Symbol.ArrowUpLeft);
        SymbolIcon topRightIcon = CreateIcon(Symbol.ArrowUpRight);
        SymbolIcon bottomLeftIcon = CreateIcon(Symbol.ArrowDownLeft);
        SymbolIcon bottomRightIcon = CreateIcon(Symbol.ArrowDownLeft);

        bottomRightIcon.RenderTransform = new ScaleTransform(-1, 1);

        xTextBox.InnerLeftContent = topLeftIcon;
        yTextBox.InnerLeftContent = topRightIcon;
        zTextBox.InnerLeftContent = bottomLeftIcon;
        wTextBox.InnerLeftContent = bottomRightIcon;
    }

    protected override CornerRadius IncrementX(CornerRadius value, int increment)
    {
        return value.WithTopLeft(value.TopLeft + increment);
    }

    protected override CornerRadius IncrementY(CornerRadius value, int increment)
    {
        return value.WithTopRight(value.TopRight + increment);
    }

    protected override CornerRadius IncrementZ(CornerRadius value, int increment)
    {
        return value.WithBottomLeft(value.BottomLeft + increment);
    }

    protected override CornerRadius IncrementW(CornerRadius value, int increment)
    {
        return value.WithBottomRight(value.BottomRight + increment);
    }

    protected override bool TryParse(string? x, string? y, string? z, string? w, out CornerRadius value)
    {
        if (float.TryParse(x, out float xi) &&
            float.TryParse(y, out float yi) &&
            float.TryParse(z, out float zi) &&
            float.TryParse(w, out float wi))
        {
            value = new CornerRadius(xi, yi, wi, zi);
            return true;
        }
        else
        {
            value = default;
            return false;
        }
    }
}
