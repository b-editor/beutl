using System;

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
        private readonly Image _image;

        public ZoomWindow()
        {
            DataContext = MainWindowViewModel.Current.Previewer;
            InitializeComponent();
            _zoomBorder = this.FindControl<ZoomBorder>("ZoomBorder");
            _image = this.FindControl<Image>("image");
            MainWindowViewModel.Current.Previewer.ImageChanged += Previewer_ImageChanged;
#if DEBUG
            this.AttachDevTools();
#endif
        }

        private void Previewer_ImageChanged(object? sender, EventArgs e)
        {
            _image.InvalidateVisual();
        }

        public void Reset(object s, RoutedEventArgs e)
        {
            _zoomBorder.ResetMatrix();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            MainWindowViewModel.Current.Previewer.ImageChanged -= Previewer_ImageChanged;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}