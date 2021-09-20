using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BEditor.Views.VideoOutputPages
{
    public sealed class Output : UserControl
    {
        public Output()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}