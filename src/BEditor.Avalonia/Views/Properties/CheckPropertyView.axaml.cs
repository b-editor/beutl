using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using BEditor.Data.Property;
using BEditor.ViewModels.Properties;

namespace BEditor.Views.Properties
{
    public class CheckPropertyView : UserControl
    {
        public CheckPropertyView()
        {
            InitializeComponent();
        }

        public CheckPropertyView(CheckProperty property)
        {
            DataContext = new CheckPropertyViewModel(property);
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
