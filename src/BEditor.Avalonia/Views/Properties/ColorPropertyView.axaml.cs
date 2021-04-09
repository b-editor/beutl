using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using BEditor.Data.Property;
using BEditor.ViewModels.Properties;

namespace BEditor.Views.Properties
{
    public class ColorPropertyView : UserControl
    {
        public ColorPropertyView()
        {
            InitializeComponent();
        }

        public ColorPropertyView(ColorProperty property)
        {
            DataContext = new ColorPropertyViewModel(property);
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
