using Beutl.ViewModels.Dialogs;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Dialogs;

public partial class UpdateDialog : FAContentDialog
{
    public UpdateDialog()
    {
        InitializeComponent();
    }

    protected override Type StyleKeyOverride => typeof(FAContentDialog);

    protected override void OnCloseButtonClick(FAContentDialogButtonClickEventArgs args)
    {
        base.OnCloseButtonClick(args);
        if (DataContext is not UpdateDialogViewModel vm) return;
        vm.Cancel();
    }
}
