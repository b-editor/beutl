using Beutl.ViewModels.Tools;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Dialogs;

public partial class OutputProgressDialog : ContentDialog
{
    public OutputProgressDialog()
    {
        InitializeComponent();
    }

    protected override Type StyleKeyOverride => typeof(ContentDialog);

    private void OnCloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (DataContext is OutputViewModel vm && vm.IsEncoding.Value)
        {
            vm.CancelEncode();
        }
    }
}
