using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BeUtl.Services;
using BeUtl.ViewModels.Editors;

namespace BeUtl.Views.Editors;

public partial class ObjectPropertyEditor : UserControl
{
    public ObjectPropertyEditor()
    {
        Resources["ViewModelToViewConverter"] = ViewModelToViewConverter.Instance;
        InitializeComponent();
    }

    private sealed class ViewModelToViewConverter : IValueConverter
    {
        public static readonly ViewModelToViewConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is BaseEditorViewModel viewModel)
            {
                Control? editor = PropertyEditorService.CreateEditor(viewModel.WrappedProperty);

                return editor ?? new Label
                {
                    Height = 24,
                    Margin = new Thickness(0, 4),
                    Content = viewModel.WrappedProperty.AssociatedProperty.Name
                };
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
