using Beutl.ViewModels.Dialogs;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Dialogs;

public partial class AddOutputProfileDialog : ContentDialog
{
    public AddOutputProfileDialog()
    {
        InitializeComponent();
    }

    protected override Type StyleKeyOverride => typeof(ContentDialog);

    protected override void OnPrimaryButtonClick(ContentDialogButtonClickEventArgs args)
    {
        base.OnPrimaryButtonClick(args);
        if (DataContext is not AddOutputProfileViewModel vm) return;
        vm.Add();
    }
}
