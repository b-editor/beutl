using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using BEditor.Drawing;
using BEditor.ViewModels;

namespace BEditor.Views
{
    public partial class AddToColorPalette : FluentWindow
    {
        public AddToColorPalette()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        public AddToColorPalette(Color color)
        {
            var viewModel = new AddToColorPaletteViewModel(color);
            DataContext = viewModel;
            InitializeComponent();
            viewModel.Close.Subscribe(Close);
            viewModel.ClosePopup.Subscribe(() =>
            {
                this.FindControl<Popup>("NewPalette").Close();
            });
#if DEBUG
            this.AttachDevTools();
#endif
        }

        public void NewPalette_Click(object s, RoutedEventArgs e)
        {
            this.FindControl<Popup>("NewPalette").Open();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
