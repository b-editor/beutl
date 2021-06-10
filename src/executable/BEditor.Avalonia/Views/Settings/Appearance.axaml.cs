using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BEditor.Views.Settings
{
    public sealed class Appearance : UserControl
    {
        public Appearance()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}