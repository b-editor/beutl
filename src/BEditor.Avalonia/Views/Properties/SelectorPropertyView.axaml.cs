using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using BEditor.Data.Property;
using BEditor.ViewModels.Properties;

namespace BEditor.Views.Properties
{
    public class SelectorPropertyView : UserControl
    {
        public SelectorPropertyView()
        {
            InitializeComponent();
        }
        
        public SelectorPropertyView(SelectorProperty property)
        {
            DataContext = new SelectorPropertyViewModel(property);
            InitializeComponent();
        }

        public void ComboBox_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if(DataContext is SelectorPropertyViewModel vm)
            {
                vm.Command.Execute(((ComboBox)s).SelectedIndex);
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
