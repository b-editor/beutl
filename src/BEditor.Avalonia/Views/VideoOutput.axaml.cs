using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using BEditor.ViewModels;

namespace BEditor.Views
{
    public class VideoOutput : Window
    {
        public VideoOutput()
        {
            var vm = new VideoOutputViewModel();
            DataContext = vm;
            InitializeComponent();

            vm.Output.Subscribe(Close);
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