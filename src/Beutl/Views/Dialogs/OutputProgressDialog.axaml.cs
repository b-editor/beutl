using Beutl.ViewModels;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Dialogs;

public partial class OutputProgressDialog : ContentDialog
{
    public OutputProgressDialog()
    {
        InitializeComponent();
    }

    protected override Type StyleKeyOverride => typeof(ContentDialog);

    protected override void OnCloseButtonClick(ContentDialogButtonClickEventArgs args)
    {
        base.OnCloseButtonClick(args);
        if (DataContext is not OutputViewModel vm) return;
        vm.CancelEncode();
    }
}
