using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.LogicalTree;
using Beutl.Controls.PropertyEditors;
using Beutl.Extensibility;

namespace Beutl.Controls.Converters;

public sealed class PropertyEditorContextToViewConverter(bool hideMenu) : IValueConverter
{
    public static readonly PropertyEditorContextToViewConverter Instance = new(false);
    public static readonly PropertyEditorContextToViewConverter HideMenu = new(true);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is IPropertyEditorContext viewModel)
        {
            if (PropertyEditorHost.TryCreateControl(viewModel, PropertyEditorSurface.Panel, out var control))
            {
                if (hideMenu)
                    HideMenuOf(control);

                if (viewModel is IServiceProvider services
                    && services.GetService(typeof(IPropertyEditorControlHost)) is IPropertyEditorControlHost host)
                    return host.WrapEditor(viewModel, control);

                return control;
            }
            else
            {
                return new Label
                {
                    Height = 24,
                    Margin = new Thickness(0, 4),
                    Content = viewModel.Extension.DisplayName
                };
            }
        }
        else
        {
            return BindingNotification.Null;
        }
    }

    // The row may sit in a host's wrapper, so find it rather than expect the control itself to be the editor.
    private static void HideMenuOf(Control control)
    {
        if (control is PropertyEditor editor)
        {
            editor.MenuContent = null;
            return;
        }

        foreach (ILogical child in control.GetLogicalChildren())
        {
            if (child is Control nested)
                HideMenuOf(nested);
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return BindingNotification.Null;
    }
}
