using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BEditor.Views
{
    public class VideoOutput : Window
    {
        public VideoOutput()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
