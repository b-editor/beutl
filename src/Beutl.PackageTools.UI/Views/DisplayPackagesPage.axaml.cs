using Avalonia.Interactivity;
using Avalonia.VisualTree;

using Beutl.Logging;
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

        // The window opens on this page; coming back to it later starts nothing.
        if (e.NavigationMode == FANavigationMode.New && DataContext is MainViewModel main)
        {
            StartAutomatically(main);
        }
    }

    // Presses Start for the user when Beutl is restarting, once the changes are known. Not when the user
    // has already moved on from this page.
    private async void StartAutomatically(MainViewModel viewModel)
    {
        try
        {
            if (await viewModel.ShouldStartAutomaticallyAsync()
                && this.FindAncestorOfType<FAFrame>() is { CanGoForward: false } frame
                && ReferenceEquals(frame.Content, this))
            {
                object? nextViewModel = viewModel.Next(null, default);
                frame.NavigateFromObject(nextViewModel);
            }
        }
        catch (Exception ex)
        {
            Log.CreateLogger<DisplayPackagesPage>().LogWarning(ex, "Failed to start the package changes for the restart.");
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
