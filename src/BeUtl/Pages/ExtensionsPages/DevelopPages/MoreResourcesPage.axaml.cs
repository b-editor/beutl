using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

using BeUtl.ViewModels.ExtensionsPages.DevelopPages;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media.Animation;
using FluentAvalonia.UI.Navigation;

namespace BeUtl.Pages.ExtensionsPages.DevelopPages;
public partial class MoreResourcesPage : UserControl
{
    public MoreResourcesPage()
    {
        InitializeComponent();
    }

    private void NavigatePackagePage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MoreResourcesPageViewModel viewModel)
        {
            Frame frame = this.FindAncestorOfType<Frame>();
            var transitionInfo = new EntranceNavigationTransitionInfo
            {
                FromHorizontalOffset = -28,
                FromVerticalOffset = 0
            };
            var options = new FrameNavigationOptions()
            {
                TransitionInfoOverride = transitionInfo,
                IsNavigationStackEnabled = false
            };

            frame.NavigateToType(typeof(PackagePage), viewModel._viewModel, options);
        }
    }
}
