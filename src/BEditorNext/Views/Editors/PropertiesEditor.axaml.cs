using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BEditorNext.ProjectSystem;
using BEditorNext.Services;

namespace BEditorNext.Views.Editors;

public partial class PropertiesEditor : UserControl
{
    public PropertiesEditor()
    {
        Resources["OperationDisplayNameConverter"] = OperationDisplayNameConverter.Instance;
        Resources["ModelToViewConverter"] = ModelToViewConverter.Instance;
        InitializeComponent();
    }

    private sealed class OperationDisplayNameConverter : IValueConverter
    {
        public static readonly OperationDisplayNameConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is RenderOperation operation)
            {
                Type type = operation.GetType();
                RenderOperationRegistry.RegistryItem? item = RenderOperationRegistry.FindItem(type);

                if (item == null)
                    goto ReturnNull;

                return new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new CheckBox
                        {
                            [!ToggleButton.IsCheckedProperty] = new Binding("IsEnabled"),
                            [!ContentProperty] = new DynamicResourceExtension(item.DisplayName.Key)
                        }
                    }
                };
            }

        ReturnNull:
            return BindingNotification.Null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return BindingNotification.Null;
        }
    }

    private sealed class ModelToViewConverter : IValueConverter
    {
        public static readonly ModelToViewConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ISetter setter)
            {
                Control? editor = PropertyEditorService.CreateEditor(setter);

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
