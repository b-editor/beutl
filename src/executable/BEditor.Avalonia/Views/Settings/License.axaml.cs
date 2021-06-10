using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BEditor.Views.Settings
{
    public sealed class License : UserControl
    {
        public License()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}