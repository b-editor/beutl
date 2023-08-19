using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Beutl.ExceptionHandler;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;

        _viewModel.IsBusy.Subscribe(v =>
        {
            if (v)
            {
                CloseButton.Content = Properties.Resources.Cancel;
            }
            else
            {
                CloseButton.Content = Properties.Resources.Close;
            }
        });
    }

    private void OnMoreDetailsButtonClick(object? sender, RoutedEventArgs e)
    {
        FooterHost.IsVisible = !FooterHost.IsVisible;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy.Value)
        {
            _viewModel.Cancel.Execute();
        }
        else
        {
            Close();
        }
    }
}
