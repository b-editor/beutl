using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Editor.Components.VersionControl.ViewModels;
using Beutl.Editor.Components.VersionControl.Views;
using Beutl.Language;

namespace Beutl.Views;

public sealed partial class TitleBarBranchView : UserControl
{
    internal VersionControlPickerFlyout PromptFlyout { get; } = new();

    public TitleBarBranchView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is TitleBarBranchViewModel viewModel)
        {
            viewModel.RequestNewBranchNameAsync = ShowNewBranchFlyoutAsync;
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

    private Task<string?> ShowNewBranchFlyoutAsync()
    {
        TitleBarBranchButton.Flyout?.Hide();
        return PromptFlyout.ShowTextInputAsync(
            TitleBarBranchButton,
            Strings.VersionControl_NewBranch,
            Strings.VersionControl_BranchName,
            initialText: null);
    }
}
