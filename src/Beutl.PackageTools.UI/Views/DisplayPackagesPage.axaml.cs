using Avalonia.Interactivity;
using Avalonia.VisualTree;

using Beutl.PackageTools.UI.ViewModels;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;

namespace Beutl.PackageTools.UI.Views;

public partial class DisplayPackagesPage : PackageToolPage
{
    public DisplayPackagesPage()
    {
        InitializeComponent();
        AddHandler(FAFrame.NavigatedToEvent, OnNavigatedTo, RoutingStrategies.Direct);
    }

    private void OnNavigatedTo(object? sender, FANavigationEventArgs e)
    {
        if (e.Parameter is MainViewModel viewModel)
        {
            DataContext = viewModel;
        }
    }

    private void OnNextClick(object? sender, RoutedEventArgs e)
    {
        FAFrame? frame = this.FindAncestorOfType<FAFrame>();
        if (frame == null) return;

        if (frame.CanGoForward)
        {
            frame.GoForward();
            return;
        }

        if (DataContext is MainViewModel viewModel)
        {
            object? nextViewModel = viewModel.Next(null, default);
            frame.NavigateFromObject(nextViewModel);
        }
    }
}
