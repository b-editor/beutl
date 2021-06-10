using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace BEditor.PackageInstaller.Views
{
    public partial class CompletePage : UserControl
    {
        public CompletePage()
        {
            InitializeComponent();
        }

        public void CloseClick(object s, RoutedEventArgs e)
        {
            if (VisualRoot is Window window)
            {
                window.Close();
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}