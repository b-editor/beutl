using Beutl.ViewModels.Tools;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Dialogs;

public partial class OutputProgressDialog : FAContentDialog
{
    public OutputProgressDialog()
    {
        InitializeComponent();
    }

    protected override Type StyleKeyOverride => typeof(FAContentDialog);

    private void OnCloseButtonClick(FAContentDialog sender, FAContentDialogButtonClickEventArgs args)
    {
        if (DataContext is OutputViewModel vm && vm.IsEncoding.Value)
        {
            vm.CancelEncode();
        }
    }
}
