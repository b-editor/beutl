using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using BEditor.Data.Property;
using BEditor.ViewModels.Properties;

namespace BEditor.Views.Properties
{
    public class FilePropertyView : UserControl
    {
        public FilePropertyView()
        {
            InitializeComponent();
        }
        
        public FilePropertyView(FileProperty property)
        {
            DataContext = new FilePropertyViewModel(property);
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
