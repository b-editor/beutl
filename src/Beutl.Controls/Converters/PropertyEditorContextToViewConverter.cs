#nullable enable

using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;

using Beutl.Extensibility;

namespace Beutl.Controls.Converters;

public sealed class PropertyEditorContextToViewConverter : IValueConverter
{
    public static readonly PropertyEditorContextToViewConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is IPropertyEditorContext viewModel)
        {
            if (viewModel.Extension.TryCreateControl(viewModel, out var control))
            {
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

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return BindingNotification.Null;
    }
}
