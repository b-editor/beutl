using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using BEditor.Data.Property;
using BEditor.ViewModels.Properties;

namespace BEditor.Views.Properties
{
    public class FolderPropertyView : UserControl
    {
        public FolderPropertyView()
        {
            InitializeComponent();
        }

        public FolderPropertyView(FolderProperty property)
        {
            DataContext = new FolderPropertyViewModel(property);
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}