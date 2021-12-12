using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Data;
using Avalonia.Data.Converters;

using BEditorNext.ProjectSystem;
using BEditorNext.Services;

namespace BEditorNext.Views.Editors;
public partial class PropertiesEditor : UserControl
{
    public PropertiesEditor()
    {
        Resources["ModelToViewConverter"] = new ModelToViewConverter();
        InitializeComponent();
    }

    private sealed class ModelToViewConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ISetter setter)
            {
                object? editor = PropertyEditorService.CreateEditor(setter);

                return editor ?? new Label
                {
                    Height = 24,
                    Margin = new Thickness(0, 4),
                    Content = setter.Property.Name
                };
            }
            else
            {
                return BindingNotification.Null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return BindingNotification.Null;
        }
    }
}
