using System.ComponentModel;
using System.Reflection;

using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml.MarkupExtensions;

using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

public partial class EnumEditor : UserControl
{
    public EnumEditor()
    {
        InitializeComponent();
    }
}

public sealed class EnumEditor<T> : EnumEditor
    where T : struct, Enum
{
    public EnumEditor()
    {
        Resources["EnumToTextBlockConverter"] = EnumToTextBlockConverter.Instance;
        comboBox.Items = Enum.GetValues<T>();
        comboBox.SelectionChanged += ComboBox_SelectionChanged;
    }

    private void ComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is EnumEditorViewModel<T> vm && comboBox.SelectedItem is T item)
        {
            vm.SetValue(vm.WrappedProperty.GetValue(), item);
        }
    }

    private sealed class EnumToTextBlockConverter : IValueConverter
    {
        public static readonly EnumToTextBlockConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is Enum e)
            {
                // Todo: Enumのローカライズ
                DescriptionAttribute? name = GetAttrubute<DescriptionAttribute>(e);
                if (name != null)
                {
                    return new TextBlock
                    {
                        Text = name.Description
                    };
                }
                else
                {
                    return new TextBlock
                    {
                        Text = e.ToString("g")
                    };
                }
            }

            return BindingNotification.Null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return BindingNotification.Null;
        }

        private static TAtt? GetAttrubute<TAtt>(Enum e)
            where TAtt : Attribute
        {
            FieldInfo? field = e.GetType().GetField(e.ToString());
            if (field?.GetCustomAttribute<TAtt>() is TAtt att)
            {
                return att;
            }

            return null;
        }
    }
}
