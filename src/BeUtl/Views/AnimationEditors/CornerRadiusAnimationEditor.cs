using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

using BeUtl.Controls;
using BeUtl.Media;
using BeUtl.ViewModels.AnimationEditors;

namespace BeUtl.Views.AnimationEditors;

public sealed class CornerRadiusAnimationEditor : BaseVector4AnimationEditor<CornerRadius>
{
    public CornerRadiusAnimationEditor()
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

        FluentIconRegular bottomRightIcon1 = CreateIcon(FluentIconsRegular.Arrow_Down_Left);
        FluentIconRegular bottomRightIcon2 = CreateIcon(FluentIconsRegular.Arrow_Down_Left);

        bottomRightIcon1.RenderTransform = new ScaleTransform(-1, 1);
        bottomRightIcon2.RenderTransform = new ScaleTransform(-1, 1);

        prevXTextBox.InnerLeftContent = CreateIcon(FluentIconsRegular.Arrow_Up_Left);
        prevYTextBox.InnerLeftContent = CreateIcon(FluentIconsRegular.Arrow_Up_Right);
        prevZTextBox.InnerLeftContent = CreateIcon(FluentIconsRegular.Arrow_Down_Left);
        prevWTextBox.InnerLeftContent = bottomRightIcon1;
        nextXTextBox.InnerLeftContent = CreateIcon(FluentIconsRegular.Arrow_Up_Left);
        nextYTextBox.InnerLeftContent = CreateIcon(FluentIconsRegular.Arrow_Up_Right);
        nextZTextBox.InnerLeftContent = CreateIcon(FluentIconsRegular.Arrow_Down_Left);
        nextWTextBox.InnerLeftContent = bottomRightIcon2;

        // Todo: Bindingをキャッシュする
        prevXTextBox[!TextBox.TextProperty] = new Binding("Animation.Previous.TopLeft", BindingMode.OneWay);
        prevYTextBox[!TextBox.TextProperty] = new Binding("Animation.Previous.TopRight", BindingMode.OneWay);
        prevZTextBox[!TextBox.TextProperty] = new Binding("Animation.Previous.BottomLeft", BindingMode.OneWay);
        prevWTextBox[!TextBox.TextProperty] = new Binding("Animation.Previous.BottomRight", BindingMode.OneWay);
        nextXTextBox[!TextBox.TextProperty] = new Binding("Animation.Next.TopLeft", BindingMode.OneWay);
        nextYTextBox[!TextBox.TextProperty] = new Binding("Animation.Next.TopRight", BindingMode.OneWay);
        nextZTextBox[!TextBox.TextProperty] = new Binding("Animation.Next.BottomLeft", BindingMode.OneWay);
        nextWTextBox[!TextBox.TextProperty] = new Binding("Animation.Next.BottomRight", BindingMode.OneWay);
    }

    protected override CornerRadius Clamp(CornerRadius value)
    {
        if (DataContext is CornerRadiusAnimationEditorViewModel vm)
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
