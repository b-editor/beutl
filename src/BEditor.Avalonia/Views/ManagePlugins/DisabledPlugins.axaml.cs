using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BEditor.Views.ManagePlugins
{
    public class DisabledPlugins : UserControl
    {
        public DisabledPlugins()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}