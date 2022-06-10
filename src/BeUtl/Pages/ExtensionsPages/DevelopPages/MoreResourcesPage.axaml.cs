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
    private MoreResourcesPageViewModel? _viewModel;

    public MoreResourcesPage()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MoreResourcesPageViewModel viewModel)
        {
            _viewModel?.Dispose();
            viewModel.Initialize();
            _viewModel = viewModel;
        }
    }

    protected override void OnDetachedFromLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromLogicalTree(e);

        _viewModel?.Dispose();
        DataContext = _viewModel = null;
    }

    private async void Add_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MoreResourcesPageViewModel viewModel) return;
        AddDialog_DisplayName.Text = "";
        AddDialog_Description.Text = "";
        AddDialog_Culture.Items = viewModel.GetCultures().ToArray();
        AddDialog_Culture.SelectedIndex = 0;

        if (await AddDialog.ShowAsync() == ContentDialogResult.Primary)
        {

        }
    }

    private void Edit_Click(object? sender, RoutedEventArgs e)
    {

    }

    private void Delete_Click(object? sender, RoutedEventArgs e)
    {

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

            frame.Navigate(typeof(PackagePage), viewModel._viewModel, transitionInfo);
        }
    }
}
