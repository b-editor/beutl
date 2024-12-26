using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Beutl.Controls.PropertyEditors;

namespace Beutl.Views.Tools;

public partial class OutputPropertiesEditor : UserControl
{
    public OutputPropertiesEditor()
    {
        Resources["ViewModelToViewConverter"] = ViewModelToViewConverter.Instance;
        InitializeComponent();
    }

    public sealed class ViewModelToViewConverter : IValueConverter
    {
        public static readonly ViewModelToViewConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is IPropertyEditorContext viewModel)
            {
                if (viewModel.Extension.TryCreateControl(viewModel, out var control))
                {
                    if (control is PropertyEditor pe)
                    {
                        pe.MenuContent = null;
                    }

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
}
