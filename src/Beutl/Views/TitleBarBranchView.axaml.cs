using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Editor.Components.VersionControl.ViewModels;
using Beutl.Language;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views;

public sealed partial class TitleBarBranchView : UserControl
{
    public TitleBarBranchView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is TitleBarBranchViewModel viewModel)
        {
            viewModel.RequestNewBranchNameAsync = ShowNewBranchDialogAsync;
        }
    }

    private async void OnBranchFlyoutOpening(
        object? sender,
        EventArgs e)
    {
        if (DataContext is TitleBarBranchViewModel viewModel)
        {
            await viewModel.PrepareFlyoutAsync();
        }
    }

    private async void OnBranchClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TitleBarBranchViewModel viewModel
            && sender is Button
            {
                DataContext: TitleBarBranchItemViewModel branch,
            })
        {
            TitleBarBranchButton.Flyout?.Hide();
            await viewModel.SwitchBranchAsync(branch.Name);
        }
    }

    private static async Task<string?> ShowNewBranchDialogAsync()
    {
        var textBox = new TextBox
        {
            Watermark = Strings.VersionControl_BranchName,
        };
        var dialog = new ContentDialog
        {
            Title = Strings.VersionControl_NewBranch,
            Content = textBox,
            PrimaryButtonText = Strings.VersionControl_CreateBranch,
            CloseButtonText = Strings.Cancel,
            DefaultButton = ContentDialogButton.Primary,
        };
        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? textBox.Text : null;
    }
}
