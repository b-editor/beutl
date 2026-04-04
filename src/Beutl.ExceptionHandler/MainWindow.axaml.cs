using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Beutl.ExceptionHandler;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow() : this(null)
    {
    }

    public MainWindow(string? sessionId)
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel(sessionId);
        DataContext = _viewModel;
    }

    private void OnMoreDetailsButtonClick(object? sender, RoutedEventArgs e)
    {
        FooterHost.IsVisible = !FooterHost.IsVisible;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
