using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;

using BEditorNext.Controls;
using BEditorNext.Graphics;
using BEditorNext.ViewModels.AnimationEditors;

namespace BEditorNext.Views.AnimationEditors;

public sealed class ThicknessAnimationEditor : BaseVector4AnimationEditor<Thickness>
{
    public ThicknessAnimationEditor()
    {
        static object CreateIcon(FluentIconsRegular icon)
        {
            return new FluentIconRegular
            {
                IconType = icon,
                Width = 16,
                Height = 16,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        prevXTextBox.InnerLeftContent = CreateIcon(FluentIconsRegular.Arrow_Left);
        prevYTextBox.InnerLeftContent = CreateIcon(FluentIconsRegular.Arrow_Up);
        prevZTextBox.InnerLeftContent = CreateIcon(FluentIconsRegular.Arrow_Right);
        prevWTextBox.InnerLeftContent = CreateIcon(FluentIconsRegular.Arrow_Down);
        nextXTextBox.InnerLeftContent = CreateIcon(FluentIconsRegular.Arrow_Left);
        nextYTextBox.InnerLeftContent = CreateIcon(FluentIconsRegular.Arrow_Up);
        nextZTextBox.InnerLeftContent = CreateIcon(FluentIconsRegular.Arrow_Right);
        nextWTextBox.InnerLeftContent = CreateIcon(FluentIconsRegular.Arrow_Down);

        prevXTextBox[!TextBox.TextProperty] = new Binding("Animation.Previous.Left", BindingMode.OneWay);
        prevYTextBox[!TextBox.TextProperty] = new Binding("Animation.Previous.Top", BindingMode.OneWay);
        prevZTextBox[!TextBox.TextProperty] = new Binding("Animation.Previous.Right", BindingMode.OneWay);
        prevWTextBox[!TextBox.TextProperty] = new Binding("Animation.Previous.Bottom", BindingMode.OneWay);
        nextXTextBox[!TextBox.TextProperty] = new Binding("Animation.Next.Left", BindingMode.OneWay);
        nextYTextBox[!TextBox.TextProperty] = new Binding("Animation.Next.Top", BindingMode.OneWay);
        nextZTextBox[!TextBox.TextProperty] = new Binding("Animation.Next.Right", BindingMode.OneWay);
        nextWTextBox[!TextBox.TextProperty] = new Binding("Animation.Next.Bottom", BindingMode.OneWay);
    }

    protected override Thickness Clamp(Thickness value)
    {
        if (DataContext is ThicknessAnimationEditorViewModel vm)
        {
            Thickness min = vm.Minimum;
            Thickness max = vm.Maximum;

            return new Thickness(
                Math.Clamp(value.Left, min.Left, max.Left),
                Math.Clamp(value.Top, min.Top, max.Top),
                Math.Clamp(value.Right, min.Right, max.Right),
                Math.Clamp(value.Bottom, min.Bottom, max.Bottom));
        }
        else
        {
            return value;
        }
    }

    protected override Thickness IncrementX(Thickness value, int increment)
    {
        return value.WithLeft(value.Left + increment);
    }

    protected override Thickness IncrementY(Thickness value, int increment)
    {
        return value.WithTop(value.Top + increment);
    }

    protected override Thickness IncrementZ(Thickness value, int increment)
    {
        return value.WithRight(value.Right + increment);
    }

    protected override Thickness IncrementW(Thickness value, int increment)
    {
        return value.WithBottom(value.Bottom + increment);
    }

    protected override bool TryParse(string? x, string? y, string? z, string? w, out Thickness value)
    {
        if (float.TryParse(x, out float xi) &&
            float.TryParse(y, out float yi) &&
            float.TryParse(z, out float zi) &&
            float.TryParse(w, out float wi))
        {
            value = new Thickness(xi, yi, zi, wi);
            return true;
        }
        else
        {
            value = default;
            return false;
        }
    }
}
