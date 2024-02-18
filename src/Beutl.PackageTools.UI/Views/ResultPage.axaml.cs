using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using Beutl.PackageTools.UI.ViewModels;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;

namespace Beutl.PackageTools.UI.Views;

public partial class ResultPage : PackageToolPage
{
    public ResultPage()
    {
        InitializeComponent();
        AddHandler(Frame.NavigatedToEvent, OnNavigatedTo, RoutingStrategies.Direct);
    }

    private void OnNavigatedTo(object? sender, NavigationEventArgs e)
    {
        if (e.Parameter is ResultViewModel viewModel)
        {
            DataContext = viewModel;
        }
    }

    private void OnBackButtonClick(object? sender, RoutedEventArgs e)
    {
    }

    private void OnCloseButtonClick(object? sender, RoutedEventArgs e)
    {
        this.FindAncestorOfType<Window>()?.Close();
    }

    private void ShowDetailsClick(object? sender, RoutedEventArgs e)
    {
        Frame? frame = this.FindAncestorOfType<Frame>();
        if (frame == null) return;

        if (sender is StyledElement { DataContext: ActionViewModel item })
        {
            frame.NavigateFromObject(item);
        }
        else if (sender is StyledElement { DataContext: CleanViewModel clean })
        {
            frame.NavigateFromObject(clean);
        }
    }
}
