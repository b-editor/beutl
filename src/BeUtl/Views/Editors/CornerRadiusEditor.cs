using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

using BeUtl.Controls;
using BeUtl.Media;
using BeUtl.ViewModels.Editors;

namespace BeUtl.Views.Editors;

public sealed class CornerRadiusEditor : BaseVector4Editor<CornerRadius>
{
    public CornerRadiusEditor()
    {
        static FluentIconRegular CreateIcon(FluentIconsRegular icon)
        {
            return new FluentIconRegular
            {
                IconType = icon,
                Width = 16,
                Height = 16,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        // Todo: Bindingをキャッシュする
        xTextBox[!TextBox.TextProperty] = new Binding("Value.Value.TopLeft", BindingMode.OneWay);
        yTextBox[!TextBox.TextProperty] = new Binding("Value.Value.TopRight", BindingMode.OneWay);
        zTextBox[!TextBox.TextProperty] = new Binding("Value.Value.BottomLeft", BindingMode.OneWay);
        wTextBox[!TextBox.TextProperty] = new Binding("Value.Value.BottomRight", BindingMode.OneWay);

        FluentIconRegular topLeftIcon = CreateIcon(FluentIconsRegular.Arrow_Up_Left);
        FluentIconRegular topRightIcon = CreateIcon(FluentIconsRegular.Arrow_Up_Right);
        FluentIconRegular bottomLeftIcon = CreateIcon(FluentIconsRegular.Arrow_Down_Left);
        FluentIconRegular bottomRightIcon = CreateIcon(FluentIconsRegular.Arrow_Down_Left);

        bottomRightIcon.RenderTransform = new ScaleTransform(-1, 1);

        xTextBox.InnerLeftContent = topLeftIcon;
        yTextBox.InnerLeftContent = topRightIcon;
        zTextBox.InnerLeftContent = bottomLeftIcon;
        wTextBox.InnerLeftContent = bottomRightIcon;
    }

    protected override CornerRadius Clamp(CornerRadius value)
    {
        if (DataContext is CornerRadiusEditorViewModel vm)
        {
            CornerRadius min = vm.Minimum;
            CornerRadius max = vm.Maximum;

            return new CornerRadius(Math.Clamp(value.TopLeft, min.TopLeft, max.TopLeft),
                Math.Clamp(value.TopRight, min.TopRight, max.TopRight),
                Math.Clamp(value.BottomRight, min.BottomRight, max.BottomRight),
                Math.Clamp(value.BottomLeft, min.BottomLeft, max.BottomLeft));
        }
        else
        {
            return value;
        }
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
