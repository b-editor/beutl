using System;
using System.Reactive.Linq;
using System.ComponentModel;
using Avalonia;
using Avalonia.Input;
using Avalonia.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.ViewModels.Dialogs;
using FluentAvalonia.Core;
using FluentAvalonia.UI.Controls.Primitives;
using Reactive.Bindings.Extensions;

namespace Beutl.Views.Editors;

public sealed class FilterEffectPickerFlyout(SelectLibraryItemDialogViewModel viewModel) : PickerFlyoutBase
{
    public event TypedEventHandler<FilterEffectPickerFlyout, EventArgs> Confirmed;

    public event TypedEventHandler<FilterEffectPickerFlyout, EventArgs> Dismissed;

    protected override Control CreatePresenter()
    {
        var pfp = new FilterEffectPickerFlyoutPresenter();
        pfp.CloseClicked += (_, _) => Hide();
        pfp.Confirmed += OnFlyoutConfirmed;
        pfp.Dismissed += OnFlyoutDismissed;
        pfp.Items = viewModel.Items;
        pfp.GetObservable(FilterEffectPickerFlyoutPresenter.SelectedItemProperty)
            .Subscribe(v => viewModel.SelectedItem.Value = v);
        pfp.GetObservable(FilterEffectPickerFlyoutPresenter.ShowAllProperty)
            .Subscribe(v => viewModel.ShowAll.Value = v);
        pfp.GetObservable(FilterEffectPickerFlyoutPresenter.SearchTextProperty)
            .Subscribe(v => viewModel.SearchText.Value = v);
        viewModel.IsBusy.ObserveOnUIDispatcher()
            .Subscribe(v => pfp.IsBusy = v);
        pfp.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter:
                    OnConfirmed();
                    break;
                case Key.Escape:
                    Hide();
                    break;
            }
        };

        return pfp;
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
