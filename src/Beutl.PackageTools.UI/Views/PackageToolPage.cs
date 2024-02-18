using Avalonia;
using Avalonia.Controls;

namespace Beutl.PackageTools.UI.Views;

public class PackageToolPage : UserControl
{
    public static readonly StyledProperty<Control?> ButtonsContainerProperty =
            AvaloniaProperty.Register<PackageToolPage, Control?>(nameof(ButtonsContainer));

    public Control? ButtonsContainer
    {
        get => GetValue(ButtonsContainerProperty);
        set => SetValue(ButtonsContainerProperty, value);
    }
}
