using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BEditor.Views
{
    public sealed class Configuration : UserControl
    {
        public Configuration()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}