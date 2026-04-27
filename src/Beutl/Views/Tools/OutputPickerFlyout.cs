using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Beutl.Controls.PropertyEditors;
using Beutl.ViewModels.Tools;
using FluentAvalonia.Core;
using FluentAvalonia.UI.Controls.Primitives;

namespace Beutl.Views.Tools;

public sealed class OutputPickerFlyout(OutputPickerViewModel viewModel) : PickerFlyoutBase
{
    public event TypedEventHandler<OutputPickerFlyout, EventArgs>? Confirmed;

    public event TypedEventHandler<OutputPickerFlyout, EventArgs>? Dismissed;

    public event TypedEventHandler<OutputPickerFlyout, PinnableOutputItem>? Pinned;

    public event TypedEventHandler<OutputPickerFlyout, PinnableOutputItem>? Unpinned;

    public event TypedEventHandler<OutputPickerFlyout, MoreMenuRequestedArgs>? MoreMenuRequested;

    public OutputPickerViewModel ViewModel { get; } = viewModel;

    protected override Control CreatePresenter()
    {
        var pfp = new OutputPickerFlyoutPresenter();
        pfp.CloseClicked += OnFlyoutDismissed;
        pfp.Confirmed += OnFlyoutConfirmed;
        pfp.Dismissed += OnFlyoutDismissed;
        pfp.Pinned += item =>
        {
            ViewModel.Pin(item);
            Pinned?.Invoke(this, item);
        };
        pfp.Unpinned += item =>
        {
            ViewModel.Unpin(item);
            Unpinned?.Invoke(this, item);
        };
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

        pfp.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter:
                    OnConfirmed();
                    break;
                case Key.Escape:
                    Dismissed?.Invoke(this, EventArgs.Empty);
                    Hide();
                    break;
            }
        };
        return pfp;
    }

    protected override void OnOpened()
    {
        base.OnOpened();
        if (Popup.Child is OutputPickerFlyoutPresenter pfp)
        {
            pfp.FocusInitialElement();
        }
    }

    private void OnFlyoutDismissed(DraggablePickerFlyoutPresenter sender, object args)
    {
        Dismissed?.Invoke(this, EventArgs.Empty);
        Hide();
    }

    private void OnFlyoutConfirmed(DraggablePickerFlyoutPresenter sender, object args)
    {
        OnConfirmed();
    }

    protected override void OnConfirmed()
    {
        Confirmed?.Invoke(this, EventArgs.Empty);
        Hide();
    }

    protected override void OnOpening(CancelEventArgs args)
    {
        base.OnOpening(args);
        Popup.IsLightDismissEnabled = false;
    }

    protected override bool ShouldShowConfirmationButtons() => true;
}

public sealed record MoreMenuRequestedArgs(PinnableOutputItem Item, Control Anchor);
