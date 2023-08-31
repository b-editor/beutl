using Beutl.Services;
using Beutl.ViewModels.Dialogs;

using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Dialogs;

public sealed partial class SelectFilterEffectType : ContentDialog
{
    public SelectFilterEffectType()
    {
        InitializeComponent();
    }

    protected override Type StyleKeyOverride => typeof(ContentDialog);

    protected override void OnPrimaryButtonClick(ContentDialogButtonClickEventArgs args)
    {
        base.OnPrimaryButtonClick(args);
        if (DataContext is not SelectLibraryItemDialogViewModel vm) return;

        if (carousel.SelectedIndex == 0)
        {
            vm.SelectedItem.Value = listbox1.SelectedItem as LibraryItem;
        }
        else
        {
            vm.SelectedItem.Value = listbox2.SelectedItem as LibraryItem;
        }
    }

    protected override void OnSecondaryButtonClick(ContentDialogButtonClickEventArgs args)
    {
        base.OnSecondaryButtonClick(args);
        if (DataContext is not SelectLibraryItemDialogViewModel vm) return;
        args.Cancel = true;

        if (carousel.SelectedIndex == 1)
        {
            SecondaryButtonText = Strings.ShowMore;
            carousel.Previous();
        }
        else
        {
            SecondaryButtonText = Strings.Back;
            vm.LoadAllItems();
            carousel.Next();
        }
    }
}
