using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using BEditor.Controls;

namespace BEditor.Extensions.FFmpeg.Views
{
    public partial class MediaInfo : FluentWindow
    {
        public MediaInfo()
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
