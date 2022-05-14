using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using BeUtl.Controls;
using BeUtl.ViewModels.Editors;

namespace BeUtl.Views.Editors;

public sealed class ThicknessEditor : BaseVector4Editor<Graphics.Thickness>
{
    public ThicknessEditor()
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

        // Todo: Bindingをキャッシュする
        xTextBox[!TextBox.TextProperty] = new Binding("Value.Value.Left", BindingMode.OneWay);
        yTextBox[!TextBox.TextProperty] = new Binding("Value.Value.Top", BindingMode.OneWay);
        zTextBox[!TextBox.TextProperty] = new Binding("Value.Value.Right", BindingMode.OneWay);
        wTextBox[!TextBox.TextProperty] = new Binding("Value.Value.Bottom", BindingMode.OneWay);
        xTextBox.InnerLeftContent = CreateIcon(FluentIconsRegular.Arrow_Left);
        yTextBox.InnerLeftContent = CreateIcon(FluentIconsRegular.Arrow_Up);
        zTextBox.InnerLeftContent = CreateIcon(FluentIconsRegular.Arrow_Right);
        wTextBox.InnerLeftContent = CreateIcon(FluentIconsRegular.Arrow_Down);
    }

    protected override Graphics.Thickness Clamp(Graphics.Thickness value)
    {
        if (DataContext is ThicknessEditorViewModel vm)
        {
            return new Graphics.Thickness(
                Math.Clamp(value.Left, vm.Minimum.Left, vm.Maximum.Left),
                Math.Clamp(value.Top, vm.Minimum.Top, vm.Maximum.Top),
                Math.Clamp(value.Right, vm.Minimum.Right, vm.Maximum.Right),
                Math.Clamp(value.Bottom, vm.Minimum.Bottom, vm.Maximum.Bottom));
        }
        else
        {
            return value;
        }
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
