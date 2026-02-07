using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Beutl.Controls.PropertyEditors;
using Beutl.ViewModels.Editors;
using FluentAvalonia.Core;
using FluentAvalonia.UI.Controls.Primitives;

namespace Beutl.Views.Editors;

public sealed class TargetPickerFlyout(TargetPickerFlyoutViewModel viewModel) : PickerFlyoutBase
{
    public event TypedEventHandler<TargetPickerFlyout, EventArgs>? Confirmed;

    public event TypedEventHandler<TargetPickerFlyout, EventArgs>? Dismissed;

    protected override Control CreatePresenter()
    {
        var presenter = new LibraryItemPickerFlyoutPresenter
        {
            ShowReferences = true
        };

        presenter.CloseClicked += OnFlyoutDismissed;
        presenter.Confirmed += OnFlyoutConfirmed;
        presenter.Dismissed += OnFlyoutDismissed;
        presenter.ReferenceItems = viewModel.Items;
        presenter.SelectedItem = viewModel.SelectedItem.Value;
        AvaloniaObjectExtensions.GetObservable(presenter, LibraryItemPickerFlyoutPresenter.SelectedItemProperty)
            .Subscribe(v => viewModel.SelectedItem.Value = v);
        AvaloniaObjectExtensions.GetObservable(presenter, LibraryItemPickerFlyoutPresenter.SearchTextProperty)
            .Subscribe(v => viewModel.SearchText.Value = v);
        presenter.KeyDown += (_, e) =>
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

        return presenter;
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
