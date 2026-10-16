using Avalonia;
using Avalonia.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.ViewModels.Tools;
using FluentAvalonia.Core;

namespace Beutl.Views.Tools;

internal sealed class OutputPickerFlyout(OutputPickerViewModel viewModel)
    : ConfirmablePickerFlyout<OutputPickerFlyout, OutputPickerFlyoutPresenter>
{
    public event TypedEventHandler<OutputPickerFlyout, MoreMenuRequestedArgs>? MoreMenuRequested;

    public OutputPickerViewModel ViewModel { get; } = viewModel;

    protected override OutputPickerFlyoutPresenter CreatePickerPresenter()
    {
        var pfp = new OutputPickerFlyoutPresenter();
        pfp.Pinned += item => ViewModel.Pin(item);
        pfp.Unpinned += item => ViewModel.Unpin(item);
        pfp.MoreMenuRequested += (item, anchor) =>
            MoreMenuRequested?.Invoke(this, new MoreMenuRequestedArgs(item, anchor));

        pfp.ProfileItems = ViewModel.ProfileItems;
        pfp.PresetItems = ViewModel.PresetItems;
        pfp.ShowPresets = ViewModel.ShowPresets.Value;

        pfp.GetObservable(OutputPickerFlyoutPresenter.SelectedProfileProperty)
            .Subscribe(v => ViewModel.SelectedProfile.Value = v);
        pfp.GetObservable(OutputPickerFlyoutPresenter.SelectedPresetProperty)
            .Subscribe(v => ViewModel.SelectedPreset.Value = v);
        pfp.GetObservable(OutputPickerFlyoutPresenter.SearchTextProperty)
            .Subscribe(v => ViewModel.SearchText.Value = v);
        pfp.GetObservable(OutputPickerFlyoutPresenter.ShowPresetsProperty)
            .Subscribe(v => ViewModel.ShowPresets.Value = v);
        return pfp;
    }
}

internal sealed record MoreMenuRequestedArgs(PinnableLibraryItem Item, Control Anchor);
