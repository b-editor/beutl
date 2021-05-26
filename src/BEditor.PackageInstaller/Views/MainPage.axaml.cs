using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using BEditor.PackageInstaller.ViewModels;

namespace BEditor.PackageInstaller.Views
{
    public partial class MainPage : UserControl
    {
        public MainPage()
        {
            InitializeComponent();
        }

        public void CancelClick(object s, RoutedEventArgs e)
        {
            if (Parent is Window window)
            {
                window.Close();
            }
        }

        public void ModifyClick(object s, RoutedEventArgs e)
        {
            if (Parent is Window window && DataContext is MainPageViewModel viewModel)
            {
                window.Content = new ModifyPage
                {
                    DataContext = new ModifyPageViewModel(viewModel.Updates.Concat(viewModel.Installs).Concat(viewModel.Uninstalls))
                };
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}