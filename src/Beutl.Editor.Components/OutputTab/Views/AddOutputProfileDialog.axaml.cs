using Beutl.Editor.Components.OutputTab.ViewModels;
using FluentAvalonia.UI.Controls;

namespace Beutl.Editor.Components.OutputTab.Views;

public partial class AddOutputProfileDialog : FAContentDialog
{
    public AddOutputProfileDialog()
    {
        InitializeComponent();
    }

    protected override Type StyleKeyOverride => typeof(FAContentDialog);

    protected override void OnPrimaryButtonClick(FAContentDialogButtonClickEventArgs args)
    {
        base.OnPrimaryButtonClick(args);
        if (DataContext is not AddOutputProfileViewModel vm) return;
        vm.Add();
    }
}
