using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BEditor.Views.ManagePlugins
{
    public partial class CreatePluginPackage : UserControl
    {
        public CreatePluginPackage()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
