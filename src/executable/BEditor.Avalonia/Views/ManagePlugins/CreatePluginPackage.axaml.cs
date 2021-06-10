using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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