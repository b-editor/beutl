using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using BEditor.ViewModels;

namespace BEditor.Views
{
    public partial class ZoomWindow : FluentWindow
    {
        private readonly ZoomBorder _zoomBorder;

        public ZoomWindow()
        {
            DataContext = MainWindowViewModel.Current.Previewer;
            InitializeComponent();
            _zoomBorder = this.FindControl<ZoomBorder>("ZoomBorder");
#if DEBUG
            this.AttachDevTools();
#endif
        }

        public void Reset(object s, RoutedEventArgs e)
        {
            _zoomBorder.ResetMatrix();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}